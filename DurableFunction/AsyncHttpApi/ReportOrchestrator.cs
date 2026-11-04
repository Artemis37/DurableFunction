using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace DurableFunction.AsyncHttpApi;

// ─────────────────────────────────────────────────────────────────────────────
// PATTERN 3: ASYNC HTTP APIs
//
// A long job behind HTTP. The caller gets 202 Accepted plus a status URL immediately,
// then polls until it gets 200 with the result:
//
//   POST /api/reports ───────────► 202  Location: …/instances/report-q3-emea
//   GET  status URL   ───────────► 202  { runtimeStatus: Running, customStatus: { Rendering, 50 } }
//   GET  status URL   ───────────► 200  { runtimeStatus: Completed, output: "https://…/q3-emea.pdf" }
//
// WHY YOU NEED IT
//   An HTTP-triggered function can't hold a request open for 6 minutes. Azure's front-end
//   load balancer drops idle requests after 230 SECONDS, whatever functionTimeout says.
//
// BEST PRACTICES SHOWN
//   • SetCustomStatus for progress. It's free and appears in every status response.
//   • Split the long job into several activities. Each ACTIVITY is still bound by
//     functionTimeout (5 min by default on Consumption). Durable gets around the HTTP
//     limit, not the per-execution limit.
//   • Return the result BY REFERENCE (a URL), not the PDF bytes. The output is stored
//     in history and returned on every status poll.
//   • Deterministic instance IDs make the start idempotent (see ReportApi).
//   • Don't hand the built-in management URLs to untrusted callers (see ReportApi).
// ─────────────────────────────────────────────────────────────────────────────
public static class ReportOrchestrator
{
    [Function(nameof(GenerateReport))]
    public static async Task<string> GenerateReport(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        ReportRequest request = context.GetInput<ReportRequest>()!;

        context.SetCustomStatus(new ReportProgress("Querying", 10));
        string dataBlob = await context.CallActivityAsync<string>(nameof(ReportActivities.QueryReportData), request);

        context.SetCustomStatus(new ReportProgress("Rendering", 50));
        string pdfBlob = await context.CallActivityAsync<string>(nameof(ReportActivities.RenderPdf), dataBlob);

        context.SetCustomStatus(new ReportProgress("Publishing", 90));
        string downloadUrl = await context.CallActivityAsync<string>(nameof(ReportActivities.CreateDownloadLink), pdfBlob);

        context.SetCustomStatus(new ReportProgress("Done", 100));
        return downloadUrl;
    }
}
