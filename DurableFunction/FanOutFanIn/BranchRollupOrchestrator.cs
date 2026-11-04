using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace DurableFunction.FanOutFanIn;

// ─────────────────────────────────────────────────────────────────────────────
// PATTERN 2: FAN-OUT / FAN-IN
//
// Start N activities in parallel, wait for all of them, then aggregate:
//
//                  ┌─► ProcessBranchFile(branch-0001) ─┐
//   ListFiles ─────┼─► ProcessBranchFile(branch-0002) ─┼──► WriteSummary
//                  └─► ProcessBranchFile(branch-N)    ─┘
//
// THE TRICK
//   Call CallActivityAsync for every item WITHOUT awaiting each one, collect the tasks,
//   then `await Task.WhenAll(tasks)`. The framework schedules every activity at once,
//   and they run on as many instances as the plan scales to.
//
// BEST PRACTICES SHOWN
//   • Fan out on references (blob names) and fan in on small results (totals).
//     Every result goes into history and is reloaded on EVERY replay:
//     5,000 × 2 KB of results = 10 MB read again on each wake-up.
//   • Pick a partial-failure policy on purpose. Task.WhenAll alone fails the whole
//     run at the first bad file. Here we catch, then check which tasks succeeded.
//   • For very large fan-outs, split the work into sub-orchestrations
//     (NightlyBranchRollupBatched). Each child keeps its own small history.
//   • Fan-out doesn't mean unlimited concurrency. host.json
//     maxConcurrentActivityFunctions caps it per instance, which protects downstream systems.
// ─────────────────────────────────────────────────────────────────────────────
public static class BranchRollupOrchestrator
{
    private static readonly TaskOptions FileRetry = TaskOptions.FromRetryPolicy(new RetryPolicy(
        maxNumberOfAttempts: 3,
        firstRetryInterval: TimeSpan.FromSeconds(1)));

    // FLAT VERSION: one activity per file. Fine for tens or hundreds of items.
    [Function(nameof(NightlyBranchRollup))]
    public static async Task<NightlySummary> NightlyBranchRollup(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger log = context.CreateReplaySafeLogger(nameof(NightlyBranchRollup));
        RollupRequest request = context.GetInput<RollupRequest>()!;
        DateTime runDate = context.CurrentUtcDateTime.Date;   // NOT DateTime.UtcNow

        string[] files = await context.CallActivityAsync<string[]>(
            nameof(BranchActivities.ListBranchFiles), new ListFilesRequest(runDate, request.BranchCount));
        log.LogInformation("Fanning out over {Count} branch files", files.Length);

        BatchResult result = await ProcessFilesAsync(context, files);

        NightlySummary summary = Summarise(runDate, result);
        await context.CallActivityAsync(nameof(BranchActivities.WriteSummary), summary);
        return summary;
    }

    // BATCHED VERSION: parent → N sub-orchestrations → activities.
    // 5,000 files / 500 per batch = the parent's history holds 10 results, not 5,000.
    [Function(nameof(NightlyBranchRollupBatched))]
    public static async Task<NightlySummary> NightlyBranchRollupBatched(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger log = context.CreateReplaySafeLogger(nameof(NightlyBranchRollupBatched));
        RollupRequest request = context.GetInput<RollupRequest>()!;
        DateTime runDate = context.CurrentUtcDateTime.Date;

        string[] files = await context.CallActivityAsync<string[]>(
            nameof(BranchActivities.ListBranchFiles), new ListFilesRequest(runDate, request.BranchCount));

        string[][] batches = files.Chunk(request.BatchSize).ToArray();
        log.LogInformation("Fanning out {Files} files as {Batches} sub-orchestrations", files.Length, batches.Length);

        // Fan out over sub-orchestrations instead of activities: same pattern, one level up.
        List<Task<BatchResult>> batchTasks = batches
            .Select(batch => context.CallSubOrchestratorAsync<BatchResult>(nameof(ProcessBranchBatch), batch))
            .ToList();
        BatchResult[] results = await Task.WhenAll(batchTasks);

        var merged = new BatchResult(
            results.SelectMany(r => r.Succeeded).ToArray(),
            results.SelectMany(r => r.FailedFiles).ToArray());

        NightlySummary summary = Summarise(runDate, merged);
        await context.CallActivityAsync(nameof(BranchActivities.WriteSummary), summary);
        return summary;
    }

    // A sub-orchestrator is a normal orchestrator, started by another orchestrator.
    [Function(nameof(ProcessBranchBatch))]
    public static Task<BatchResult> ProcessBranchBatch(
        [OrchestrationTrigger] TaskOrchestrationContext context) =>
        ProcessFilesAsync(context, context.GetInput<string[]>()!);

    // Shared fan-out/fan-in core. It's still orchestrator code, so it follows the same determinism rules.
    private static async Task<BatchResult> ProcessFilesAsync(TaskOrchestrationContext context, string[] files)
    {
        // FAN OUT: schedule everything first. No await inside the Select!
        List<Task<BranchTotals>> tasks = files
            .Select(file => context.CallActivityAsync<BranchTotals>(
                nameof(BranchActivities.ProcessBranchFile), file, FileRetry))
            .ToList();

        // FAN IN: one await for all of them.
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (TaskFailedException)
        {
            // At least one file failed after its retries. Don't fail the night; check each task below.
        }

        return new BatchResult(
            tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray(),
            files.Where((_, i) => tasks[i].IsFaulted).ToArray());
    }

    private static NightlySummary Summarise(DateTime runDate, BatchResult result) => new(
        runDate,
        Processed: result.Succeeded.Length,
        Failed: result.FailedFiles.Length,
        Revenue: result.Succeeded.Sum(t => t.Revenue),
        Transactions: result.Succeeded.Sum(t => t.Transactions),
        FailedFiles: result.FailedFiles);
}
