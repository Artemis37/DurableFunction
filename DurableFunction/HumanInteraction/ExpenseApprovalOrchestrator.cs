using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace DurableFunction.HumanInteraction;

// ─────────────────────────────────────────────────────────────────────────────
// PATTERN 5: HUMAN INTERACTION
//
// Wait for a person, but not forever. Race an external event against a durable timer;
// whichever completes first wins:
//
//                        ┌─ WaitForExternalEvent("ManagerDecision") ─► Approved / Rejected
//   SendApprovalRequest ─┤
//                        └─ CreateTimer(now + 72h) ─────────────────► Escalate
//
//   Task winner = await Task.WhenAny(decisionTask, timeoutTask);
//
// BEST PRACTICES SHOWN
//   • ALWAYS cancel the losing timer. An orchestration can't reach "Completed" while any
//     durable timer is still pending. Without timeoutCts.Cancel(), an expense approved after
//     5 minutes would show as "Running" for 72 hours.
//   • Pass context.InstanceId (or a deterministic ID) to the notification, so the
//     Approve/Reject click knows which instance to signal.
//   • Authorize whoever raises the event (see ExpenseApi). This code trusts every event it receives.
//   • Use a constant for the event name. A typo means waiting until the timeout.
//   • Deadline = context.CurrentUtcDateTime + timeout, so it's identical on every replay.
//   • Escalation could itself be another wait (loop over a chain of approvers).
//
// Shorthand: WaitForExternalEvent<T>(name, timeout) throws when the timeout elapses.
// The explicit WhenAny race below is clearer and is what exam questions show.
// ─────────────────────────────────────────────────────────────────────────────
public static class ExpenseApprovalOrchestrator
{
    public const string ManagerDecisionEvent = "ManagerDecision";

    [Function(nameof(ApproveExpense))]
    public static async Task<string> ApproveExpense(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ILogger log = context.CreateReplaySafeLogger(nameof(ApproveExpense));
        ExpenseApprovalInput input = context.GetInput<ExpenseApprovalInput>()!;
        Expense expense = input.Expense;

        if (expense.Amount <= input.AutoApproveLimit)
        {
            await context.CallActivityAsync(nameof(ExpenseActivities.RecordOutcome),
                new ExpenseOutcome(expense.Id, "AutoApproved", "policy", $"At or under {input.AutoApproveLimit:N0}"));
            return "AutoApproved";
        }

        DateTime deadlineUtc = context.CurrentUtcDateTime.Add(input.ApprovalTimeout);
        context.SetCustomStatus(new ApprovalProgress(expense.ManagerId, deadlineUtc));

        await context.CallActivityAsync(nameof(ExpenseActivities.SendApprovalRequest),
            new ApprovalRequest(expense, expense.ManagerId, context.InstanceId, deadlineUtc));

        // THE RACE
        using var timeoutCts = new CancellationTokenSource();
        Task timeoutTask = context.CreateTimer(deadlineUtc, timeoutCts.Token);
        Task<ApprovalDecision> decisionTask = context.WaitForExternalEvent<ApprovalDecision>(ManagerDecisionEvent);

        Task winner = await Task.WhenAny(decisionTask, timeoutTask);

        if (winner == decisionTask)
        {
            timeoutCts.Cancel();   // ← the line people forget

            ApprovalDecision decision = await decisionTask;   // already complete, so this returns immediately
            string outcome = decision.Approved ? "Approved" : "Rejected";
            log.LogInformation("{ExpenseId} {Outcome} by {Approver}", expense.Id, outcome, decision.ApproverId);

            await context.CallActivityAsync(nameof(ExpenseActivities.RecordOutcome),
                new ExpenseOutcome(expense.Id, outcome, decision.ApproverId, decision.Comment));
            return outcome;
        }

        log.LogWarning("{ExpenseId}: no decision from {Manager} by {Deadline:u}, escalating",
            expense.Id, expense.ManagerId, deadlineUtc);

        await context.CallActivityAsync(nameof(ExpenseActivities.EscalateToFinanceDirector), expense);
        await context.CallActivityAsync(nameof(ExpenseActivities.RecordOutcome),
            new ExpenseOutcome(expense.Id, "Escalated", "system", $"No response within {input.ApprovalTimeout}"));
        return "Escalated";
    }
}
