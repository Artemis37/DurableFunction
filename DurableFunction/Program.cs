using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Blobs;
using DurableFunction.FunctionalChaining;
using DurableFunction.Monitoring;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

// Activities get constructor injection like any other function.
// Orchestrators never use these services directly: they ask an activity to do it.
builder.Services.AddSingleton<ILoanRepository, InMemoryLoanRepository>();
builder.Services.AddSingleton(_ => CreateHostStorageBlobClient());
builder.Services.AddSingleton<FakeSftpServer>();

builder.Build().Run();

// The storage account behind AzureWebJobsStorage, which an app setting describes in one of two ways.
static BlobServiceClient CreateHostStorageBlobClient()
{
    // Connection string. Locally this is UseDevelopmentStorage=true, which is Azurite.
    string? connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
    if (!string.IsNullOrEmpty(connectionString))
    {
        return new BlobServiceClient(connectionString);
    }

    // Identity-based: no secret, just the account name. __clientId is only set when the app
    // connects with a user-assigned identity; without it, the system-assigned identity is used.
    string accountName = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName")
        ?? throw new InvalidOperationException("Set AzureWebJobsStorage or AzureWebJobsStorage__accountName.");

    return new BlobServiceClient(
        new Uri($"https://{accountName}.blob.core.windows.net"),
        new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = Environment.GetEnvironmentVariable("AzureWebJobsStorage__clientId")
        }));
}
