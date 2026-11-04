namespace DurableFunction.HumanInteraction;

public record Expense(string Id, string EmployeeId, string ManagerId, decimal Amount, string Description);

// Orchestration input: the expense PLUS the policy values the starter read from config.
// The orchestrator never reads config itself (see the Determinism folder).
public record ExpenseApprovalInput(Expense Expense, decimal AutoApproveLimit, TimeSpan ApprovalTimeout);

public record ApprovalRequest(Expense Expense, string ApproverId, string InstanceId, DateTime DeadlineUtc);

// The payload of the external event.
public record ApprovalDecision(bool Approved, string ApproverId, string? Comment);

public record ExpenseOutcome(string ExpenseId, string Outcome, string DecidedBy, string? Comment);

// Custom status while waiting: tells the decision endpoint who is allowed to answer.
public record ApprovalProgress(string WaitingFor, DateTime DeadlineUtc);
