using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DurableFunction.Determinism;

// ─────────────────────────────────────────────────────────────────────────────
// THE DETERMINISM RULE: run this and watch the console.
//
// An orchestrator REPLAYS from the top every time it wakes up (after each activity, timer
// or event), fast-forwarding through results already recorded in history. So the code
// MUST take the same path with the same values on every replay.
//
//   BAD (inside an orchestrator)          GOOD
//   ───────────────────────────────────   ──────────────────────────────────────────────
//   DateTime.Now / DateTime.UtcNow        context.CurrentUtcDateTime
//   Guid.NewGuid()                        context.NewGuid()
//   new Random() / Random.Shared          generate in an activity, or seed from the input
//   HttpClient, SQL, Blob, file I/O       context.CallActivityAsync(...)
//   Environment variables, IConfiguration read in the starter and pass as input, or read in an activity
//   Thread.Sleep / Task.Delay             context.CreateTimer(...)
//   Task.Run, ConfigureAwait(false)       just await the context.* tasks (single-threaded)
//   static mutable fields                 orchestration input / output
//   ILogger from DI / FunctionContext     context.CreateReplaySafeLogger(...)
//
// The Microsoft.DurableTask.Analyzers package (in the .csproj) flags many of these at BUILD
// time: DURABLE0001 (DateTime), DURABLE0002 (Guid.NewGuid), DURABLE0010 (non-replay-safe logger)...
// Remove the #pragma lines below to see the warnings.
//
// WHAT THIS DEMO SHOWS
//   3 activities + 1 durable timer = the method body runs 5 times. On each run it logs
//   DateTime.UtcNow and Guid.NewGuid(), which differ EVERY time, next to
//   context.CurrentUtcDateTime and context.NewGuid(), which are the SAME on every replay.
//
// VERSIONING: the same rule applies to DEPLOYMENTS
//   Adding, removing or reordering awaited calls changes the replay path, so instances
//   in flight fail when they resume. For breaking changes, deploy side by side (new
//   orchestrator name or new task hub), or let running instances finish first.
// ─────────────────────────────────────────────────────────────────────────────
public static class ReplayDemo
{
    [Function(nameof(ReplayDemoOrchestrator))]
    public static async Task<string> ReplayDemoOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        FunctionContext executionContext)
    {
        // NOT replay-safe: prints on every replay. Used here only to make the replays visible.
        ILogger everyRunLog = executionContext.GetLogger(nameof(ReplayDemoOrchestrator));
        // Replay-safe: suppressed while context.IsReplaying is true, so each line prints once.
        ILogger onceLog = context.CreateReplaySafeLogger(nameof(ReplayDemoOrchestrator));

        // Config arrives as INPUT, decided by the starter: same value on every replay.
        string label = context.GetInput<string>() ?? "demo";

        // BAD: different on every replay. Only logged here, never used for a decision.
        // The analyzer catches these at build time; suppressed only because the demo needs them.
#pragma warning disable DURABLE0001, DURABLE0002
        DateTime wallClock = DateTime.UtcNow;
        Guid randomGuid = Guid.NewGuid();
#pragma warning restore DURABLE0001, DURABLE0002

        // GOOD: identical on every replay.
        DateTime replayClock = context.CurrentUtcDateTime;
        Guid replayGuid = context.NewGuid();

#pragma warning disable DURABLE0010 // deliberate: a non-replay-safe logger, so every replay shows up
        everyRunLog.LogInformation(
            "[{Label}] IsReplaying={IsReplaying} | DateTime.UtcNow={Wall:HH:mm:ss.fff} vs CurrentUtcDateTime={Replay:HH:mm:ss.fff} | Guid.NewGuid={RandomGuid} vs context.NewGuid={ReplayGuid}",
            label, context.IsReplaying, wallClock, replayClock, randomGuid.ToString()[..8], replayGuid.ToString()[..8]);
#pragma warning restore DURABLE0010

        var results = new List<string>();
        for (int step = 1; step <= 3; step++)
        {
            onceLog.LogInformation("[{Label}] Calling step {Step} (replay-safe log, printed once)", label, step);
            results.Add(await context.CallActivityAsync<string>(nameof(ReplayDemoStep), step));
        }

        onceLog.LogInformation("[{Label}] Waiting 2 seconds on a durable timer (not Task.Delay)", label);
        await context.CreateTimer(TimeSpan.FromSeconds(2), CancellationToken.None);

        return $"{string.Join("; ", results)} | context.NewGuid() = {replayGuid}";
    }

    [Function(nameof(ReplayDemoStep))]
    public static async Task<string> ReplayDemoStep([ActivityTrigger] int step)
    {
        await Task.Delay(500);   // fine: activities don't replay
        // Fine too: this runs once, and the string is recorded in history, so replays read it from there.
        return $"step {step} ran at {DateTime.UtcNow:HH:mm:ss.fff}";
    }

    [Function("StartReplayDemo")]
    public static async Task<HttpResponseData> StartReplayDemo(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "determinism/replay-demo")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ReplayDemoOrchestrator), "replay-demo");
        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }
}
