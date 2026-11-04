using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableFunction.HumanInteraction;

public class ExpenseActivities(ILogger<ExpenseActivities> logger)
{
    [Function(nameof(SendApprovalRequest))]
    public void SendApprovalRequest([ActivityTrigger] ApprovalRequest request) =>
        // Real code: an email or Teams adaptive card whose Approve / Reject buttons
        // POST to /api/expenses/{id}/decision. That URL is how the click gets back to this instance.
        logger.LogInformation(
            "APPROVAL NEEDED from {Approver}: expense {ExpenseId}, {Amount:N2} for '{Description}'. Decide by {Deadline:u}",
            request.ApproverId, request.Expense.Id, request.Expense.Amount, request.Expense.Description, request.DeadlineUtc);

    [Function(nameof(EscalateToFinanceDirector))]
    public void EscalateToFinanceDirector([ActivityTrigger] Expense expense) =>
        logger.LogWarning("ESCALATED expense {ExpenseId} ({Amount:N2}) to the finance director", expense.Id, expense.Amount);

    [Function(nameof(RecordOutcome))]
    public void RecordOutcome([ActivityTrigger] ExpenseOutcome outcome) =>
        // Real code: upsert into the expenses table, keyed on ExpenseId (idempotent).
        logger.LogInformation("Expense {ExpenseId}: {Outcome} by {DecidedBy} ({Comment})",
            outcome.ExpenseId, outcome.Outcome, outcome.DecidedBy, outcome.Comment);
}
