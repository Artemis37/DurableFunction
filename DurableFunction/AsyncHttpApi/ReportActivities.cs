using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableFunction.AsyncHttpApi;

// In production each step takes about 2 minutes (6 in total). Here each one takes 8 seconds,
// so you have time to poll the status endpoint and watch the progress change.
public class ReportActivities(ILogger<ReportActivities> logger)
{
    private static readonly TimeSpan SimulatedWork = TimeSpan.FromSeconds(8);

    [Function(nameof(QueryReportData))]
    public async Task<string> QueryReportData([ActivityTrigger] ReportRequest request)
    {
        logger.LogInformation("Querying {Region} data {From} to {To}", request.Region, request.From, request.To);
        await Task.Delay(SimulatedWork);
        return $"report-data/{request.ReportId}.json";   // where the intermediate data was written
    }

    [Function(nameof(RenderPdf))]
    public async Task<string> RenderPdf([ActivityTrigger] string dataBlob)
    {
        logger.LogInformation("Rendering PDF from {DataBlob}", dataBlob);
        await Task.Delay(SimulatedWork);
        return dataBlob.Replace("report-data/", "reports/").Replace(".json", ".pdf");
    }

    [Function(nameof(CreateDownloadLink))]
    public async Task<string> CreateDownloadLink([ActivityTrigger] string pdfBlob)
    {
        await Task.Delay(SimulatedWork);
        // DateTime.UtcNow is fine here: activities don't replay.
        DateTime expires = DateTime.UtcNow.AddHours(24);
        return $"https://<storage-account>.blob.core.windows.net/{pdfBlob}?se={expires:yyyy-MM-ddTHH:mm:ssZ}&sig=<sas>";
    }
}
