using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace DurableFunction.HumanInteraction;

public static class ExpenseApi
{
    // POST /api/expenses[?timeoutSeconds=60]: submit an expense for approval.
    [Function("SubmitExpense")]
    public static async Task<HttpResponseData> SubmitExpense(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "expenses")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        Expense? expense = await req.ReadFromJsonAsync<Expense>();
        if (expense is null || string.IsNullOrWhiteSpace(expense.Id))
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        // Config is read HERE, in a client function that never replays, and passed in as input.
        // Reading it inside the orchestrator would break determinism if it changed mid-flight.
        decimal autoApproveLimit = decimal.TryParse(
            Environment.GetEnvironmentVariable("ExpenseAutoApproveLimit"),
            NumberStyles.Number, CultureInfo.InvariantCulture, out decimal limit) ? limit : 5_000m;

        // Production: 72 hours. ?timeoutSeconds=60 shortens it for the demo.
        TimeSpan approvalTimeout = int.TryParse(req.Query["timeoutSeconds"], out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromHours(72);

        string instanceId = InstanceIdFor(expense.Id);
        if (await client.GetInstanceAsync(instanceId) is null)   // idempotent submit
        {
            await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(ExpenseApprovalOrchestrator.ApproveExpense),
                new ExpenseApprovalInput(expense, autoApproveLimit, approvalTimeout),
                new StartOrchestrationOptions { InstanceId = instanceId });
        }

        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    // POST /api/expenses/{expenseId}/decision: what the Approve / Reject buttons call.
    [Function("SubmitExpenseDecision")]
    public static async Task<HttpResponseData> SubmitExpenseDecision(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "expenses/{expenseId}/decision")] HttpRequestData req,
        string expenseId,
        [DurableClient] DurableTaskClient client)
    {
        ApprovalDecision? decision = await req.ReadFromJsonAsync<ApprovalDecision>();
        if (decision is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        OrchestrationMetadata? instance = await client.GetInstanceAsync(InstanceIdFor(expenseId), getInputsAndOutputs: true);
        if (instance is null)
        {
            return await TextAsync(req, HttpStatusCode.NotFound, "No such expense.");
        }
        if (instance.RuntimeStatus != OrchestrationRuntimeStatus.Running)
        {
            return await TextAsync(req, HttpStatusCode.Conflict, $"Expense is already {instance.RuntimeStatus}.");
        }

        // AUTHORIZATION. The orchestrator trusts whatever event arrives, so check here.
        // Real code: take the approver's identity from the caller's Entra ID token, not the request body.
        ApprovalProgress? progress = instance.ReadCustomStatusAs<ApprovalProgress>();
        if (progress is null || progress.WaitingFor != decision.ApproverId)
        {
            return await TextAsync(req, HttpStatusCode.Forbidden, $"{decision.ApproverId} can't decide on this expense.");
        }

        // Delivered to the orchestrator's WaitForExternalEvent. If it arrived before the
        // orchestrator started waiting, it would be buffered rather than lost.
        await client.RaiseEventAsync(instance.InstanceId, ExpenseApprovalOrchestrator.ManagerDecisionEvent, decision);
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    private static string InstanceIdFor(string expenseId) => $"expense-{expenseId}";

    private static async Task<HttpResponseData> TextAsync(HttpRequestData req, HttpStatusCode code, string message)
    {
        HttpResponseData response = req.CreateResponse(code);
        await response.WriteStringAsync(message);
        return response;
    }
}
