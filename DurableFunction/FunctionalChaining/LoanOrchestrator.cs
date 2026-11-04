using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace DurableFunction.FunctionalChaining;

// ─────────────────────────────────────────────────────────────────────────────
// PATTERN 1: FUNCTION CHAINING
//
// Sequential steps; the output of one step is the input of the next:
//
//     Validate ──► Enrich ──► Score ──► Persist
//
// HOW IT RUNS (replay)
//   The orchestrator doesn't sit in memory waiting. Every `await` on a durable task
//   checkpoints to history and unloads. When the activity finishes, this method runs
//   AGAIN FROM THE TOP, and calls that already have a result in history return it
//   instantly instead of executing again:
//
//     Run 1: Validate → not in history → schedule it, unload
//     Run 2: Validate ✓ (from history) → Enrich → schedule, unload
//     Run 3: Validate ✓ → Enrich ✓ → Score → schedule, unload
//     Run 4: Validate ✓ → Enrich ✓ → Score ✓ → Persist → schedule, unload
//     Run 5: all ✓ → return → Completed
//
//   This method body therefore runs five times for one loan. The replay-safe logger
//   below prints each line once; a normal ILogger would print it on every run.
//
// BEST PRACTICES SHOWN
//   • Retry only TRANSIENT steps (Enrich = external API, Persist = database).
//   • Don't retry Validate: bad input is still bad on attempt four.
//   • Compensation goes in catch (TaskFailedException), which runs after retries are exhausted.
//   • No I/O here. This method only coordinates; LoanActivities does the work.
// ─────────────────────────────────────────────────────────────────────────────
public static class LoanOrchestrator
{
    // Immutable static: safe to share. Mutable static state is NOT safe in orchestrators.
    private static readonly TaskOptions TransientRetry = TaskOptions.FromRetryPolicy(new RetryPolicy(
        maxNumberOfAttempts: 4,
        firstRetryInterval: TimeSpan.FromSeconds(2),
        backoffCoefficient: 2.0));   // waits 2s, 4s, 8s between attempts

    [Function(nameof(ProcessLoan))]
    public static async Task<string> ProcessLoan(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger log = context.CreateReplaySafeLogger(nameof(ProcessLoan));
        LoanApplication app = context.GetInput<LoanApplication>()
            ?? throw new InvalidOperationException("A loan application input is required.");

        try
        {
            log.LogInformation("[{LoanId}] Step 1/4: validate", app.Id);
            LoanApplication valid = await context.CallActivityAsync<LoanApplication>(
                nameof(LoanActivities.Validate), app);

            log.LogInformation("[{LoanId}] Step 2/4: enrich from credit bureau", app.Id);
            EnrichedApplication enriched = await context.CallActivityAsync<EnrichedApplication>(
                nameof(LoanActivities.Enrich), valid, TransientRetry);

            log.LogInformation("[{LoanId}] Step 3/4: score", app.Id);
            ScoredApplication scored = await context.CallActivityAsync<ScoredApplication>(
                nameof(LoanActivities.Score), enriched);

            log.LogInformation("[{LoanId}] Step 4/4: persist decision {Decision}", app.Id, scored.Decision);
            await context.CallActivityAsync(nameof(LoanActivities.Persist), scored, TransientRetry);

            return scored.Decision;
        }
        catch (TaskFailedException ex)
        {
            // Runs ONCE, after the retry policy (if any) has given up on the failing step.
            log.LogWarning("[{LoanId}] {Step} failed: {Error}", app.Id, ex.TaskName, ex.FailureDetails.ErrorMessage);

            await context.CallActivityAsync(nameof(LoanActivities.FlagForManualReview),
                new ReviewRequest(app.Id, $"{ex.TaskName}: {ex.FailureDetails.ErrorMessage}"));
            return "ManualReview";
        }
    }
}
