using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DurableFunction.Monitoring;

public class PartnerFileApi(FakeSftpServer sftp)
{
    // POST /api/monitor/partners: start ONE monitor per partner (deterministic instance ID).
    [Function("StartPartnerFileMonitor")]
    public async Task<HttpResponseData> StartPartnerFileMonitor(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "monitor/partners")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        MonitorRequest? request = await req.ReadFromJsonAsync<MonitorRequest>();
        if (request is null || request.PollIntervalSeconds <= 0 || request.TimeoutMinutes <= 0)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        string instanceId = InstanceIdFor(request.PartnerId);
        OrchestrationMetadata? existing = await client.GetInstanceAsync(instanceId);
        if (existing is null || existing.IsCompleted)
        {
            await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(PartnerFileMonitor.WatchForPartnerFile),
                request,
                new StartOrchestrationOptions { InstanceId = instanceId });
        }

        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    // PUT /api/monitor/partners/{partnerId}/files/{fileName}: SIMULATES the partner
    // uploading the file. The monitor doesn't get a message; it finds the file on its next check.
    [Function("SimulatePartnerUpload")]
    public async Task<HttpResponseData> SimulatePartnerUpload(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "monitor/partners/{partnerId}/files/{fileName}")] HttpRequestData req,
        string partnerId,
        string fileName)
    {
        await sftp.UploadAsync(partnerId, fileName);
        return req.CreateResponse(HttpStatusCode.Created);
    }

    // DELETE /api/monitor/partners/{partnerId}: stop watching early.
    // A monitor is cancellable per instance, which a Timer trigger can't do.
    [Function("StopPartnerFileMonitor")]
    public async Task<HttpResponseData> StopPartnerFileMonitor(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "monitor/partners/{partnerId}")] HttpRequestData req,
        string partnerId,
        [DurableClient] DurableTaskClient client)
    {
        await client.TerminateInstanceAsync(InstanceIdFor(partnerId), "Stopped by operator");
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    private static string InstanceIdFor(string partnerId) => $"monitor-{partnerId}";
}
