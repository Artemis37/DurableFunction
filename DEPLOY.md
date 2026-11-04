# Deploy to Azure with Visual Studio

This guide publishes the function app to a Flex Consumption plan with the **Publish** button.

These steps haven't been tested yet. They come from
[Microsoft's Visual Studio guide](https://learn.microsoft.com/en-us/azure/azure-functions/functions-develop-vs)
and this repo's code, and only a local Release `dotnet publish` has been run so far. Microsoft's
guide describes Visual Studio 2022, so labels in Visual Studio 2026 may differ slightly.

## 1. Before you start

- Visual Studio with the **Azure development** workload.
- An Azure subscription, with Visual Studio signed in to that account.

Publishing creates the other resources, so you don't need a storage account beforehand.

## 2. Publish

1. In **Solution Explorer**, right-click the `DurableFunction` project (not the solution) and select
   **Publish**.
2. Select **Azure** > **Azure Function App** > **Create new**, selecting **Next** between screens.
3. Fill in these fields:

   | Field | Value |
   |-------|-------|
   | **Name** | Globally unique: lowercase letters, numbers and hyphens |
   | **Subscription name** | Yours |
   | **Resource group** | **New** |
   | **Plan Type** | **Flex Consumption**, because .NET 10 can't run on Linux Consumption. Microsoft's docs note a dialog issue: select **App service plan** first, then reselect **Flex Consumption**. |
   | **Operating system** | **Linux** |
   | **Location** | A [region that supports Flex Consumption](https://learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-how-to#view-currently-supported-regions). **Create** stays greyed out otherwise. |
   | **Instance memory size** | **2048** |
   | **Azure Storage** | **New** |
   | **Application Insights** | **New**. `Program.cs` only exports telemetry when this is connected. |

4. Select **Create** and wait for it to finish. Select **Finish**, then **Close** once the profile
   is created, then **Publish**. When the deployment completes, the publish page shows the app's URL.

## 3. App settings

Visual Studio doesn't upload `local.settings.json`, so do this right after publishing.

On the publish page, go to **Hosting** > **...** > **Manage Azure App Service settings**. The dialog
should list the settings from `local.settings.json`, each with a **Local** and a **Remote** value.
Only **Remote** is used in Azure.

| Setting | What to do |
|---------|------------|
| `AzureWebJobs.NightlyRollupTimer.Disabled` | Set **Remote** to `true`, or use **Add setting** if it isn't listed. Without this, the nightly rollup runs at 02:00 UTC with at least 5,000 activity runs. |
| `AzureWebJobsStorage` | Keep what Visual Studio set, which may be `AzureWebJobsStorage__accountName` instead (`Program.cs` handles both). Never use **Insert value from Local** on it, because the local value points to the emulator and would break the app. |
| `FUNCTIONS_WORKER_RUNTIME` | Don't add it. It's deprecated on Flex Consumption. |
| `AzureWebJobsDashboard` | Don't add it. It's no longer in Microsoft's settings reference. |
| `ExpenseAutoApproveLimit` | Optional. The code defaults to 5000. |

Select **OK**. This saves your changes to the app and to `local.settings.json`, which is gitignored.

## 4. Test it

Follow [Testing the deployed app](README.md#testing-the-deployed-app) in the README. It covers
`http-client.env.json`, the gitignored `http-client.env.json.user` and the **azure** dropdown.

## 5. Costs and cleanup

- **Don't add always-ready instances.** They bill continuously and get no free allowance. Microsoft
  recommends one for Durable Functions on Flex Consumption to reduce cold starts, but a learning app
  doesn't need it.
- **Stop the eternal orchestration after testing.** It runs until you stop it. Send the `Stop it`
  request in `DurableFunction/EternalOrchestration/EternalOrchestration.http`.
- **Leaving the app idle isn't quite free.** Expect roughly $0.50 to $1 a month in storage queue
  calls. That's only an estimate, based on East US prices and assuming Flex polls queues the way
  Microsoft documents for Consumption. If you leave it idle, also set
  `AzureWebJobs.PurgeOldInstances.Disabled` to `true` so its daily 03:00 UTC run stops.
- **For exactly $0, delete the resource group.**

## 6. Before you commit

Publishing generates a `.pubxml` file in `DurableFunction/Properties/PublishProfiles`, and it isn't
gitignored. It holds no password but can include your subscription ID, so check it before pushing
to GitHub. Run `git status` too, in case Visual Studio added other files under
`DurableFunction/Properties`.
