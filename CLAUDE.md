# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Jira Rollup Agent — a hackathon project (solo, @bblair2018) that summarizes Jira activity across a full ticket hierarchy into a single HTML report. It uses Azure OpenAI (GPT-4o, GitHub Models as fallback) to produce role-weighted summaries at each rollup level, then sorts the result by business priority.

**Current state: `JiraHierarchyLoaderService` is implemented; `SummarizationService`/`HtmlReportGeneratorService` are stubs.** `Program.cs` wires up the Host/DI/Serilog/EF Core boilerplate (see below) and runs three pipeline services in order. The first stage actually loads `MockData/jira-hierarchy.json` + `team-roster.json` into `VSLiveJiraRollup`; the other two still just log `** NOT YET IMPLEMENTED **` (or `** SKIPPED **` if their `AppSettings` flag is off) and return `true`.

## Commands

Run all commands from the repo root (where `vslhq26-ai-rocketeer.slnx` lives).

```
dotnet build                                     # build the solution
dotnet run --project src/JiraRollupAgent          # run the agent
```

EF Core migrations (run from `src/JiraRollupAgent/`, requires the `dotnet-ef` global tool):

```
dotnet ef migrations add <Name> --output-dir DAL/Migrations   # add a migration
dotnet ef database update                                     # apply migrations to VSLiveJiraRollup
```

Adding a migration prints a `HostAbortedException` stack trace to the console — that's `dotnet ef` probing `Host.CreateDefaultBuilder` as designed (it aborts host startup after extracting the service provider); the migration still generates correctly despite the logged error.

There is no test project in the solution yet.

SDK version is pinned via `global.json` (10.0.302, `rollForward: latestFeature`) — `dotnet --version` should resolve to a compatible 10.x SDK.

## Architecture

### Ticket hierarchy

```
Initiative
  Epic
    Story
      Subtask
      StoryBug
    Bug        (standalone, same level as Story)
    Task
    Spike
```

All items at every level carry `comments`, each with `author`, `role` (`Dev` / `QA` / `ScrumMaster` / `Stakeholder` / `EngineeringManager`), `timestamp`, and `text`. Initiatives additionally carry a `priorityRank` used for final report ordering.

### Intended data flow (not yet implemented in code)

Mocked Jira hierarchy → .NET agent → Azure OpenAI → item summaries → Epic engineering summaries → Initiative business summaries → sort by priority → single HTML report.

1. **Item summarization**: one LLM summary per Story/Bug/Task/Spike, rolling up that item's own Subtasks/StoryBugs. No role-weighting at this level.
2. **Epic summarization**: comments across all of an Epic's items, filtered/weighted toward `Dev`/`QA` roles, into an **Engineering Summary**.
3. **Initiative summarization**: comments across the Initiative, filtered/weighted toward `ScrumMaster`/`Stakeholder`/`EngineeringManager` roles, into a **Business Summary**.
4. **Sort**: Initiatives ordered by `priorityRank`.
5. **Output**: one HTML report — Initiatives in priority order, each showing its Business Summary with nested Epic Engineering Summaries.

The mock data stands in for a separate, already-built Jira ingestion pipeline — the intent is that pipeline's real output could later be substituted for `MockData/jira-hierarchy.json` without changing the summarization/report logic.

### `src/JiraRollupAgent/MockData/`

All three files are copied to the build output directory (`CopyToOutputDirectory: PreserveNewest`, configured in `JiraRollupAgent.csproj`), so code should load them relative to the app's base directory, not the source tree.

#### `jira-hierarchy.json` (~5100 lines — the primary pipeline input)

Top level is a single object: `{ "initiatives": [ ... ] }`.

```
Initiative:  id, title, priorityRank (int, 1 = highest), status, comments[], epics[]
Epic:        id, title, comments[], items[]
Item (in Epic.items[]), discriminated by "type":
  type: "Story" | "Bug" | "Task" | "Spike"
  Story:            id, title, assignee, status, comments[], subtasks[], storyBugs[]
  Bug/Task/Spike:   id, title, assignee, status, comments[]   (no subtasks/storyBugs)
Subtask (in Story.subtasks[]):   id, title, assignee, status, comments[]
StoryBug (in Story.storyBugs[]): id, title, assignee, status, comments[]
Comment (in any comments[]): author, role, timestamp (ISO 8601), text
```

Notes:
- `Bug` can also appear as a standalone item directly under an Epic (sibling of Story), not only as a `StoryBug` nested under a Story — the two are distinct: `type: "Bug"` items have no parent Story and no `subtasks`/`storyBugs`, while `storyBugs[]` entries are always children of a specific Story and use the `StoryBug` id prefix (`SBUG-...`) instead of `BUG-...`.
- `comments[].role` is one of `Dev`, `QA`, `ScrumMaster`, `Stakeholder`, `EngineeringManager` — matches `team-roster.json`'s `role` field and drives the Epic/Initiative role-weighting described above.
- IDs are human-readable and hierarchical by convention (e.g. `STORY-PFD-1-1` under `EPIC-PFD-1` under `INIT-PFD`, with `SUB-PFD-1-1-1`/`SBUG-PFD-1-1-1` beneath the story), but nothing in the pipeline should assume ID structure — always traverse via the actual nested arrays.
- Initiative/Epic have no `assignee` field (only leaf-and-mid-level items do).

#### `issue-type-workflows.json`

`{ "_source": "...", "issueTypeWorkflows": [ { "issueType": "...", "states": [...] } ] }` — one entry per issue type (Initiative, Epic, Story, Bug, Task, Spike, Subtask, StoryBug), each an ordered array of valid status strings (workflow order, not alphabetical). Per its own `_source` field, these lists are copied verbatim from `JiraInsightsApp.Shared/Helpers/WorkflowStatuses.cs` in the separate INSYTE StatusReporter project; Epic/Initiative aren't defined there natively and fall back to that project's generic status list, so treat those two as a reduced subset rather than an authoritative per-type workflow. Any status-rendering/validation logic added here should treat this file as the source of truth rather than hardcoding state lists.

#### `team-roster.json`

`{ "team": [ { id, name, role, jobTitle, email } ] }` — flat list, 14 users. `role` is the same enum as `comments[].role`; `jobTitle` is a human-readable label not used for filtering (e.g. role `Stakeholder` maps to job titles like "Head of Underwriting" or "VP, Customer Support").

### App boilerplate (adapted from `C:\UATP_CODE\INSYTE\StatusReporter\StatusReporter.Console`)

`Program.cs` follows the same shape as the INSYTE StatusReporter console app: `Main` → `MainAsync`, region-organized, wrapped in one top-level try/catch that logs and returns `false` on any unhandled exception. Startup order: build config (`BuildConfig`) → configure Serilog from that config → log environment (`ASPNETCORE_ENVIRONMENT`) → `Host.CreateDefaultBuilder().ConfigureServices(...).UseSerilog().Build()`. `Extensions/LoggerExtensions.cs` provides the same `ILogger.Here()` extension (adds `MemberName`/`FilePath`/`LineNumber` via `CallerMemberName`/`CallerFilePath`/`CallerLineNumber`) used on every log call. `appsettings.json` holds the `Serilog` sink config (Console + `MinimumLevel` only) and `ConnectionStrings`, copied to output via `CopyToOutputDirectory: Always`.

Unlike the reference app's `appsettings.json`, the Serilog **File** sink is not config-driven — it's added in code (`.WriteTo.File(logFilePath, ...)` in `Program.cs`) with `logFilePath` built from `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "JiraRollupAgent.log")`. A relative path in config (`.\Logs\...`, what the reference app uses) resolves against the process's current working directory at the point the sink opens the file, not the base directory — so `dotnet run`/`dotnet ef` invoked from different folders would scatter log files across the repo instead of writing to one place. Keep the log path built in code rather than moving it back into `appsettings.json`.

Unlike the reference app, everything lives in the single `JiraRollupAgent` project rather than a separate `.DAL` project — there's only one consumer so far, so the DAL lives under `DAL/` inside this project instead.

`Program.cs`'s `Host.CreateDefaultBuilder()` config providers resolve `appsettings.json` relative to the process's working directory, not `AppDomain.CurrentDomain.BaseDirectory` — so running `dotnet run --project src/JiraRollupAgent` from the repo root (the working directory in the Commands section above) would silently fail to find it. `.ConfigureAppConfiguration((context, config) => BuildConfig(config))` is added to the host builder specifically to force the same base-directory-relative load `BuildConfig` already uses for the pre-host Serilog setup, so `IConfiguration`/`AppSettings:*` flags work the same regardless of the caller's CWD.

### `Services/` — pipeline stages

Same Interface + Implementation-per-folder shape as the reference app's `Services/`, each stage constructor-injecting `IConfiguration` (+ `IUnitOfWork` where it'll need to read/write `VSLiveJiraRollup`), logging via `Serilog.Log.ForContext<T>()` and `.Here()`, and exposing an async `Run()` gated by its own `AppSettings` flag (`RunHierarchyLoad` / `RunSummarization` / `RunReportGeneration` in `appsettings.json`) — mirroring how the reference's `SqlIssueStorageService.Run()` checks `AppSettings:RunJIRAAPIDBProcess`. `Program.cs` registers all three as scoped services and runs them in order via `ActivatorUtilities.CreateInstance<T>(host.Services)`, exactly like the reference's "Running the Services" region.

- `Services/JiraHierarchyLoaderService/` — **implemented.** Deserializes `MockData/jira-hierarchy.json` + `team-roster.json` (via `System.Text.Json`, case-insensitive property matching against the `Models/JiraHierarchyLoaderService/Mock*` DTOs — see below), calls `IUnitOfWork.DeleteAllRowsAsync()`, maps the DTOs into the `DAL.Models` entity graph (`Initiative` → `Epic` → `WorkItem` → `Comment`, `WorkItem.Children` for Subtask/StoryBug), adds it via `_unitOfWork.Initiatives.AddAsync(initiative)` (EF Core walks the whole reachable graph on save — no need to add Epics/WorkItems/Comments individually), and `CompleteAsync()`s. On success it also flips `AppSettings:RunHierarchyLoad` to `false` in `appsettings.json` (see below) — the "Extract" stage.
- `Services/SummarizationService/` — still a stub. Will produce the item/Epic-engineering/Initiative-business summaries via Azure OpenAI — the "Summarize" stage. There's no output schema for these yet (see `DAL/` note above); add one when this stage is actually implemented.
- `Services/HtmlReportGeneratorService/` — still a stub. Will read Initiatives (by `PriorityRank`) with their summaries and render the single HTML report — the "Report" stage.

#### Load-once behavior

`JiraHierarchyLoaderService` is meant to populate the DB once, not on every run. After a successful load it rewrites `AppSettings:RunHierarchyLoad` to `false` in the `appsettings.json` sitting next to the built exe (`DisableHierarchyLoadFlagAsync`, using `System.Text.Json.Nodes.JsonNode` so the rest of the file is preserved; `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` is set so `=>` in the Serilog output template doesn't get HTML-escaped to `>`). Subsequent runs see the flag is `false` and skip straight to logging `** SKIPPED **`. Flipping `RunHierarchyLoad` back to `true` by hand and running again wipes all 5 tables (`DeleteAllRowsAsync`) and reloads everything from the mock JSON fresh.

Caveat: this only persists between runs of the same build. `appsettings.json` is copied from the source tree on every build (`CopyToOutputDirectory: Always`), so a `dotnet build`/`dotnet run` that recompiles will reset the flag back to whatever's checked into `src/JiraRollupAgent/appsettings.json` (currently `false`, since the DB has already been loaded once) — the self-disable only "sticks" across repeated `dotnet run`s that don't trigger a rebuild. Set it back to `true` in the checked-in file (not just the build output) if you want a fresh clone/build to reload automatically.

#### `Models/JiraHierarchyLoaderService/MockDataModels.cs`

Deserialization DTOs shaped after the mock JSON, not the `DAL.Models` entities — `MockJiraHierarchy`/`MockInitiative`/`MockEpic`/`MockWorkItem`/`MockSubItem`/`MockComment`/`MockTeamRoster`/`MockTeamMember`. Kept deliberately separate from `DAL.Models` because the shapes diverge (e.g. `MockWorkItem.Subtasks`/`StoryBugs` vs. the DAL's unified `WorkItem.Children` + `Type` discriminator) — the loader service is what reconciles the two.

### `DAL/` — data access layer

Mirrors the reference app's generic repository pattern:

- `DAL/Context/JiraRollupDBContext.cs` — EF Core `DbContext` for SQL Server, connection string key `VSLiveJiraRollupConnectionString` (same server/credentials as StatusReporter's `StatusReporterDBConnectionString`, database renamed to `VSLiveJiraRollup`). `OnModelCreating` sets `SQL_Latin1_General_CP1_CI_AS` collation and configures relationships; `Comment`'s three optional parent FKs (Initiative/Epic/WorkItem) and `WorkItem`'s self-referencing parent/child FK all use `DeleteBehavior.Restrict` to avoid SQL Server's multiple-cascade-paths error.
- `DAL/Repositories/Interfaces/` + `DAL/Repositories/Implementations/` — generic `IRepository<T>`/`Repository<T>` (CRUD over any entity) and `IUnitOfWork`/`UnitOfWork` (named repository properties per entity, a generic `Repository<T>()` accessor cached by type, `CompleteAsync`/`SaveAsync`, `DeleteAllRowsAsync` for a full-table reset). Registered in `Program.cs` as `AddDbContext<JiraRollupDBContext>` + `AddScoped<IUnitOfWork, UnitOfWork>`.
- `DAL/Models/JiraHierarchyEntities.cs` — `Initiative`, `Epic`, `WorkItem` (Story/Bug/Task/Spike/Subtask/StoryBug via a `Type` discriminator column, with `EpicId` for direct Epic children and a self-referencing `ParentWorkItemId` for Subtask/StoryBug under a Story), `Comment` (attaches to exactly one of Initiative/Epic/WorkItem via three nullable FKs), `TeamMember`. This is a relational projection of the `jira-hierarchy.json`/`team-roster.json` shapes documented above — populated by `Services/JiraHierarchyLoaderService/` (see below).
- `DAL/Migrations/` — the `InitialCreate` migration generated from this model; regenerate with a new migration (don't hand-edit) whenever the entities change.

Deliberately scoped to just the mock-data shapes for now — no output/rollup tables (e.g. per-item/Epic/Initiative summary storage) exist yet, and `issue-type-workflows.json` (the third mock data file) has no entity yet either. Add either only when actually needed by a service that writes to them.

#### Table schema (verified against `VSLiveJiraRollup`)

```
Initiatives      Id (PK), JiraId, Title, PriorityRank (int), Status
Epics            Id (PK), JiraId, Title, InitiativeId (FK, required)
WorkItems        Id (PK), JiraId, Type, Title, Assignee, Status, EpicId (FK, nullable), ParentWorkItemId (FK, nullable, self-ref)
Comments         Id (PK), Author, Role, Timestamp, Text, InitiativeId (FK, nullable), EpicId (FK, nullable), WorkItemId (FK, nullable)
TeamMembers      Id (PK), ExternalId, Name, Role, JobTitle, Email   -- no FK to anything; joined only by Role/Author string match in app logic
```

`WorkItems` covers Story/Bug/Task/Spike/Subtask/StoryBug in one table via `Type`; a row has either `EpicId` set (direct Epic child) or `ParentWorkItemId` set (Subtask/StoryBug under a Story) — never both, though nothing in the schema enforces that invariant, so any code writing to this table must maintain it itself.

FK relationships and delete behavior:

```
Initiatives (1) ──< Epics (many)          FK_Epics_Initiatives_InitiativeId       [CASCADE]
Epics (1)       ──< WorkItems (many)      FK_WorkItems_Epics_EpicId               [no action]
WorkItems (1)   ──< WorkItems (many)      FK_WorkItems_WorkItems_ParentWorkItemId [no action]

Comments attach to exactly one of:
  Initiatives ──< Comments   FK_Comments_Initiatives_InitiativeId  [no action / Restrict]
  Epics       ──< Comments   FK_Comments_Epics_EpicId              [no action / Restrict]
  WorkItems   ──< Comments   FK_Comments_WorkItems_WorkItemId      [no action / Restrict]
```

Only `Epics → Initiatives` cascades, because `Epic.InitiativeId` is the one non-nullable parent FK. Everything else is `NO_ACTION`/`Restrict` — `WorkItems.EpicId`/`ParentWorkItemId` because they're optional (EF's default for nullable FKs), and all three `Comments` FKs explicitly via `DeleteBehavior.Restrict` in `OnModelCreating`, since a Comment can reach the same Initiative through three different paths (direct, via Epic, via WorkItem) and SQL Server rejects multiple cascade paths to the same table.

### Known limits (from README, still applicable to any implementation)

- Demo uses mocked Jira data, not a live pull.
- Item-level summaries (Story/Bug/Task/Spike) have no role split — role weighting only applies at Epic and Initiative level.
- Initiative ordering relies solely on the `priorityRank` field in the mock data; there's no independent prioritization logic to implement.
