namespace DurableFunction.FanOutFanIn;

// BatchSize = 0 runs the flat version (one activity per file).
// BatchSize > 0 splits the files into sub-orchestrations of that size.
public record RollupRequest(int BranchCount, int BatchSize);

public record ListFilesRequest(DateTime RunDate, int BranchCount);

// What each fan-out activity returns: a SMALL aggregate, never the file contents.
public record BranchTotals(string BranchId, decimal Revenue, int Transactions);

public record BatchResult(BranchTotals[] Succeeded, string[] FailedFiles);

public record NightlySummary(
    DateTime RunDate,
    int Processed,
    int Failed,
    decimal Revenue,
    int Transactions,
    string[] FailedFiles);
