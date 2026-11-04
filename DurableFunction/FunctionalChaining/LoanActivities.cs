using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableFunction.FunctionalChaining;

// ACTIVITY functions: where the actual work happens. They behave like normal functions:
//   • They CAN do I/O, call HTTP, read config, and use DateTime.UtcNow, Random and Guid.NewGuid().
//   • They run AT LEAST ONCE, so make them idempotent (see Persist).
//   • They're still bound by functionTimeout (5 min by default on Consumption).
//   • Constructor injection works (ILoanRepository is registered in Program.cs).
public class LoanActivities(ILoanRepository repository, ILogger<LoanActivities> logger)
{
    [Function(nameof(Validate))]
    public LoanApplication Validate([ActivityTrigger] LoanApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.ApplicantId))
        {
            throw new ArgumentException("ApplicantId is required.");
        }
        if (app.Amount is <= 0 or > 1_000_000m)
        {
            throw new ArgumentOutOfRangeException(nameof(app.Amount), app.Amount, "Amount must be between 0 and 1,000,000.");
        }
        return app;
    }

    [Function(nameof(Enrich))]
    public async Task<EnrichedApplication> Enrich([ActivityTrigger] LoanApplication app)
    {
        // Real code: await http.GetFromJsonAsync<CreditReport>($"reports/{app.ApplicantId}")
        await Task.Delay(500);   // Task.Delay is fine in an activity, never in an orchestrator

        // Simulated flaky dependency: fails ~30% of the time so you can watch the retry policy work.
        if (Random.Shared.NextDouble() < 0.3)
        {
            logger.LogWarning("Credit bureau timed out for {ApplicantId} (simulated)", app.ApplicantId);
            throw new HttpRequestException("Credit bureau timed out (simulated transient failure).");
        }

        CreditReport report = FakeCreditBureau(app.ApplicantId);
        return new EnrichedApplication(app, report.Score, report.AnnualIncome);
    }

    // Gotcha: don't call an activity's input parameter "data". It compiles, but the host
    // fails to index the function at startup ("Can't bind parameter 'data' to type 'System.String'").
    [Function(nameof(Score))]
    public ScoredApplication Score([ActivityTrigger] EnrichedApplication enriched)
    {
        double creditRisk = (850 - enriched.CreditScore) / 550.0;
        double affordability = Math.Min((double)(enriched.App.Amount / Math.Max(enriched.AnnualIncome, 1m)), 1.0);
        double risk = Math.Round(creditRisk * 0.6 + affordability * 0.4, 3);

        string decision = risk switch
        {
            < 0.35 => "Approved",
            < 0.60 => "Refer",
            _ => "Declined",
        };
        return new ScoredApplication(enriched, risk, decision);
    }

    [Function(nameof(Persist))]
    public async Task Persist([ActivityTrigger] ScoredApplication scored)
    {
        // UPSERT keyed on the loan ID. If the host crashes after this write but before the
        // completion is checkpointed, the activity runs again and simply overwrites the row.
        // An INSERT here would create a duplicate.
        await repository.UpsertAsync(scored.Data.App.Id, scored);
        logger.LogInformation("Saved loan {LoanId}: {Decision} (risk {Risk})",
            scored.Data.App.Id, scored.Decision, scored.Risk);
    }

    [Function(nameof(FlagForManualReview))]
    public void FlagForManualReview([ActivityTrigger] ReviewRequest request) =>
        logger.LogWarning("Loan {LoanId} sent to manual review: {Reason}", request.LoanId, request.Reason);

    // Same applicant always gets the same score, so repeated runs are comparable.
    private static CreditReport FakeCreditBureau(string applicantId)
    {
        int seed = applicantId.Aggregate(17, (hash, c) => unchecked(hash * 31 + c));
        var rng = new Random(seed);
        return new CreditReport(Score: rng.Next(300, 851), AnnualIncome: rng.Next(20, 150) * 1_000m);
    }
}
