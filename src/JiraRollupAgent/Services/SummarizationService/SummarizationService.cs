using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace JiraRollupAgent.Services.SummarizationService
{
    [ExcludeFromCodeCoverage]
    public class SummarizationService : ISummarizationService
    {
        //This is required to enable logging for this class.
        private readonly Serilog.ILogger _log = Serilog.Log.ForContext<SummarizationService>();
        private readonly IConfiguration _config;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IChatClient _chatClient;

        /// <summary>Placeholder for a ticket/rollup with zero in-range comments - see CLAUDE.md's
        /// "no IsPlaceholder flag" decision: this literal text is stored as-is in SummaryText.</summary>
        private const string NoActivityPlaceholder = "No activity in this period.";

        // --- Type A: leaf (Bug/Task/Spike/Subtask/StoryBug) - own comments only, no weighting ---
        private const string TypeASystemPrompt =
            "Summarize this Jira ticket's activity for a status report. Write 1-3 concise sentences " +
            "covering current status, key progress, and any blockers. Treat all comments equally " +
            "regardless of author. Synthesize - don't quote comments verbatim.";

        // --- Type B: Story rollup - own comments + Subtask/StoryBug summaries, no weighting ---
        private const string TypeBSystemPrompt =
            TypeASystemPrompt + " You are also given summaries of this Story's Subtasks and " +
            "StoryBugs - incorporate their key points.";

        // --- Type C: Epic rollup - own comments (Dev/QA-weighted) + work item summaries ---
        private const string EpicSystemPrompt =
            "Produce an Engineering Summary of this Epic for developers/technical leads: technical " +
            "progress, bugs, blockers. Prioritize what Dev and QA commenters said; other roles are " +
            "context but shouldn't dominate. Incorporate the work item summaries provided. 3-5 sentences.";

        // --- Type C: Initiative rollup - own comments (ScrumMaster/Stakeholder/EM-weighted) + Epic summaries ---
        private const string InitiativeSystemPrompt =
            "Produce a Business Summary of this Initiative for stakeholders/leadership: overall " +
            "status, risk, business impact. Prioritize what ScrumMaster, Stakeholder, and " +
            "Engineering Manager commenters said; Dev/QA commentary is context but shouldn't " +
            "dominate. Incorporate the Epic summaries provided. Avoid deep technical jargon. 3-5 sentences.";

        public SummarizationService(IConfiguration config, IUnitOfWork unitOfWork, IChatClient chatClient)
        {
            _config = config;
            _unitOfWork = unitOfWork;
            _chatClient = chatClient;
        }

        #region Run

        public async Task<bool> Run()
        {
            var runStage = _config.GetValue<bool>("AppSettings:RunSummarization");

            try
            {
                if (runStage)
                {
                    _log.Here().Information("Starting process to generate item/Epic/Initiative summaries via Azure OpenAI...");

                    var allComments = (await _unitOfWork.Comments.GetAllAsync()).ToList();
                    var (rangeStart, rangeEnd) = ValidateDateRange(allComments);

                    var data = await LoadHierarchyDataAsync(allComments, rangeStart, rangeEnd);

                    var inRangeCommentCount = data.CommentsByInitiativeId.Values.Sum(c => c.Count)
                        + data.CommentsByEpicId.Values.Sum(c => c.Count)
                        + data.CommentsByWorkItemId.Values.Sum(c => c.Count);

                    _log.Here().Information(
                        "Loaded {InitiativeCount} initiatives, {EpicCount} epics, {WorkItemCount} work items, " +
                        "{InRangeCommentCount} in-range comments (of {TotalCommentCount} total) for range {RangeStart:yyyy-MM-dd} to {RangeEnd:yyyy-MM-dd}.",
                        data.Initiatives.Count,
                        data.EpicsByInitiativeId.Values.Sum(e => e.Count),
                        data.TopLevelItemsByEpicId.Values.Sum(w => w.Count) + data.ChildrenByParentWorkItemId.Values.Sum(w => w.Count),
                        inRangeCommentCount,
                        allComments.Count,
                        rangeStart,
                        rangeEnd);

                    var (workItemSummaries, epicSummaries, initiativeSummaries) = await BuildSummaryChainAsync(data, rangeStart, rangeEnd);

                    _log.Here().Information(
                        "Built {WorkItemSummaryCount} work item summaries, {EpicSummaryCount} epic summaries, {InitiativeSummaryCount} initiative summaries. Persisting...",
                        workItemSummaries.Count, epicSummaries.Count, initiativeSummaries.Count);

                    await PersistSummariesAsync(workItemSummaries, epicSummaries, initiativeSummaries, rangeStart, rangeEnd);

                    _log.Here().Information(
                        "Process to generate item/Epic/Initiative summaries completed successfully: persisted {WorkItemSummaryCount} work item summaries, " +
                        "{EpicSummaryCount} epic summaries, {InitiativeSummaryCount} initiative summaries for range {RangeStart:yyyy-MM-dd} to {RangeEnd:yyyy-MM-dd}.",
                        workItemSummaries.Count, epicSummaries.Count, initiativeSummaries.Count, rangeStart, rangeEnd);

                    await DisableSummarizationFlagAsync();

                    return true;
                }
                else
                {
                    _log.Here().Information("Process to generate item/Epic/Initiative summaries completed ** SKIPPED **");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Process to generate item/Epic/Initiative summaries ** FAILED! **");
                return false;
            }
        }

        #endregion

        #region Self-disable

        /// <summary>
        /// Flips AppSettings:RunSummarization to false in appsettings.json after a successful run, so
        /// summaries are generated once and subsequent runs skip the ~15-minute, ~320-call regenerate.
        /// Setting it back to true by hand causes the next run to wipe and regenerate every summary
        /// fresh (via PersistSummariesAsync's DeleteAllSummariesAsync) - same shape as
        /// JiraHierarchyLoaderService's DisableHierarchyLoadFlagAsync.
        /// </summary>
        private async Task DisableSummarizationFlagAsync()
        {
            try
            {
                var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                var json = await File.ReadAllTextAsync(appSettingsPath);
                var root = JsonNode.Parse(json)?.AsObject();

                if (root?["AppSettings"] is not JsonObject appSettings)
                {
                    _log.Here().Warning("Could not find an \"AppSettings\" section in appsettings.json - leaving RunSummarization as-is.");
                    return;
                }

                appSettings["RunSummarization"] = false;

                var writeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                await File.WriteAllTextAsync(appSettingsPath, root!.ToJsonString(writeOptions));

                _log.Here().Information("Set AppSettings:RunSummarization to false so the next run skips regenerating summaries.");
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Failed to disable AppSettings:RunSummarization after a successful run - it will run again next time.");
            }
        }

        #endregion

        #region Date range validation

        /// <summary>
        /// Reads and validates AppSettings:SummaryRangeStart/SummaryRangeEnd. Throws (caught by
        /// Run()'s try/catch) if the range is malformed, start is after end, or the range doesn't
        /// overlap the data at all - see CLAUDE.md's "Bounds handling, decided" for the reasoning.
        /// </summary>
        private (DateTime Start, DateTime End) ValidateDateRange(IReadOnlyCollection<Comment> allComments)
        {
            string startText = _config["AppSettings:SummaryRangeStart"]
                ?? throw new InvalidOperationException("Missing 'AppSettings:SummaryRangeStart' in appsettings.json.");
            string endText = _config["AppSettings:SummaryRangeEnd"]
                ?? throw new InvalidOperationException("Missing 'AppSettings:SummaryRangeEnd' in appsettings.json.");

            if (!DateTime.TryParse(startText, out var start))
                throw new InvalidOperationException($"'AppSettings:SummaryRangeStart' ('{startText}') is not a valid date.");

            if (!DateTime.TryParse(endText, out var end))
                throw new InvalidOperationException($"'AppSettings:SummaryRangeEnd' ('{endText}') is not a valid date.");

            // A date-only config value (e.g. "2026-07-31") parses to midnight - the *start* of that
            // day - which would exclude nearly the entire last day from an inclusive <= comparison.
            // Normalize to the end of that calendar day so "SummaryRangeEnd": "2026-07-31" means
            // "through end of July 31," matching what a human configuring this would expect.
            end = end.Date.AddDays(1).AddTicks(-1);

            if (start > end)
                throw new InvalidOperationException(
                    $"'AppSettings:SummaryRangeStart' ({start:yyyy-MM-dd}) is after 'AppSettings:SummaryRangeEnd' ({end:yyyy-MM-dd}).");

            if (allComments.Count == 0)
                throw new InvalidOperationException(
                    "No comments exist in the database at all - has the Jira hierarchy been loaded yet? (see AppSettings:RunHierarchyLoad)");

            var earliest = allComments.Min(c => c.Timestamp);
            var latest = allComments.Max(c => c.Timestamp);

            if (end < earliest || start > latest)
                throw new InvalidOperationException(
                    $"No comments found between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}; data spans {earliest:yyyy-MM-dd} to {latest:yyyy-MM-dd}.");

            return (start, end);
        }

        #endregion

        #region Prompt building and LLM calls

        /// <summary>
        /// True when there's nothing to summarize: no comments of its own, and (for rollup levels)
        /// every child summary is itself the placeholder. Callers skip the LLM call entirely and use
        /// <see cref="NoActivityPlaceholder"/> instead - see CLAUDE.md's placeholder decision.
        /// </summary>
        private static bool HasNoActivity(IReadOnlyList<Comment> ownComments, IReadOnlyList<(string Label, string SummaryText)> childSummaries)
            => ownComments.Count == 0 && childSummaries.All(c => c.SummaryText == NoActivityPlaceholder);

        /// <summary>Shared by all three prompt types - sends the system + user messages and returns the response text.</summary>
        private async Task<string> GetSummaryAsync(string systemPrompt, string userMessage)
        {
            List<ChatMessage> messages =
            [
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userMessage)
            ];

            var response = await _chatClient.GetResponseAsync(messages);
            return response.Text;
        }

        private static void AppendComments(StringBuilder sb, IReadOnlyList<Comment> comments)
        {
            foreach (var c in comments.OrderBy(c => c.Timestamp))
                sb.AppendLine($"- [{c.Timestamp:yyyy-MM-dd HH:mm}] {c.Author} ({c.Role}): {c.Text}");
        }

        /// <summary>Type A user message: ticket header, reporting period, own comments only.</summary>
        private static string BuildLeafUserMessage(string ticketHeader, DateTime rangeStart, DateTime rangeEnd, IReadOnlyList<Comment> comments)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ticketHeader);
            sb.AppendLine($"Reporting period: {rangeStart:yyyy-MM-dd} to {rangeEnd:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine("Comments:");
            AppendComments(sb, comments);
            return sb.ToString();
        }

        /// <summary>
        /// Type B/C user message shape: ticket header, reporting period, own comments, then a labeled
        /// block of already-generated child summaries. Used by Story (Type B), Epic and Initiative
        /// (Type C) - they differ only in header text and the two section labels.
        /// </summary>
        private static string BuildRollupUserMessage(
            string ticketHeader, DateTime rangeStart, DateTime rangeEnd,
            string ownCommentsLabel, IReadOnlyList<Comment> ownComments,
            string childSummariesLabel, IReadOnlyList<(string Label, string SummaryText)> childSummaries)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ticketHeader);
            sb.AppendLine($"Reporting period: {rangeStart:yyyy-MM-dd} to {rangeEnd:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine(ownCommentsLabel);
            AppendComments(sb, ownComments);
            sb.AppendLine();
            sb.AppendLine(childSummariesLabel);
            foreach (var (label, summary) in childSummaries)
                sb.AppendLine($"- [{label}] {summary}");
            return sb.ToString();
        }

        private static string BuildTicketHeader(WorkItem item)
            => $"Ticket: {item.Type} {item.JiraId} — \"{item.Title}\" (Status: {item.Status}, Assignee: {item.Assignee})";

        private static string BuildEpicHeader(Epic epic)
            => $"Epic {epic.JiraId} — \"{epic.Title}\"";

        private static string BuildInitiativeHeader(Initiative initiative)
            => $"Initiative {initiative.JiraId} — \"{initiative.Title}\" (Priority Rank: {initiative.PriorityRank}, Status: {initiative.Status})";

        #endregion

        #region Bottom-up orchestration

        /// <summary>
        /// Walks every Initiative bottom-up (Subtask/StoryBug -&gt; Story -&gt; Bug/Task/Spike -&gt;
        /// Epic -&gt; Initiative), building the in-memory summary chain - each level's summaries are
        /// looked up from the dictionaries built by the level below rather than re-querying the DB.
        /// Persistence is a separate step; this only returns the three summary dictionaries.
        /// </summary>
        private async Task<(Dictionary<int, string> WorkItemSummaries, Dictionary<int, string> EpicSummaries, Dictionary<int, string> InitiativeSummaries)>
            BuildSummaryChainAsync(HierarchyData data, DateTime rangeStart, DateTime rangeEnd)
        {
            var workItemSummaries = new Dictionary<int, string>();
            var epicSummaries = new Dictionary<int, string>();
            var initiativeSummaries = new Dictionary<int, string>();

            foreach (var initiative in data.Initiatives)
            {
                var summary = await SummarizeInitiativeAsync(initiative, data, rangeStart, rangeEnd, workItemSummaries, epicSummaries);
                initiativeSummaries[initiative.Id] = summary;

                _log.Here().Information("Summarized Initiative {JiraId} \"{Title}\".", initiative.JiraId, initiative.Title);
            }

            return (workItemSummaries, epicSummaries, initiativeSummaries);
        }

        /// <summary>Type A: a leaf WorkItem (Bug/Task/Spike/Subtask/StoryBug) - own comments only, no weighting.</summary>
        private async Task<string> SummarizeLeafAsync(WorkItem item, HierarchyData data, DateTime rangeStart, DateTime rangeEnd)
        {
            var comments = data.CommentsByWorkItemId.GetValueOrDefault(item.Id, []);

            string summary;
            if (comments.Count == 0)
            {
                summary = NoActivityPlaceholder;
            }
            else
            {
                var userMessage = BuildLeafUserMessage(BuildTicketHeader(item), rangeStart, rangeEnd, comments);
                summary = await GetSummaryAsync(TypeASystemPrompt, userMessage);
            }

            _log.Here().Information("Summarized {Type} {JiraId} \"{Title}\".", item.Type, item.JiraId, item.Title);
            return summary;
        }

        /// <summary>Type B: a Story - summarizes its Subtasks/StoryBugs first, then rolls those summaries plus its own comments up, no weighting.</summary>
        private async Task<string> SummarizeStoryAsync(WorkItem story, HierarchyData data, DateTime rangeStart, DateTime rangeEnd, Dictionary<int, string> workItemSummaries)
        {
            var children = data.ChildrenByParentWorkItemId.GetValueOrDefault(story.Id, []);
            var childSummaries = new List<(string Label, string SummaryText)>();

            foreach (var child in children)
            {
                var childSummary = await SummarizeLeafAsync(child, data, rangeStart, rangeEnd);
                workItemSummaries[child.Id] = childSummary;
                childSummaries.Add(($"{child.Type} \"{child.Title}\"", childSummary));
            }

            var ownComments = data.CommentsByWorkItemId.GetValueOrDefault(story.Id, []);

            string summary;
            if (HasNoActivity(ownComments, childSummaries))
            {
                summary = NoActivityPlaceholder;
            }
            else
            {
                var userMessage = BuildRollupUserMessage(
                    BuildTicketHeader(story), rangeStart, rangeEnd,
                    "Comments on this Story directly:", ownComments,
                    "Related sub-ticket summaries:", childSummaries);

                summary = await GetSummaryAsync(TypeBSystemPrompt, userMessage);
            }

            _log.Here().Information("Summarized {Type} {JiraId} \"{Title}\".", story.Type, story.JiraId, story.Title);
            return summary;
        }

        /// <summary>Type C (Engineering): an Epic - summarizes its work items first (Story via <see cref="SummarizeStoryAsync"/>, others via <see cref="SummarizeLeafAsync"/>), then rolls those up Dev/QA-weighted.</summary>
        private async Task<string> SummarizeEpicAsync(Epic epic, HierarchyData data, DateTime rangeStart, DateTime rangeEnd, Dictionary<int, string> workItemSummaries)
        {
            var topLevelItems = data.TopLevelItemsByEpicId.GetValueOrDefault(epic.Id, []);
            var itemSummaries = new List<(string Label, string SummaryText)>();

            foreach (var item in topLevelItems)
            {
                var summary = item.Type == "Story"
                    ? await SummarizeStoryAsync(item, data, rangeStart, rangeEnd, workItemSummaries)
                    : await SummarizeLeafAsync(item, data, rangeStart, rangeEnd);

                workItemSummaries[item.Id] = summary;
                itemSummaries.Add(($"{item.Type} \"{item.Title}\"", summary));
            }

            var ownComments = data.CommentsByEpicId.GetValueOrDefault(epic.Id, []);

            if (HasNoActivity(ownComments, itemSummaries))
                return NoActivityPlaceholder;

            var userMessage = BuildRollupUserMessage(
                BuildEpicHeader(epic), rangeStart, rangeEnd,
                "Comments on this Epic directly:", ownComments,
                "Work item summaries:", itemSummaries);

            return await GetSummaryAsync(EpicSystemPrompt, userMessage);
        }

        /// <summary>Type C (Business): an Initiative - summarizes its Epics first, then rolls those up ScrumMaster/Stakeholder/EngineeringManager-weighted.</summary>
        private async Task<string> SummarizeInitiativeAsync(
            Initiative initiative, HierarchyData data, DateTime rangeStart, DateTime rangeEnd,
            Dictionary<int, string> workItemSummaries, Dictionary<int, string> epicSummaries)
        {
            var epics = data.EpicsByInitiativeId.GetValueOrDefault(initiative.Id, []);
            var epicSummaryTuples = new List<(string Label, string SummaryText)>();

            foreach (var epic in epics)
            {
                var summary = await SummarizeEpicAsync(epic, data, rangeStart, rangeEnd, workItemSummaries);
                epicSummaries[epic.Id] = summary;
                epicSummaryTuples.Add(($"Epic \"{epic.Title}\"", summary));
            }

            var ownComments = data.CommentsByInitiativeId.GetValueOrDefault(initiative.Id, []);

            if (HasNoActivity(ownComments, epicSummaryTuples))
                return NoActivityPlaceholder;

            var userMessage = BuildRollupUserMessage(
                BuildInitiativeHeader(initiative), rangeStart, rangeEnd,
                "Comments on this Initiative directly:", ownComments,
                "Epic engineering summaries:", epicSummaryTuples);

            return await GetSummaryAsync(InitiativeSystemPrompt, userMessage);
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Clears the three summary tables and inserts the freshly built rows in one CompleteAsync() -
        /// overwrite, no history, per CLAUDE.md's decision. The delete happens here (right before the
        /// insert), not before BuildSummaryChainAsync, so a mid-run failure while calling the LLM
        /// leaves the previous run's summaries intact instead of wiping them for nothing.
        /// </summary>
        private async Task PersistSummariesAsync(
            Dictionary<int, string> workItemSummaries, Dictionary<int, string> epicSummaries, Dictionary<int, string> initiativeSummaries,
            DateTime rangeStart, DateTime rangeEnd)
        {
            await _unitOfWork.DeleteAllSummariesAsync();

            var generatedAt = DateTime.Now;

            foreach (var (workItemId, summaryText) in workItemSummaries)
            {
                await _unitOfWork.WorkItemSummaries.AddAsync(new WorkItemSummary
                {
                    WorkItemId = workItemId,
                    SummaryText = summaryText,
                    RangeStart = rangeStart,
                    RangeEnd = rangeEnd,
                    GeneratedAt = generatedAt
                });
            }

            foreach (var (epicId, summaryText) in epicSummaries)
            {
                await _unitOfWork.EpicEngineeringSummaries.AddAsync(new EpicEngineeringSummary
                {
                    EpicId = epicId,
                    SummaryText = summaryText,
                    RangeStart = rangeStart,
                    RangeEnd = rangeEnd,
                    GeneratedAt = generatedAt
                });
            }

            foreach (var (initiativeId, summaryText) in initiativeSummaries)
            {
                await _unitOfWork.InitiativeBusinessSummaries.AddAsync(new InitiativeBusinessSummary
                {
                    InitiativeId = initiativeId,
                    SummaryText = summaryText,
                    RangeStart = rangeStart,
                    RangeEnd = rangeEnd,
                    GeneratedAt = generatedAt
                });
            }

            await _unitOfWork.CompleteAsync();
        }

        #endregion

        #region Data loading

        /// <summary>
        /// Loads the whole hierarchy into flat lookups keyed by parent id, filtered to the given date
        /// range for comments. No DAL navigation properties are populated/mutated here - IRepository
        /// has no Include support, so this builds its own lookups from flat query results instead,
        /// matching the "load level-by-level" approach already used by JiraHierarchyLoaderService.
        /// </summary>
        private async Task<HierarchyData> LoadHierarchyDataAsync(IReadOnlyCollection<Comment> allComments, DateTime rangeStart, DateTime rangeEnd)
        {
            var initiatives = (await _unitOfWork.Initiatives.GetAllAsync()).ToList();
            var epics = (await _unitOfWork.Epics.GetAllAsync()).ToList();
            var workItems = (await _unitOfWork.WorkItems.GetAllAsync()).ToList();

            var inRangeComments = allComments
                .Where(c => c.Timestamp >= rangeStart && c.Timestamp <= rangeEnd)
                .ToList();

            return new HierarchyData
            {
                Initiatives = initiatives,
                EpicsByInitiativeId = epics
                    .GroupBy(e => e.InitiativeId)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                TopLevelItemsByEpicId = workItems
                    .Where(w => w.EpicId.HasValue)
                    .GroupBy(w => w.EpicId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                ChildrenByParentWorkItemId = workItems
                    .Where(w => w.ParentWorkItemId.HasValue)
                    .GroupBy(w => w.ParentWorkItemId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                CommentsByInitiativeId = inRangeComments
                    .Where(c => c.InitiativeId.HasValue)
                    .GroupBy(c => c.InitiativeId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                CommentsByEpicId = inRangeComments
                    .Where(c => c.EpicId.HasValue)
                    .GroupBy(c => c.EpicId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                CommentsByWorkItemId = inRangeComments
                    .Where(c => c.WorkItemId.HasValue)
                    .GroupBy(c => c.WorkItemId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList())
            };
        }

        /// <summary>
        /// Flat, in-memory view of the hierarchy for one SummarizationService run: entities keyed by
        /// their parent's id, and comments already filtered to the configured date range.
        /// </summary>
        private sealed class HierarchyData
        {
            public required List<Initiative> Initiatives { get; init; }
            public required Dictionary<int, List<Epic>> EpicsByInitiativeId { get; init; }
            public required Dictionary<int, List<WorkItem>> TopLevelItemsByEpicId { get; init; }
            public required Dictionary<int, List<WorkItem>> ChildrenByParentWorkItemId { get; init; }
            public required Dictionary<int, List<Comment>> CommentsByInitiativeId { get; init; }
            public required Dictionary<int, List<Comment>> CommentsByEpicId { get; init; }
            public required Dictionary<int, List<Comment>> CommentsByWorkItemId { get; init; }
        }

        #endregion
    }
}
