using Azure.Monitor.OpenTelemetry.Exporter;
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
builder.Services.AddSingleton<FakeSftpServer>();

builder.Build().Run();
