namespace DurableFunction.AsyncHttpApi;

public record ReportRequest(string ReportId, string Region, DateOnly From, DateOnly To);

// Written with context.SetCustomStatus, read back by the status endpoint.
public record ReportProgress(string Stage, int Percent);

// What our own status endpoint returns. No system keys or management URLs in it.
public record ReportStatus(string ReportId, string Status, ReportProgress? Progress, string? DownloadUrl, string? Error);
