using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DurableFunction.FanOutFanIn;

public static class BranchRollupStarter
{
    // Production trigger: every night at 02:00 UTC, 5,000 files in batches of 500.
    // It's disabled locally through local.settings.json ("AzureWebJobs.NightlyRollupTimer.Disabled").
    [Function("NightlyRollupTimer")]
    public static async Task NightlyRollupTimer(
        [TimerTrigger("0 0 2 * * *")] TimerInfo timer,
        [DurableClient] DurableTaskClient client,
        FunctionContext executionContext)
    {
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(BranchRollupOrchestrator.NightlyBranchRollupBatched),
            new RollupRequest(BranchCount: 5_000, BatchSize: 500));

        executionContext.GetLogger(nameof(NightlyRollupTimer))
            .LogInformation("Started nightly rollup {InstanceId}", instanceId);
    }

    // Manual trigger, so you can try it locally without waiting for 02:00.
    [Function("StartBranchRollup")]
    public static async Task<HttpResponseData> StartBranchRollup(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "rollup")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        RollupRequest? request = await req.ReadFromJsonAsync<RollupRequest>();
        if (request is null || request.BranchCount <= 0)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        string orchestrator = request.BatchSize > 0
            ? nameof(BranchRollupOrchestrator.NightlyBranchRollupBatched)
            : nameof(BranchRollupOrchestrator.NightlyBranchRollup);

        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestrator, request);
        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }
}
