# Durable Functions: patterns by example (AZ-204)

Stateful orchestration on top of stateless Azure Functions. .NET 10 isolated worker,
`Microsoft.Azure.Functions.Worker.Extensions.DurableTask`.

Each folder is one concept. Read the big comment at the top of the orchestrator file first,
then try it with the folder's `.http` file.

| # | Folder | Concept | Orchestrator to read first | Example |
|---|--------|---------|----------------------------|---------|
| 1 | [FunctionalChaining](FunctionalChaining/) | Sequential steps, with retries and compensation | `LoanOrchestrator.cs` | Loan: validate → enrich → score → persist |
| 2 | [FanOutFanIn](FanOutFanIn/) | Parallel activities, `Task.WhenAll`, partial failure, sub-orchestrations | `BranchRollupOrchestrator.cs` | Nightly roll-up of branch files |
| 3 | [AsyncHttpApi](AsyncHttpApi/) | `202` + status URL, custom status, idempotent start | `ReportOrchestrator.cs` | Long report generator |
| 4 | [Monitoring](Monitoring/) | Durable polling loop with `CreateTimer` | `PartnerFileMonitor.cs` | Watch a partner's SFTP for a file |
| 5 | [HumanInteraction](HumanInteraction/) | `WaitForExternalEvent` raced against `CreateTimer` | `ExpenseApprovalOrchestrator.cs` | Expense approval with escalation |
| ★ | [Determinism](Determinism/) | Replay, and why orchestrators must be deterministic | `ReplayDemo.cs`, `BrokenOrchestratorDemo.cs` | Watch replays in the console |
| ★ | [EternalOrchestration](EternalOrchestration/) | `ContinueAsNew`, singletons, purging history | `PartnerSyncOrchestrator.cs` | Sync forever with bounded history |

## Running it

1. Open `DurableFunction.slnx` in Visual Studio and press **F5**. Visual Studio starts Azurite
   (the storage emulator), which holds the task hub: `AzureWebJobsStorage=UseDevelopmentStorage=true`.
   Outside Visual Studio: run `azurite` and `func start` (Azure Functions Core Tools v4).
2. Open any folder's `.http` file and click **Send request** above a call.
3. Watch the console. The orchestrators log with the replay-safe logger, so each step prints once.

Built-in status endpoint for any instance:
`GET http://localhost:7268/runtime/webhooks/durabletask/instances/{instanceId}?showHistory=true`

Azurite keeps history between runs. Scenarios with fixed instance IDs (report, expense,
monitor, sync) return the existing instance if you send the same ID again, so change the ID
to re-run one.

## Testing the deployed app

The `.http` files get their URL and keys from `DurableFunction/http-client.env.json`. The local
values under `$shared` apply by default, and choosing the `azure` environment overrides them.
Visual Studio reads these files on your PC, so they aren't Azure app settings and nothing in them
is published.

1. In `http-client.env.json`, replace `YOUR-APP` under `azure` with your function app's host name.
2. Create `DurableFunction/http-client.env.json.user` next to it. It's gitignored, so your keys stay
   on your machine, and its values override the ones in `http-client.env.json`:
   ```json
   { "azure": { "functionKey": "<default host key>", "systemKey": "<durabletask_extension system key>" } }
   ```
   Both keys are on the function app's **Functions** > **App keys** page in the Azure portal.
3. Choose **azure** in the environment dropdown at the top right of the `.http` editor and send a
   request. If the dropdown doesn't show the environments, close and reopen the solution.

## Gotchas found by running it

- **The 202 body is PascalCase in the isolated worker** (`Id`, `StatusQueryGetUri`, …), while the
  docs show camelCase. JSONPath is case-sensitive, so the `.http` files use `$.StatusQueryGetUri`.
  The built-in status endpoint itself still returns camelCase (`runtimeStatus`, `customStatus`).
- **Don't name an activity's input parameter `data`.** It compiles, but the host fails to index the
  function at startup ("Can't bind parameter 'data' to type 'System.String'") and disables it.

## The four function types

| Type | Binding | Job | Rules |
|------|---------|-----|-------|
| Client (starter) | `[DurableClient] DurableTaskClient` + any trigger | Start, query, raise events, terminate, purge | Normal function |
| Orchestrator | `[OrchestrationTrigger] TaskOrchestrationContext` | Coordinates; replays from the top on every wake-up | **Deterministic only** |
| Activity | `[ActivityTrigger] T input` | The actual work: I/O, HTTP, DB | Runs at least once, so **make it idempotent** |
| Entity | `[EntityTrigger]` | Small addressable state ("aggregator" pattern) | Not covered here |

## Replay in one picture

```
Run 1:  Validate → (not in history) schedule it, unload
Run 2:  Validate ✓ (from history) → Enrich → schedule, unload
Run 3:  Validate ✓ → Enrich ✓ → Score → schedule, unload
Run 4:  Validate ✓ → Enrich ✓ → Score ✓ → Persist → schedule, unload
Run 5:  all ✓ → return → Completed
```

The orchestrator body runs five times, so anything that can differ between runs breaks it.
See `Determinism/ReplayDemo.cs` for the full BAD → GOOD table.

On the default Azure Storage provider, history and instance status live in **Tables**, messages
in **Queues**, and leases and large payloads in **Blobs**. Alternatives: the managed Durable Task
Scheduler and MSSQL.

## Best practices, and where each one is shown

| Practice | Where |
|----------|-------|
| Orchestrators coordinate; all I/O goes in activities | every folder |
| Retry policies on transient steps only (`TaskOptions.FromRetryPolicy`) | `LoanOrchestrator.cs`, `BranchRollupOrchestrator.cs` |
| Compensation in `catch (TaskFailedException)` | `LoanOrchestrator.cs` |
| Idempotent activities (upsert, not insert) | `LoanActivities.Persist`, `PartnerFileActivities.ImportPartnerFile` |
| Small payloads: pass references, return aggregates | `BranchActivities.cs`, `ReportActivities.cs` |
| Explicit partial-failure policy for `Task.WhenAll` | `BranchRollupOrchestrator.ProcessFilesAsync` |
| Sub-orchestrations for large fan-outs | `NightlyBranchRollupBatched` |
| Concurrency throttles | `host.json` (`maxConcurrentActivityFunctions`) |
| Deterministic instance IDs for idempotent starts and singletons | `ReportApi.cs`, `PartnerSyncApi.cs` |
| Don't expose the built-in management URLs (they carry the system key) | `ReportApi.GetReportStatus` |
| Progress via `SetCustomStatus` | `ReportOrchestrator.cs`, `PartnerFileMonitor.cs` |
| Each activity is still bound by `functionTimeout` | `ReportOrchestrator.cs` header |
| Cancel the losing timer after `Task.WhenAny` | `ExpenseApprovalOrchestrator.cs` |
| Authorize whoever raises external events | `ExpenseApi.SubmitExpenseDecision` |
| Config read in the starter and passed as input | `ExpenseApi.SubmitExpense` |
| `ContinueAsNew` with minimal state | `PartnerSyncOrchestrator.cs` |
| Purge finished instances on a schedule | `HistoryPurge.cs` |
| Disable heavy timers locally | `local.settings.json` (`AzureWebJobs.NightlyRollupTimer.Disabled`) |
| Roslyn analyzers that flag non-deterministic code (DURABLE0001, 0002, 0010...) | `Microsoft.DurableTask.Analyzers` in the `.csproj`; see the `#pragma` lines in `Determinism/` |

## AZ-204 cheat sheet

| Pattern | Key API | Typical wording in the question |
|---------|---------|---------------------------------|
| Chaining | sequential `await CallActivityAsync` | "in a specific order", "output of the previous" |
| Fan-out/fan-in | `Task.WhenAll` | "in parallel, then aggregate" |
| Async HTTP | `CreateCheckStatusResponse` | "202", "status endpoint", "long-running HTTP" |
| Monitor | `CreateTimer` in a loop | "poll until", "flexible recurring process" |
| Human interaction | `WaitForExternalEvent` + `CreateTimer` + `Task.WhenAny` | "approval", "timeout", "escalate" |
| Aggregator | Durable Entities | "accumulate events into state over time" |

Exam questions often use the **in-process** names. Here's how they map to this code:

| In-process | Isolated (this project) |
|------------|-------------------------|
| `IDurableOrchestrationContext` | `TaskOrchestrationContext` |
| `[DurableClient] IDurableOrchestrationClient` | `[DurableClient] DurableTaskClient` |
| `StartNewAsync` | `ScheduleNewOrchestrationInstanceAsync` |
| `CallActivityWithRetryAsync(name, RetryOptions, input)` | `CallActivityAsync(name, input, TaskOptions.FromRetryPolicy(...))` |
| `CreateCheckStatusResponse` | `CreateCheckStatusResponseAsync` |
| `[ActivityTrigger] IDurableActivityContext` | `[ActivityTrigger] T input` |
| `CurrentUtcDateTime`, `NewGuid`, `CreateTimer`, `WaitForExternalEvent`, `RaiseEventAsync`, `ContinueAsNew` | same names |
