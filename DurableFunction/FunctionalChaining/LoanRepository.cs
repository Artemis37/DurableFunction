using System.Collections.Concurrent;

namespace DurableFunction.FunctionalChaining;

public interface ILoanRepository
{
    Task UpsertAsync(string loanId, ScoredApplication decision);
}

// Stand-in for Cosmos DB / SQL. Upsert semantics are what make the Persist activity idempotent.
public class InMemoryLoanRepository : ILoanRepository
{
    private readonly ConcurrentDictionary<string, ScoredApplication> _loans = new();

    public Task UpsertAsync(string loanId, ScoredApplication decision)
    {
        _loans[loanId] = decision;
        return Task.CompletedTask;
    }
}
