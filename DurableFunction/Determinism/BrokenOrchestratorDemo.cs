using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DurableFunction.Determinism;

// ─────────────────────────────────────────────────────────────────────────────
// WHAT A DETERMINISM BUG LOOKS LIKE. Never write this.
//
// The orchestrator picks WHICH activity to call from the wall clock. On the first run it
// might schedule StepA. On replay the clock has moved, so it might "decide" StepB, but
// history says StepA was scheduled at that position. The replay no longer matches the
// recorded history, and the framework fails the instance with a non-determinism error.
//
// Run it a few times (Determinism.http). Expect nearly every run to fail with
// "Non-Deterministic workflow detected: ... scheduled an activity task ... named 'StepA', but
// the current orchestration replay instead scheduled ... 'StepB'". Because the choice is a coin
// flip, a run occasionally survives by luck. That's why these bugs can slip through testing.
//
// The fix: make the decision in an activity (its result is recorded once and replayed),
// or base it on context.CurrentUtcDateTime or the orchestration input.
// ─────────────────────────────────────────────────────────────────────────────
public static class BrokenOrchestratorDemo
{
    [Function(nameof(BrokenOrchestrator))]
    public static async Task<string> BrokenOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var path = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            // BUG: a non-deterministic value decides the control flow.
            // The analyzer catches this at build time (DURABLE0001); suppressed only for the demo.
#pragma warning disable DURABLE0001
            string activity = DateTime.UtcNow.Millisecond % 2 == 0 ? nameof(StepA) : nameof(StepB);
#pragma warning restore DURABLE0001
            path.Add(await context.CallActivityAsync<string>(activity, i));
        }
        return string.Join(" → ", path);
    }

    [Function(nameof(StepA))]
    public static string StepA([ActivityTrigger] int i) => $"A{i}";

    [Function(nameof(StepB))]
    public static string StepB([ActivityTrigger] int i) => $"B{i}";

    [Function("StartBrokenOrchestrator")]
    public static async Task<HttpResponseData> StartBrokenOrchestrator(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "determinism/broken")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(BrokenOrchestrator));
        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }
}
