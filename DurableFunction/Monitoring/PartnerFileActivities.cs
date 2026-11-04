using System.Collections.Concurrent;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableFunction.Monitoring;

public class PartnerFileActivities(FakeSftpServer sftp, ILogger<PartnerFileActivities> logger)
{
    // Cheap, read-only check. Safe to run any number of times.
    [Function(nameof(CheckSftpForFile))]
    public bool CheckSftpForFile([ActivityTrigger] MonitorRequest watch) =>
        sftp.Exists(watch.PartnerId, watch.FileName);

    [Function(nameof(ImportPartnerFile))]
    public void ImportPartnerFile([ActivityTrigger] MonitorRequest watch)
    {
        // Real code: download, then move to /processed. That makes a re-run harmless (idempotent).
        logger.LogInformation("Importing {FileName} from {PartnerId}", watch.FileName, watch.PartnerId);
        sftp.MoveToProcessed(watch.PartnerId, watch.FileName);
    }

    [Function(nameof(AlertOps))]
    public void AlertOps([ActivityTrigger] string message) =>
        // Real code: post to Teams / PagerDuty / an Action Group.
        logger.LogError("OPS ALERT: {Message}", message);
}

// Stand-in for the partner's SFTP server. Registered as a singleton in Program.cs.
public class FakeSftpServer
{
    private readonly ConcurrentDictionary<string, string> _files = new();

    public void Upload(string partnerId, string fileName) => _files[Key(partnerId, fileName)] = "incoming";

    public bool Exists(string partnerId, string fileName) =>
        _files.TryGetValue(Key(partnerId, fileName), out string? state) && state == "incoming";

    public void MoveToProcessed(string partnerId, string fileName) => _files[Key(partnerId, fileName)] = "processed";

    private static string Key(string partnerId, string fileName) => $"{partnerId}/{fileName}";
}
