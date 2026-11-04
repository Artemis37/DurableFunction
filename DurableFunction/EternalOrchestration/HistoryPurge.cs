using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DurableFunction.EternalOrchestration;

// HOUSEKEEPING. ContinueAsNew keeps ONE instance's history small, but finished instances
// (every loan, report, expense...) keep their history rows in the task hub FOREVER unless
// you purge them. Run this daily.
public static class HistoryPurge
{
    [Function("PurgeOldInstances")]
    public static async Task PurgeOldInstances(
        [TimerTrigger("0 0 3 * * *")] TimerInfo timer,   // 03:00 UTC daily
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        PurgeResult result = await client.PurgeAllInstancesAsync(new PurgeInstancesFilter(
            CreatedFrom: DateTimeOffset.MinValue,
            CreatedTo: DateTimeOffset.UtcNow.AddDays(-30),
            // Only finished instances: never purge Running / Pending / ContinuedAsNew.
            Statuses: [OrchestrationRuntimeStatus.Completed, OrchestrationRuntimeStatus.Failed, OrchestrationRuntimeStatus.Terminated]));

        executionContext.GetLogger(nameof(PurgeOldInstances))
            .LogInformation("Purged {Count} finished instances older than 30 days", result.PurgedInstanceCount);
    }
}
