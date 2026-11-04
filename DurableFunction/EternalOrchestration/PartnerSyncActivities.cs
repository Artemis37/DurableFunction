using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableFunction.EternalOrchestration;

public class PartnerSyncActivities(ILogger<PartnerSyncActivities> logger)
{
    [Function(nameof(SyncSince))]
    public async Task<SyncResult> SyncSince([ActivityTrigger] SyncState state)
    {
        // Real code: GET {partnerApi}/changes?since={cursor}, upsert each change, return the new cursor.
        await Task.Delay(300);
        int items = Random.Shared.Next(0, 25);
        logger.LogInformation("Pulled {Items} changes for {PartnerId} since {Cursor}",
            items, state.PartnerId, state.Cursor ?? "(start)");

        return new SyncResult(NextCursor: items > 0 ? $"c{state.Generation + 1:D6}" : null, items);
    }
}
