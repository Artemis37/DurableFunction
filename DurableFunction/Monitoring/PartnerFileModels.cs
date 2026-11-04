namespace DurableFunction.Monitoring;

// Polling settings travel IN THE INPUT, so the orchestrator never reads config itself.
// Production: every 15 minutes for 24 hours. The .http file uses 10 seconds for 2 minutes.
public record MonitorRequest(string PartnerId, string FileName, int PollIntervalSeconds, int TimeoutMinutes);

public record MonitorProgress(int Checks, DateTime LastCheckUtc, DateTime ExpiresUtc);
