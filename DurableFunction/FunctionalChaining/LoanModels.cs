namespace DurableFunction.FunctionalChaining;

// Records keep orchestration inputs and outputs small, immutable and JSON-friendly.
// Everything passed to or returned from an activity is serialized into the history table,
// so keep these lean: IDs and numbers, not documents.

public record LoanApplication(string Id, string ApplicantId, decimal Amount);

public record CreditReport(int Score, decimal AnnualIncome);

public record EnrichedApplication(LoanApplication App, int CreditScore, decimal AnnualIncome);

public record ScoredApplication(EnrichedApplication Data, double Risk, string Decision);

public record ReviewRequest(string LoanId, string Reason);
