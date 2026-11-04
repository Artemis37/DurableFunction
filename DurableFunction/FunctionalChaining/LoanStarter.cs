using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;

namespace DurableFunction.FunctionalChaining;

// CLIENT (starter) function.
// Any trigger (HTTP, queue, timer, Service Bus...) plus a [DurableClient] binding.
// It's the only kind of function that can START an orchestration, query it, raise events
// to it or terminate it. It is a normal function, so it has no determinism rules.
public static class LoanStarter
{
    [Function("SubmitLoan")]
    public static async Task<HttpResponseData> SubmitLoan(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "loans")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        LoanApplication? app = await req.ReadFromJsonAsync<LoanApplication>();
        if (app is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        // Returns as soon as the start message is queued; it does not wait for the steps.
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(LoanOrchestrator.ProcessLoan), app);

        // 202 Accepted + statusQueryGetUri (see the AsyncHttpApi folder for the details).
        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }
}
