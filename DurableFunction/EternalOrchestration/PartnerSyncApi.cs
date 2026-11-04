using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DurableFunction.EternalOrchestration;

public static class PartnerSyncApi
{
    // POST /api/eternal/partners/{partnerId}/start[?intervalSeconds=15]
    // SINGLETON: one fixed instance ID per partner. Calling start twice (or an app restart
    // that calls it again) doesn't create a second copy of the forever-loop.
    [Function("StartPartnerSync")]
    public static async Task<HttpResponseData> StartPartnerSync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "eternal/partners/{partnerId}/start")] HttpRequestData req,
        string partnerId,
        [DurableClient] DurableTaskClient client)
    {
        int intervalSeconds = int.TryParse(req.Query["intervalSeconds"], out int s) && s > 0 ? s : 15 * 60;

        string instanceId = InstanceIdFor(partnerId);
        OrchestrationMetadata? existing = await client.GetInstanceAsync(instanceId);

        // Running, Pending and ContinuedAsNew all mean "already alive". Only start if it's gone.
        if (existing is null || existing.IsCompleted)
        {
            await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(PartnerSyncOrchestrator.PartnerSyncEternal),
                new SyncState(partnerId, Cursor: null, Generation: 1, intervalSeconds),
                new StartOrchestrationOptions { InstanceId = instanceId });
        }

        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    // POST /api/eternal/partners/{partnerId}/stop: the only way an eternal orchestration ends.
    [Function("StopPartnerSync")]
    public static async Task<HttpResponseData> StopPartnerSync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "eternal/partners/{partnerId}/stop")] HttpRequestData req,
        string partnerId,
        [DurableClient] DurableTaskClient client)
    {
        await client.TerminateInstanceAsync(InstanceIdFor(partnerId), "Stopped by operator");
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    private static string InstanceIdFor(string partnerId) => $"partner-sync-{partnerId}";
}
