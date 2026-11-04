using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace DurableFunction.EternalOrchestration;

// ─────────────────────────────────────────────────────────────────────────────
// ETERNAL ORCHESTRATIONS: ContinueAsNew
//
// An orchestration that never ends, e.g. "sync partner data every 15 minutes, forever".
//
// WRONG:   while (true) { await Sync(); await CreateTimer(+15 min); }
//   Every iteration APPENDS events to history (TaskScheduled, TaskCompleted, TimerCreated,
//   TimerFired...). After a month that's ~3,000 iterations, and EVERY wake-up replays ALL
//   of them: slower each time, with unbounded storage growth.
//
// RIGHT:   do ONE iteration, then context.ContinueAsNew(newState)
//   The instance restarts with a fresh, EMPTY history, carrying only the state you pass in.
//   It keeps the same instance ID, so status queries, RaiseEvent and Terminate still work.
//
//   Generation 1: [Sync, Timer] → ContinueAsNew(cursor=c000001) ─┐
//   Generation 2: [Sync, Timer] → ContinueAsNew(cursor=c000002) ─┤  history never grows
//   Generation 3: [Sync, Timer] → ...                           ─┘  beyond one iteration
//
// BEST PRACTICES SHOWN
//   • Carry MINIMAL state (a cursor). Accumulating lists in it just moves the growth into the input.
//   • ContinueAsNew is the LAST statement. The restart happens when the method returns.
//   • Singleton: a fixed instance ID + "start only if not already running" (PartnerSyncApi).
//   • Stop it with TerminateInstanceAsync.
//   • Completed instances' history is never deleted automatically, so purge it on a schedule (HistoryPurge.cs).
// ─────────────────────────────────────────────────────────────────────────────
public static class PartnerSyncOrchestrator
{
    [Function(nameof(PartnerSyncEternal))]
    public static async Task PartnerSyncEternal(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger log = context.CreateReplaySafeLogger(nameof(PartnerSyncEternal));
        SyncState state = context.GetInput<SyncState>()!;

        SyncResult result = await context.CallActivityAsync<SyncResult>(nameof(PartnerSyncActivities.SyncSince), state);
        string? cursor = result.NextCursor ?? state.Cursor;

        log.LogInformation("{PartnerId} generation {Generation}: synced {Items} items, cursor now {Cursor}",
            state.PartnerId, state.Generation, result.ItemsSynced, cursor);

        DateTime nextRunUtc = context.CurrentUtcDateTime.AddSeconds(state.IntervalSeconds);
        context.SetCustomStatus(new { state.Generation, Cursor = cursor, NextRunUtc = nextRunUtc });

        await context.CreateTimer(nextRunUtc, CancellationToken.None);

        // Start over with an empty history and the new state. Nothing after this line.
        context.ContinueAsNew(state with { Cursor = cursor, Generation = state.Generation + 1 });
    }
}
