using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace DurableFunction.Monitoring;

// ─────────────────────────────────────────────────────────────────────────────
// PATTERN 4: MONITOR
//
// A durable polling loop: check → sleep (durable timer) → check … until a condition
// is met or a deadline passes.
//
//   ┌──► CheckSftpForFile ── found? ──yes──► ImportPartnerFile ──► done (true)
//   │          │ no
//   │          ▼
//   └── CreateTimer(+15 min) ◄── still before the 24h deadline?
//                                        │ no
//                                        ▼
//                                     AlertOps ──► done (false)
//
// MONITOR vs TIMER TRIGGER (a common exam question)
//   Timer trigger: ONE global CRON schedule, no memory between runs, no stop condition.
//   Monitor: one instance PER THING being watched (partner, job, file), with its own state,
//   its own exit condition and its own deadline. You can start it on demand and terminate it.
//
// BEST PRACTICES SHOWN
//   • Compute the deadline from context.CurrentUtcDateTime, never DateTime.UtcNow.
//     CurrentUtcDateTime replays the SAME value, so the deadline doesn't move.
//   • Use context.CreateTimer, never Task.Delay or Thread.Sleep. While a durable timer
//     waits, the orchestrator is unloaded: no memory, no CPU, no bill on Consumption.
//   • A BOUNDED loop is fine: 24h / 15 min = 96 iterations, a few hundred history events.
//     An UNBOUNDED monitor must use ContinueAsNew (see the EternalOrchestration folder).
//   • Keep the check activity cheap and side-effect-free; do the work in a separate activity.
//   • The interval is just code, so you can add backoff (e.g. 5 min at first, then 30 min).
// ─────────────────────────────────────────────────────────────────────────────
public static class PartnerFileMonitor
{
    [Function(nameof(WatchForPartnerFile))]
    public static async Task<bool> WatchForPartnerFile(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger log = context.CreateReplaySafeLogger(nameof(WatchForPartnerFile));
        MonitorRequest watch = context.GetInput<MonitorRequest>()!;

        DateTime expiresUtc = context.CurrentUtcDateTime.AddMinutes(watch.TimeoutMinutes);
        TimeSpan pollInterval = TimeSpan.FromSeconds(watch.PollIntervalSeconds);
        int checks = 0;

        while (context.CurrentUtcDateTime < expiresUtc)
        {
            checks++;
            bool landed = await context.CallActivityAsync<bool>(nameof(PartnerFileActivities.CheckSftpForFile), watch);
            context.SetCustomStatus(new MonitorProgress(checks, context.CurrentUtcDateTime, expiresUtc));

            if (landed)
            {
                log.LogInformation("{PartnerId}: {FileName} found on check {Checks}", watch.PartnerId, watch.FileName, checks);
                await context.CallActivityAsync(nameof(PartnerFileActivities.ImportPartnerFile), watch);
                return true;
            }

            log.LogInformation("{PartnerId}: not there yet (check {Checks}), sleeping {Interval}",
                watch.PartnerId, checks, pollInterval);

            // Durable timer: a "TimerCreated" row goes into history and the orchestrator unloads.
            // A "TimerFired" message wakes it up again, even after a host restart.
            await context.CreateTimer(context.CurrentUtcDateTime.Add(pollInterval), CancellationToken.None);
        }

        await context.CallActivityAsync(nameof(PartnerFileActivities.AlertOps),
            $"No {watch.FileName} from {watch.PartnerId} within {watch.TimeoutMinutes} minutes ({checks} checks).");
        return false;
    }
}
