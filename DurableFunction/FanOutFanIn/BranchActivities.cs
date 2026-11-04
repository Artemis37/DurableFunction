using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableFunction.FanOutFanIn;

public class BranchActivities(ILogger<BranchActivities> logger)
{
    [Function(nameof(ListBranchFiles))]
    public string[] ListBranchFiles([ActivityTrigger] ListFilesRequest request) =>
        // Real code: list the blobs under "branch-exports/{date}/". Return NAMES, not contents.
        Enumerable.Range(1, request.BranchCount)
            .Select(i => $"branch-exports/{request.RunDate:yyyy-MM-dd}/branch-{i:D4}.csv")
            .ToArray();

    [Function(nameof(ProcessBranchFile))]
    public async Task<BranchTotals> ProcessBranchFile([ActivityTrigger] string blobName)
    {
        // Real code: stream the blob, parse the rows, aggregate. Only the totals leave this function.
        await Task.Delay(Random.Shared.Next(100, 600));

        string branchId = Path.GetFileNameWithoutExtension(blobName);
        int number = int.Parse(branchId[^4..]);

        // Every 13th branch sends a corrupt file, to show partial-failure handling.
        if (number % 13 == 0)
        {
            throw new InvalidDataException($"{blobName} has a malformed header row (simulated).");
        }

        return new BranchTotals(branchId, Revenue: 1_000m + number * 137.5m % 9_000m, Transactions: 50 + number % 200);
    }

    [Function(nameof(WriteSummary))]
    public void WriteSummary([ActivityTrigger] NightlySummary summary) =>
        // Real code: write one row to the reporting DB or drop a summary blob.
        logger.LogInformation(
            "Rollup {RunDate:yyyy-MM-dd}: {Processed} processed, {Failed} failed, revenue {Revenue:N2}, failed files: {FailedFiles}",
            summary.RunDate, summary.Processed, summary.Failed, summary.Revenue, string.Join(", ", summary.FailedFiles));
}
