# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Jira Rollup Agent — a hackathon project (solo, @bblair2018) that summarizes Jira activity across a full ticket hierarchy into a single HTML report. It uses Azure OpenAI (GPT-4o, GitHub Models as fallback) to produce role-weighted summaries at each rollup level, then sorts the result by business priority.

**Current state: all three pipeline stages are implemented.** `Program.cs` wires up the Host/DI/Serilog/EF Core boilerplate (see below) and runs them in order. The first stage loads `MockData/jira-hierarchy.json` + `team-roster.json` into `VSLiveJiraRollup`. The second stage generates and persists a summary for every Initiative/Epic/WorkItem via Azure OpenAI (see "Planned: implementation order for `SummarizationService`" below for the full design — verified end-to-end: 280 work item + 30 epic + 10 initiative summaries, zero placeholders/empty, in a real ~14.5-minute run across all 10 Initiatives) and self-disables `AppSettings:RunSummarization` after success. The third stage renders `report/rollup-report.html` — Initiatives ordered by `PriorityRank` with their Business Summary, each with its Epics nested underneath showing their Engineering Summary (see `Services/HtmlReportGeneratorService/` below) — verified against the real 280/30/10 summaries above.

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

### Intended data flow (implemented)

Mocked Jira hierarchy → .NET agent → Azure OpenAI → item summaries → Epic engineering summaries → Initiative business summaries → sort by priority → single HTML report.

1. **Item/sub-item summarization**: one LLM summary per WorkItem, including Subtask/StoryBug — Bug/Task/Spike/Subtask/StoryBug from their own comments; Story from its own comments plus its Subtasks'/StoryBugs' summaries. No role-weighting at this level. See "Planned: item→Epic→Initiative summary chaining" below for the full recursive rule.
2. **Epic summarization**: comments across all of an Epic's items, filtered/weighted toward `Dev`/`QA` roles, into an **Engineering Summary**.
3. **Initiative summarization**: comments across the Initiative, filtered/weighted toward `ScrumMaster`/`Stakeholder`/`EngineeringManager` roles, into a **Business Summary**.
4. **Sort**: Initiatives ordered by `priorityRank`.
5. **Output**: one HTML report — Initiatives in priority order, each showing its Business Summary with nested Epic Engineering Summaries.

The mock data stands in for a separate, already-built Jira ingestion pipeline — the intent is that pipeline's real output could later be substituted for `MockData/jira-hierarchy.json` without changing the summarization/report logic.

#### Role-weighting is soft (emphasis), not a hard filter

"Weighted toward" a set of roles means every comment (all roles) is still sent to the model — nothing is dropped before the call. The system prompt just tells the model *whose voice to prioritize* when composing that particular summary:

- **Engineering Summary** (Epic level): prioritize what `Dev`/`QA` said — technical progress, bugs, blockers.
- **Business Summary** (Initiative level): prioritize what `ScrumMaster`/`Stakeholder`/`EngineeringManager` said — status, risk, business impact.

Same underlying comment stream, two different lenses, produced by pointing the model at different voices for each summary — not by hiding comments from roles outside the weighted set. A hard filter (drop non-matching-role comments entirely) was considered and rejected: it risks losing real signal, e.g. a `Stakeholder` comment flagging a business-critical blocker on an Epic, which a hard Dev/QA-only filter would never let the Engineering Summary see.

#### Item→Epic→Initiative summary chaining (implemented)

Every node's summary = its own comments + its children's *already-generated summaries* (not raw comments) — one rule, applied recursively at every level, no special-casing:

- **Leaves** (Bug, Task, Spike, Subtask, StoryBug — no children): summary from their own comments only. No role weighting.
- **Story** (children = Subtask/StoryBug): summary from its own comments + its Subtasks'/StoryBugs' summaries. No role weighting.
- **Epic** (children = Story/Bug/Task/Spike): summary from its own comments (Dev/QA-weighted) + its items' summaries → Engineering Summary.
- **Initiative** (children = Epics): summary from its own comments (ScrumMaster/Stakeholder/EngineeringManager-weighted) + its Epics' summaries → Business Summary.

So every WorkItem type gets its own summary, not just Story/Bug/Task/Spike — **320 total summary rows** (10 Initiatives + 30 Epics + 280 WorkItems), one per ticket in the hierarchy. See the updated table in "Current data volume" above.

Processing order, bottom-up per Initiative: Subtask/StoryBug → Story → (Bug/Task/Spike, no children, so these can run any time) → Epic → Initiative.

Why summarize every ticket instead of only Story/Bug/Task/Spike (the earlier version of this plan):
- **One rule, no special cases** — Story behaves exactly like Epic/Initiative (own comments + children's summaries) instead of being a one-off that reads raw child comments directly.
- **Better rollups** — a Story's summary is built from already-condensed, coherent child summaries instead of a raw blend of comments from multiple different Subtasks/StoryBugs, reducing the risk of the model conflating which child said what.
- **Bounded prompt size at every step** — each LLM call only ever digests one ticket's own comments plus a handful of short child summaries, never a full subtree's raw comments at once.
- **Free drill-down** — anyone can read what happened on any single Subtask/StoryBug from its own summary, no SQL required.
- **Reusable beyond this report** — Subtask/StoryBug summaries aren't just scaffolding for the Story; they're a standalone output if a future feature ever wants per-ticket or per-assignee views.

Persistence approach: build one Initiative's whole chain in memory (`Dictionary<int, string>` of summaries keyed by WorkItem/Epic id, consulted by the next level up instead of re-querying the DB), then write all levels to the DB in one pass — same shape as `JiraHierarchyLoaderService` building the whole graph in memory before one `CompleteAsync()`.

#### The three prompt types, and their exact system prompts (implemented)

Every summary in the pipeline is produced by one of three prompt shapes — varying on two axes: does it incorporate children's summaries, and is it role-weighted. A full worked example (real ticket data, every prompt written out literally end-to-end for one Initiative) lives in `doc/prompt-types-overview.html`.

- **Type A — leaf, no weighting** (Bug, Task, Spike, Subtask, StoryBug — input: own comments only): *"Summarize this Jira ticket's activity for a status report. Write 1-3 concise sentences covering current status, key progress, and any blockers. Treat all comments equally regardless of author. Synthesize — don't quote comments verbatim."*
- **Type B — rollup, no weighting** (Story only — input: own comments + its Subtasks'/StoryBugs' summaries): same system prompt as Type A, plus *"You are also given summaries of this Story's Subtasks and StoryBugs — incorporate their key points."*
- **Type C — rollup, role-weighted** (Epic and Initiative — input: own comments + children's summaries), one template parameterized twice:
  - Epic → Engineering Summary: *"Produce an Engineering Summary of this Epic for developers/technical leads: technical progress, bugs, blockers. Prioritize what Dev and QA commenters said; other roles are context but shouldn't dominate. Incorporate the work item summaries provided."* + the shared structured-output format below.
  - Initiative → Business Summary: *"Produce a Business Summary of this Initiative for stakeholders/leadership: overall status, risk, business impact. Prioritize what ScrumMaster, Stakeholder, and Engineering Manager commenters said; Dev/QA commentary is context but shouldn't dominate. Incorporate the Epic summaries provided. Avoid deep technical jargon."* + the shared structured-output format below.

Only Type C's output is ever printed in the report (as the Engineering Summary or Business Summary); Types A and B exist purely to manufacture clean input for something further up the chain, and stay plain prose (no structured format) since a human never reads them directly.

**Structured output format** (`StructuredOutputFormat`, appended to both Type C prompts, added when the report needed to be readable rather than a wall of prose): *"Format your response exactly like this: a line \"Status: <one-sentence overall status>\", then a line \"Key Progress:\" followed by 2-4 concise bullet points (each starting with \"- \"), then - only if there are real risks or blockers - a line \"Risks/Blockers:\" followed by bullet points (omit this section entirely if there are none). Keep bullets short and concrete; no filler."* `HtmlReportGeneratorService.FormatSummaryText` parses this into a `<p class="status-line">`, `<p class="summary-heading">` section headings, and real `<ul><li>` bullet lists (plus the existing `**bold**` → `<strong>` conversion) — see `doc/prompt-types-overview.html` for the full worked example, updated to reflect this format.

#### Summary storage schema (implemented)

Three new tables, one per level, each enforcing "exactly one row per entity" (matches the overwrite/no-history decision above):

```
WorkItemSummaries            Id (PK), WorkItemId (FK, unique), SummaryText, RangeStart, RangeEnd, GeneratedAt
EpicEngineeringSummaries     Id (PK), EpicId (FK, unique), SummaryText, RangeStart, RangeEnd, GeneratedAt
InitiativeBusinessSummaries  Id (PK), InitiativeId (FK, unique), SummaryText, RangeStart, RangeEnd, GeneratedAt
```

- The `unique` constraint on each FK enforces one summary per entity — not "history," just the current regenerated state.
- `RangeStart`/`RangeEnd` record which date range produced each row — provenance for a report footer and for debugging, not a history mechanism (still one row per entity, still overwritten every run).
- No `IsPlaceholder` flag for the "no activity" case — the literal text "No activity in this period" in `SummaryText` is sufficient. Nothing downstream needs to treat it specially: item-level summaries (Type A/B) are never printed in the report directly, only consumed as plain text by the next level's prompt, which handles it fine without a flag.
- Wiring: add to `DAL/Models/JiraHierarchyEntities.cs`, register `DbSet<T>` + unique indexes in `JiraRollupDBContext.OnModelCreating`, add three named `IRepository<T>` properties to `IUnitOfWork`/`UnitOfWork` (same pattern as the existing five), one new EF migration.
- Persistence: `SummarizationService.Run()` clears all three tables first via a new lightweight `IUnitOfWork.DeleteAllSummariesAsync()` (scoped to just these three, not the full `DeleteAllRowsAsync()` which wipes the whole hierarchy), builds the whole chain in memory, then inserts fresh rows in one `CompleteAsync()`.

#### Date-range filtering for summarization (implemented)

Summaries should be scopable to a date range (e.g. "this sprint," "this week") by filtering comments on `Timestamp` before they go into any prompt at any level, rather than always summarizing an item's/Epic's/Initiative's full comment history.

`Comments.Timestamp` is the **only** date/time field in the whole schema — `Initiatives`/`Epics`/`WorkItems` have no created/updated/status-changed date of their own, so any date-range filtering can only ever operate on comment timestamps. In the current mock data, all 992 comments fall within **2026-07-01 to 2026-07-31** (verified via `MIN(Timestamp)`/`MAX(Timestamp)`).

Because higher levels consume already-generated *child summaries* rather than raw comments (see chaining plan above), the date filter only needs to be applied once — to each ticket's own comments at the point it's summarized. It naturally propagates upward: an Epic's summary is built from its own (already-filtered) comments plus item summaries that were themselves built from filtered comments. No need to re-filter at every level.

Bounds handling, decided:

1. **Range doesn't overlap the data at all** (entirely before the earliest or after the latest comment) — fail fast with a clear error *before* running the pipeline (e.g. "no comments found between X and Y; data spans 2026-07-01 to 2026-07-31"), rather than silently generating hundreds of empty summaries. Detect by comparing the configured range against `MIN`/`MAX(Timestamp)` up front.
2. **Range partially overlaps the data** — normal, not an error. A plain `WHERE Timestamp >= start AND Timestamp <= end` filter naturally returns whatever subset of comments exists in that slice.
3. **Invalid range** (`start` after `end`) — reject at startup; don't silently swap or return empty.
4. **A specific ticket has zero in-range comments, even though the overall range is valid** — emit a placeholder summary (e.g. "No activity in this period") rather than skipping the ticket from the report, so the hierarchy stays structurally complete instead of having unexplained gaps.
5. **How is the range specified?** Config-driven — `AppSettings:SummaryRangeStart`/`End` in `appsettings.json`, matching the existing flag-based config pattern (`RunHierarchyLoad`/`RunSummarization`/`RunReportGeneration`). No CLI arg parsing needed: `BuildConfig` already calls `.AddEnvironmentVariables()`, so the range can also be overridden per-run via environment variable (e.g. `AppSettings__SummaryRangeStart`) without editing the file.
6. **History vs. overwrite** — overwrite, no history. Each `SummarizationService.Run()` clears the three summary tables and regenerates fresh for whatever range is currently configured; no date-range column on the summary tables. Nothing in this project's scope calls for comparing multiple historical periods, so that complexity isn't built preemptively — a straightforward additive migration if ever needed later.

#### Implementation order for `SummarizationService` (completed)

Built in this order (see the "implemented" markers above for what each step produced):

1. **Config + packages**: add `Azure.AI.OpenAI`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI` to `JiraRollupAgent.csproj` (`Microsoft.Extensions.Configuration.UserSecrets` is already there). Add `AzureOpenAI:ChatModel` (non-secret, `"gpt-5.6-sol"`) and `AppSettings:SummaryRangeStart`/`SummaryRangeEnd` to `appsettings.json`. Register `IChatClient` as a DI singleton in `Program.cs`.
2. **DAL changes + migration**: add the three summary entities (see "Planned: summary storage schema" above) to `JiraHierarchyEntities.cs`, wire `DbSet<T>` + unique-index config into `JiraRollupDBContext.OnModelCreating`, add three named repository properties + a `DeleteAllSummariesAsync()` (scoped to just the three summary tables, not the full `DeleteAllRowsAsync()`) to `IUnitOfWork`/`UnitOfWork`, generate + apply the EF migration.
3. **`SummarizationService` itself**: constructor gets a third param, `IChatClient`, alongside the existing `IConfiguration`/`IUnitOfWork`. `Run()`: validate the configured date range (reject `start > end`; fail fast if it doesn't overlap `MIN`/`MAX(Timestamp)`), clear the three summary tables, load the hierarchy level-by-level via `FindAsync` (no `Include` support, per the existing pattern), walk bottom-up per Initiative building the in-memory chain (Subtask/StoryBug → Story → Bug/Task/Spike → Epic → Initiative) per the three prompt types, handle the zero-comment placeholder case, then persist everything in one `CompleteAsync()`. One shared private helper actually calls `IChatClient.GetResponseAsync(...)`, reused by all three prompt types; separate helpers format each prompt's user message (ticket header + comments + child summaries) matching the templates in `doc/prompt-types-overview.html`.

**Error handling decision**: if a single LLM call fails mid-run (network hiccup, rate limit), let the whole `Run()` abort — matches the existing top-level try/catch pattern every other service already uses (`Program.cs`'s per-stage try/catch, each `Run()`'s own try/catch). No per-ticket retry/partial-failure resilience; this isn't a long-running production job where that complexity pays for itself.

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
- `Services/SummarizationService/` — **implemented.** Loads all comments, validates/normalizes the configured date range (see "Bounds handling, decided" above), builds the flat `HierarchyData` lookups (`LoadHierarchyDataAsync`), then walks every Initiative bottom-up (`BuildSummaryChainAsync`: Subtask/StoryBug → Story → Bug/Task/Spike → Epic → Initiative via `SummarizeLeafAsync`/`SummarizeStoryAsync`/`SummarizeEpicAsync`/`SummarizeInitiativeAsync`), calling Azure OpenAI through the three prompt types (see "Planned: the three prompt types" above — now implemented, not just planned) and skipping the LLM call entirely for zero-comment tickets in favor of the placeholder. `PersistSummariesAsync` clears the three summary tables and inserts everything in one `CompleteAsync()` — the "Summarize" stage.
- `Services/HtmlReportGeneratorService/` — **implemented.** Loads Initiatives (ordered by `PriorityRank`), their Epics (ordered by `Id` - no ordering spec exists for Epics within an Initiative), and both summary tables as flat lookups (`LoadReportDataAsync`, same "no Include support" pattern as `SummarizationService`). Fails fast if zero Initiative summaries exist at all (Summarization hasn't run yet); a missing summary for a specific Initiative/Epic renders a visible "Summary not yet generated" placeholder instead of crashing. Renders self-contained HTML (embedded CSS, no external dependencies) via `BuildReportHtml`, converting the model's own `**bold**` emphasis to `<strong>` (`FormatSummaryText`). `FindRepoRoot` walks up from `AppDomain.CurrentDomain.BaseDirectory` looking for `vslhq26-ai-rocketeer.slnx` (a marker only present at repo root) rather than hardcoding `..\..\..` levels — robust to Debug/Release/publish layout differences, still zero CWD-dependence. Writes to a single fixed `report/rollup-report.html`, overwritten every run (no history, matching the summary tables' own decision). Self-disables `AppSettings:RunReportGeneration` after success, same as the other two stages — see "Load-once behavior" below — the "Report" stage.

#### Load-once behavior (all three stages)

Each stage is meant to run once, not on every invocation. After a successful run, each rewrites its own flag to `false` in the `appsettings.json` sitting next to the built exe: `JiraHierarchyLoaderService` → `AppSettings:RunHierarchyLoad` (`DisableHierarchyLoadFlagAsync`), `SummarizationService` → `AppSettings:RunSummarization` (`DisableSummarizationFlagAsync`), `HtmlReportGeneratorService` → `AppSettings:RunReportGeneration` (`DisableReportGenerationFlagAsync`) — identical shape in all three: `System.Text.Json.Nodes.JsonNode` so the rest of the file is preserved, `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so `=>` in the Serilog output template doesn't get HTML-escaped to `>`. Subsequent runs see the relevant flag is `false` and skip straight to logging `** SKIPPED **`. Flipping a flag back to `true` by hand and running again redoes that stage's expensive work from scratch: hierarchy load wipes all 5 tables (`DeleteAllRowsAsync`) and reloads from the mock JSON; summarization wipes the 3 summary tables (`DeleteAllSummariesAsync`) and regenerates every summary (~15 minutes, ~320 LLM calls); report generation just overwrites `report/rollup-report.html`.

Caveat, confirmed by direct testing (not just inferred): **every** `dotnet run` re-copies `appsettings.json` from the source tree over the build output *before the app executes*, because `<CopyToOutputDirectory>Always</CopyToOutputDirectory>` is unconditional — it's not only "rebuilds that recompile source," as originally worded here; running `dotnet run` twice in a row with zero source changes still resets the flag both times. So the runtime self-disable **always** fires correctly on a successful run, but it only "sticks" for a subsequent `dotnet run` if the checked-in source-tree `appsettings.json` also has that flag set to `false` — the build-output rewrite alone isn't enough. `RunHierarchyLoad` and `RunReportGeneration` are currently `false` in the checked-in file (both stages have already run once); `RunSummarization` is still `true` (re-enable deliberately when you want to regenerate summaries, e.g. for a different date range).

#### `Models/JiraHierarchyLoaderService/MockDataModels.cs`

Deserialization DTOs shaped after the mock JSON, not the `DAL.Models` entities — `MockJiraHierarchy`/`MockInitiative`/`MockEpic`/`MockWorkItem`/`MockSubItem`/`MockComment`/`MockTeamRoster`/`MockTeamMember`. Kept deliberately separate from `DAL.Models` because the shapes diverge (e.g. `MockWorkItem.Subtasks`/`StoryBugs` vs. the DAL's unified `WorkItem.Children` + `Type` discriminator) — the loader service is what reconciles the two.

### `DAL/` — data access layer

Mirrors the reference app's generic repository pattern:

- `DAL/Context/JiraRollupDBContext.cs` — EF Core `DbContext` for SQL Server, connection string key `VSLiveJiraRollupConnectionString` (same server/credentials as StatusReporter's `StatusReporterDBConnectionString`, database renamed to `VSLiveJiraRollup`). `OnModelCreating` sets `SQL_Latin1_General_CP1_CI_AS` collation and configures relationships; `Comment`'s three optional parent FKs (Initiative/Epic/WorkItem) and `WorkItem`'s self-referencing parent/child FK all use `DeleteBehavior.Restrict` to avoid SQL Server's multiple-cascade-paths error.
- `DAL/Repositories/Interfaces/` + `DAL/Repositories/Implementations/` — generic `IRepository<T>`/`Repository<T>` (CRUD over any entity) and `IUnitOfWork`/`UnitOfWork` (named repository properties per entity, a generic `Repository<T>()` accessor cached by type, `CompleteAsync`/`SaveAsync`, `DeleteAllRowsAsync` for a full-table reset). Registered in `Program.cs` as `AddDbContext<JiraRollupDBContext>` + `AddScoped<IUnitOfWork, UnitOfWork>`.
- `DAL/Models/JiraHierarchyEntities.cs` — `Initiative`, `Epic`, `WorkItem` (Story/Bug/Task/Spike/Subtask/StoryBug via a `Type` discriminator column, with `EpicId` for direct Epic children and a self-referencing `ParentWorkItemId` for Subtask/StoryBug under a Story), `Comment` (attaches to exactly one of Initiative/Epic/WorkItem via three nullable FKs), `TeamMember`. This is a relational projection of the `jira-hierarchy.json`/`team-roster.json` shapes documented above — populated by `Services/JiraHierarchyLoaderService/` (see below).
- `DAL/Migrations/` — the `InitialCreate` migration generated from this model; regenerate with a new migration (don't hand-edit) whenever the entities change.

Originally scoped to just the mock-data shapes; the summary tables (`WorkItemSummaries`/`EpicEngineeringSummaries`/`InitiativeBusinessSummaries`) have since been added — see "Summary storage schema" above. `issue-type-workflows.json` (the third mock data file) still has no entity; add one only when actually needed by a service that writes to it.

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

#### Current data volume (verified against `VSLiveJiraRollup`)

```
Initiatives   10
Epics         30
WorkItems    280 (Story 69, StoryBug 65, Subtask 65, Bug 32, Task 29, Spike 20)
Comments     992
TeamMembers   14
```

Comment counts by level/type — every comment attaches to exactly one of Initiative/Epic/WorkItem (40 + 97 + 855 = 992, matching the `Comments` row count exactly; none orphaned):

| Level / Type | ItemCount | CommentCount | Gets own summary? | Role-weighted? |
| --- | --- | --- | --- | --- |
| Initiative | 10 | 40 | Yes | Yes — ScrumMaster/Stakeholder/EM |
| Epic | 30 | 97 | Yes | Yes — Dev/QA |
| Story | 69 | 272 | Yes | No |
| StoryBug | 65 | 194 | Yes | No |
| Subtask | 65 | 141 | Yes | No |
| Bug | 32 | 104 | Yes | No |
| Task | 29 | 85 | Yes | No |
| Spike | 20 | 59 | Yes | No |

Total summary rows once implemented: 320 (10 Initiatives + 30 Epics + 280 WorkItems) — every ticket in the hierarchy gets exactly one summary.

Query used (`sqlcmd -S localhost -U sa -P '<password>' -d VSLiveJiraRollup -C`):

```sql
SELECT 'Initiative' AS Level, COUNT(DISTINCT i.Id) AS ItemCount, COUNT(c.Id) AS CommentCount
FROM Initiatives i LEFT JOIN Comments c ON c.InitiativeId = i.Id
UNION ALL
SELECT 'Epic', COUNT(DISTINCT e.Id), COUNT(c.Id)
FROM Epics e LEFT JOIN Comments c ON c.EpicId = e.Id
UNION ALL
SELECT w.Type, COUNT(DISTINCT w.Id), COUNT(c.Id)
FROM WorkItems w LEFT JOIN Comments c ON c.WorkItemId = w.Id
GROUP BY w.Type;
```

**Superseded**: every WorkItem type gets its own summary now, including Subtask/StoryBug — see "Planned: item→Epic→Initiative summary chaining" above for the current (recursive, uniform) design.

### Known limits (from README, still applicable to any implementation)

- Demo uses mocked Jira data, not a live pull.
- Item-level summaries (every WorkItem type, including Subtask/StoryBug) have no role split — role weighting only applies at Epic and Initiative level.
- Initiative ordering relies solely on the `priorityRank` field in the mock data; there's no independent prioritization logic to implement.
