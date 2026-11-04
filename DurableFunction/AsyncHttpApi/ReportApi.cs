using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DurableFunction.AsyncHttpApi;

public static class ReportApi
{
    // POST /api/reports → 202 Accepted straight away; the report is built in the background.
    [Function("StartReport")]
    public static async Task<HttpResponseData> StartReport(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "reports")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        ReportRequest? request = await req.ReadFromJsonAsync<ReportRequest>();
        if (request is null || string.IsNullOrWhiteSpace(request.ReportId))
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        // BEST PRACTICE: a deterministic instance ID makes the submit idempotent.
        // A double-click or a client retry finds the existing instance instead of
        // starting a second 6-minute report. A failed or terminated one may be retried.
        string instanceId = InstanceIdFor(request.ReportId);
        OrchestrationMetadata? existing = await client.GetInstanceAsync(instanceId);
        bool canStart = existing is null
            || existing.RuntimeStatus is OrchestrationRuntimeStatus.Failed or OrchestrationRuntimeStatus.Terminated;

        if (canStart)
        {
            await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(ReportOrchestrator.GenerateReport),
                request,
                new StartOrchestrationOptions { InstanceId = instanceId });
        }

        // Built-in response: 202 + Location header + a body of management URLs:
        // StatusQueryGetUri, SendEventPostUri, TerminatePostUri, PurgeHistoryDeleteUri (plus
        // Rewind/Restart/Suspend/Resume). The docs show them camelCase; the isolated worker
        // returns them PascalCase, because its default JSON serializer has no naming policy.
        // ⚠ In Azure those URLs contain the extension's system key (?code=...). Anyone holding
        //   them can terminate or purge the instance. Fine for trusted internal callers; for
        //   public clients, return a link to GetReportStatus below instead.
        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    // GET /api/reports/{reportId}: your own status endpoint, safe to give external callers.
    // The polling contract is the same: 202 + Retry-After while running, 200 when done.
    [Function("GetReportStatus")]
    public static async Task<HttpResponseData> GetReportStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "reports/{reportId}")] HttpRequestData req,
        string reportId,
        [DurableClient] DurableTaskClient client)
    {
        // Real code: authorize here, e.g. check the caller's Entra ID claims own this report.
        OrchestrationMetadata? meta = await client.GetInstanceAsync(InstanceIdFor(reportId), getInputsAndOutputs: true);
        if (meta is null)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        var status = new ReportStatus(
            reportId,
            meta.RuntimeStatus.ToString(),
            meta.ReadCustomStatusAs<ReportProgress>(),
            meta.RuntimeStatus == OrchestrationRuntimeStatus.Completed ? meta.ReadOutputAs<string>() : null,
            meta.FailureDetails?.ErrorMessage);

        HttpResponseData response = req.CreateResponse(meta.IsCompleted ? HttpStatusCode.OK : HttpStatusCode.Accepted);
        if (!meta.IsCompleted)
        {
            response.Headers.Add("Retry-After", "5");   // tells well-behaved clients how often to poll
        }
        await response.WriteAsJsonAsync(status);
        return response;
    }

    private static string InstanceIdFor(string reportId) => $"report-{reportId}";
}
