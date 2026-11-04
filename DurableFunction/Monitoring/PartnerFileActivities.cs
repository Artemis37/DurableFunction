using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DurableFunction.Monitoring;

public class PartnerFileActivities(FakeSftpServer sftp, ILogger<PartnerFileActivities> logger)
{
    // Cheap, read-only check. Safe to run any number of times.
    [Function(nameof(CheckSftpForFile))]
    public Task<bool> CheckSftpForFile([ActivityTrigger] MonitorRequest watch) =>
        sftp.ExistsAsync(watch.PartnerId, watch.FileName);

    [Function(nameof(ImportPartnerFile))]
    public async Task ImportPartnerFile([ActivityTrigger] MonitorRequest watch)
    {
        // Real code: download, then move to /processed. That makes a re-run harmless (idempotent).
        logger.LogInformation("Importing {FileName} from {PartnerId}", watch.FileName, watch.PartnerId);
        await sftp.MoveToProcessedAsync(watch.PartnerId, watch.FileName);
    }

    [Function(nameof(AlertOps))]
    public void AlertOps([ActivityTrigger] string message) =>
        // Real code: post to Teams / PagerDuty / an Action Group.
        logger.LogError("OPS ALERT: {Message}", message);
}

// Stand-in for the partner's SFTP server. Files are blobs in the "fake-sftp" container of the
// function app's own storage account:
//   incoming/{partnerId}/{fileName}   waiting to be imported
//   processed/{partnerId}/{fileName}  already imported
//
// WHY NOT MEMORY: on Flex Consumption the HTTP function (upload) and the Durable activities
// (check, import) scale in separate groups, so they can run on different instances, and scaling
// to zero wipes memory anyway. Shared state has to live outside the process.
public class FakeSftpServer(BlobServiceClient blobs)
{
    private static readonly BinaryData FakeContent = BinaryData.FromString("fake partner file");

    private readonly BlobContainerClient _container = blobs.GetBlobContainerClient("fake-sftp");

    public async Task UploadAsync(string partnerId, string fileName)
    {
        await _container.CreateIfNotExistsAsync();
        await _container.GetBlobClient(Incoming(partnerId, fileName)).UploadAsync(FakeContent, overwrite: true);
    }

    // False (not an error) when the container doesn't exist yet, i.e. before the first upload.
    public async Task<bool> ExistsAsync(string partnerId, string fileName) =>
        await _container.GetBlobClient(Incoming(partnerId, fileName)).ExistsAsync();

    // Activities run at least once, so this can run again after a crash at any point.
    // Overwrite + delete-if-exists means a re-run changes nothing. Writing processed first means
    // a crash in between leaves the file in incoming, so a retry finishes the move.
    public async Task MoveToProcessedAsync(string partnerId, string fileName)
    {
        await _container.GetBlobClient(Processed(partnerId, fileName)).UploadAsync(FakeContent, overwrite: true);
        await _container.GetBlobClient(Incoming(partnerId, fileName)).DeleteIfExistsAsync();
    }

    private static string Incoming(string partnerId, string fileName) => $"incoming/{partnerId}/{fileName}";

    private static string Processed(string partnerId, string fileName) => $"processed/{partnerId}/{fileName}";
}
