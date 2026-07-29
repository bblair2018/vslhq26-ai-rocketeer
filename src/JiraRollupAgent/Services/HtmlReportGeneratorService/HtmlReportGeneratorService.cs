using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Extensions;
using Microsoft.Extensions.Configuration;

namespace JiraRollupAgent.Services.HtmlReportGeneratorService
{
    /// <summary>
    /// The "Report" stage: renders <c>report/rollup-report.html</c> - Initiatives ordered by
    /// <see cref="Initiative.PriorityRank"/>, each showing its Business Summary with its Epics
    /// nested underneath showing their Engineering Summaries. Self-disables after a successful
    /// run - see <see cref="DisableReportGenerationFlagAsync"/>.
    /// </summary>
    public class HtmlReportGeneratorService : IHtmlReportGeneratorService
    {
        //This is required to enable logging for this class.
        private readonly Serilog.ILogger _log = Serilog.Log.ForContext<HtmlReportGeneratorService>();
        private readonly IConfiguration _config;
        private readonly IUnitOfWork _unitOfWork;

        /// <summary>Marker file that only exists at the repo root - used to locate report/ regardless of build output layout.</summary>
        private const string SolutionFileName = "vslhq26-ai-rocketeer.slnx";

        /// <summary>The fixed output filename, overwritten every run.</summary>
        private const string ReportFileName = "rollup-report.html";

        /// <summary>Placeholder text rendered when an Initiative/Epic has no generated summary yet.</summary>
        internal const string NoSummaryPlaceholder = "Summary not yet generated.";

        /// <summary>Creates the report generator service.</summary>
        /// <param name="config">Application configuration, used to read the <c>AppSettings:RunReportGeneration</c> flag.</param>
        /// <param name="unitOfWork">Provides access to the Initiative/Epic and summary repositories the report reads from.</param>
        public HtmlReportGeneratorService(IConfiguration config, IUnitOfWork unitOfWork)
        {
            _config = config;
            _unitOfWork = unitOfWork;
        }

        #region Run

        /// <inheritdoc/>
        public async Task<bool> Run() => await Run(repoRootOverride: null);

        /// <summary>
        /// Overload of <see cref="Run()"/> that accepts an optional repo-root override, threaded down to
        /// <see cref="WriteReportAsync"/>/<see cref="FindRepoRoot"/> - used by tests to exercise the full
        /// success path (including the actual file write) against an isolated temp directory instead of
        /// walking up to the real repo root and overwriting the real <c>report/rollup-report.html</c>.
        /// </summary>
        /// <param name="repoRootOverride">Overrides where <see cref="FindRepoRoot"/> starts its search; <c>null</c> uses the real <see cref="AppDomain.CurrentDomain"/> base directory.</param>
        internal async Task<bool> Run(string? repoRootOverride)
        {
            var runStage = _config.GetValue<bool>("AppSettings:RunReportGeneration");

            try
            {
                if (runStage)
                {
                    _log.Here().Information("Starting process to generate the HTML rollup report...");

                    var data = await LoadReportDataAsync();

                    if (data.InitiativeSummaries.Count == 0)
                        throw new InvalidOperationException(
                            "No Initiative summaries found - has SummarizationService been run yet? (see AppSettings:RunSummarization)");

                    var html = BuildReportHtml(data);
                    var outputPath = await WriteReportAsync(html, repoRootOverride);

                    _log.Here().Information(
                        "Process to generate the HTML rollup report completed successfully: wrote {InitiativeCount} initiatives, {EpicCount} epics to {OutputPath}.",
                        data.Initiatives.Count, data.EpicsByInitiativeId.Values.Sum(e => e.Count), outputPath);

                    await DisableReportGenerationFlagAsync();

                    return true;
                }
                else
                {
                    _log.Here().Information("Process to generate the HTML rollup report completed ** SKIPPED **");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Process to generate the HTML rollup report ** FAILED! **");
                return false;
            }
        }

        #endregion

        #region Self-disable

        /// <summary>
        /// Flips AppSettings:RunReportGeneration to false in appsettings.json after a successful run,
        /// so the report is generated once and subsequent runs skip re-rendering it. Setting it back
        /// to true by hand causes the next run to overwrite report/rollup-report.html fresh from
        /// whatever's currently in the summary tables - same shape as JiraHierarchyLoaderService's
        /// DisableHierarchyLoadFlagAsync and SummarizationService's DisableSummarizationFlagAsync.
        /// </summary>
        /// <param name="appSettingsPathOverride">Overrides the appsettings.json path to read/write; <c>null</c> uses the real build output path. Used by tests to exercise the "missing AppSettings section" and read/parse-failure branches against an isolated temp file.</param>
        internal async Task DisableReportGenerationFlagAsync(string? appSettingsPathOverride = null)
        {
            try
            {
                var appSettingsPath = appSettingsPathOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                var json = await File.ReadAllTextAsync(appSettingsPath);
                var root = JsonNode.Parse(json)?.AsObject();

                if (root?["AppSettings"] is not JsonObject appSettings)
                {
                    _log.Here().Warning("Could not find an \"AppSettings\" section in appsettings.json - leaving RunReportGeneration as-is.");
                    return;
                }

                appSettings["RunReportGeneration"] = false;

                var writeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                await File.WriteAllTextAsync(appSettingsPath, root!.ToJsonString(writeOptions));

                _log.Here().Information("Set AppSettings:RunReportGeneration to false so the next run skips regenerating the report.");
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Failed to disable AppSettings:RunReportGeneration after a successful run - it will run again next time.");
            }
        }

        #endregion

        #region Data loading

        /// <summary>
        /// Loads Initiatives (ordered by PriorityRank), their Epics (ordered by Id - no ordering spec
        /// exists for Epics within an Initiative), and both summary tables, all as flat lookups. No
        /// DAL navigation properties are populated - same "no Include support" pattern as
        /// SummarizationService's LoadHierarchyDataAsync.
        /// </summary>
        private async Task<ReportData> LoadReportDataAsync()
        {
            var initiatives = (await _unitOfWork.Initiatives.GetAllAsync())
                .OrderBy(i => i.PriorityRank)
                .ToList();
            var epics = (await _unitOfWork.Epics.GetAllAsync()).ToList();
            var initiativeSummaries = (await _unitOfWork.InitiativeBusinessSummaries.GetAllAsync()).ToList();
            var epicSummaries = (await _unitOfWork.EpicEngineeringSummaries.GetAllAsync()).ToList();

            return new ReportData
            {
                Initiatives = initiatives,
                EpicsByInitiativeId = epics
                    .OrderBy(e => e.Id)
                    .GroupBy(e => e.InitiativeId)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                InitiativeSummaries = initiativeSummaries.ToDictionary(s => s.InitiativeId, s => s),
                EpicSummaries = epicSummaries.ToDictionary(s => s.EpicId, s => s)
            };
        }

        /// <summary>Flat, in-memory view of the report's data for one HtmlReportGeneratorService run.</summary>
        internal sealed class ReportData
        {
            /// <summary>Every Initiative, ordered by <see cref="Initiative.PriorityRank"/>.</summary>
            public required List<Initiative> Initiatives { get; init; }
            /// <summary>Every Epic, grouped by owning Initiative id and ordered by <see cref="Epic.Id"/> within each group.</summary>
            public required Dictionary<int, List<Epic>> EpicsByInitiativeId { get; init; }
            /// <summary>Generated Business Summaries, keyed by Initiative id.</summary>
            public required Dictionary<int, InitiativeBusinessSummary> InitiativeSummaries { get; init; }
            /// <summary>Generated Engineering Summaries, keyed by Epic id.</summary>
            public required Dictionary<int, EpicEngineeringSummary> EpicSummaries { get; init; }
        }

        #endregion

        #region HTML rendering

        /// <summary>Renders the complete self-contained HTML document (embedded CSS, no external dependencies).</summary>
        /// <param name="data">The loaded report data.</param>
        /// <returns>The full HTML document as a string.</returns>
        internal string BuildReportHtml(ReportData data)
        {
            var (rangeStart, rangeEnd) = data.InitiativeSummaries.Values
                .Select(s => (s.RangeStart, s.RangeEnd))
                .First();

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<title>Jira Rollup Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(ReportCss);
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            sb.AppendLine("<header>");
            sb.AppendLine("<h1>Jira Rollup Report</h1>");
            sb.AppendLine($"<p class=\"subtitle\">Activity from {rangeStart:MMMM d, yyyy} to {rangeEnd:MMMM d, yyyy}</p>");
            sb.AppendLine("</header>");

            sb.AppendLine("<main>");
            foreach (var initiative in data.Initiatives)
                AppendInitiative(sb, initiative, data);
            sb.AppendLine("</main>");

            sb.AppendLine("<footer>");
            sb.AppendLine($"<p>Generated {DateTime.Now:MMMM d, yyyy 'at' h:mm tt}</p>");
            sb.AppendLine("</footer>");

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        /// <summary>Appends one Initiative section (priority badge, title, status, Business Summary) plus its nested Epics.</summary>
        /// <param name="sb">The HTML being built.</param>
        /// <param name="initiative">The Initiative to render.</param>
        /// <param name="data">The loaded report data, used to look up this Initiative's summary and Epics.</param>
        internal void AppendInitiative(StringBuilder sb, Initiative initiative, ReportData data)
        {
            var hasSummary = data.InitiativeSummaries.TryGetValue(initiative.Id, out var summary);
            var summaryHtml = hasSummary ? FormatSummaryText(summary!.SummaryText) : Encode(NoSummaryPlaceholder);
            var summaryClass = hasSummary ? "business-summary" : "business-summary placeholder";

            sb.AppendLine("<section class=\"initiative\">");
            sb.AppendLine("<div class=\"initiative-header\">");
            sb.AppendLine($"<span class=\"priority-badge\">#{initiative.PriorityRank}</span>");
            sb.AppendLine($"<h2>{Encode(initiative.Title)}</h2>");
            sb.AppendLine($"<span class=\"jira-id\">{Encode(initiative.JiraId)}</span>");
            sb.AppendLine($"<span class=\"status-badge\">{Encode(initiative.Status)}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<div class=\"{summaryClass}\">{summaryHtml}</div>");

            var epics = data.EpicsByInitiativeId.GetValueOrDefault(initiative.Id, []);
            if (epics.Count > 0)
            {
                sb.AppendLine("<div class=\"epics\">");
                foreach (var epic in epics)
                    AppendEpic(sb, epic, data);
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</section>");
        }

        /// <summary>Appends one Epic's markup (title, its Engineering Summary) nested inside its Initiative's section.</summary>
        /// <param name="sb">The HTML being built.</param>
        /// <param name="epic">The Epic to render.</param>
        /// <param name="data">The loaded report data, used to look up this Epic's summary.</param>
        internal void AppendEpic(StringBuilder sb, Epic epic, ReportData data)
        {
            var hasSummary = data.EpicSummaries.TryGetValue(epic.Id, out var summary);
            var summaryHtml = hasSummary ? FormatSummaryText(summary!.SummaryText) : Encode(NoSummaryPlaceholder);
            var summaryClass = hasSummary ? "engineering-summary" : "engineering-summary placeholder";

            sb.AppendLine("<div class=\"epic\">");
            sb.AppendLine("<div class=\"epic-header\">");
            sb.AppendLine($"<h3>{Encode(epic.Title)}</h3>");
            sb.AppendLine($"<span class=\"jira-id\">{Encode(epic.JiraId)}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<div class=\"{summaryClass}\">{summaryHtml}</div>");
            sb.AppendLine("</div>");
        }

        /// <summary>HTML-encodes a raw string (escapes <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, quotes, and non-ASCII characters as entities).</summary>
        /// <param name="text">The raw text to encode.</param>
        internal static string Encode(string text) => System.Net.WebUtility.HtmlEncode(text);

        /// <summary>Matches "**bold**" markdown-style emphasis, capturing the enclosed text.</summary>
        private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        /// <summary>Matches a "Status: ..." line, capturing the text after the colon.</summary>
        private static readonly Regex StatusLineRegex = new(@"^Status\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>Matches a bare "Key Progress:"/"Risks/Blockers:"/"Risks:"/"Blockers:" section heading line.</summary>
        private static readonly Regex SectionHeaderRegex = new(@"^(Key Progress|Risks/Blockers|Risks|Blockers)\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parses the Type C structured format ("Status: ..." line, "Key Progress:"/"Risks/Blockers:"
        /// headings each followed by "- " bullets - see SummarizationService's StructuredOutputFormat)
        /// into real HTML: a status paragraph, section headings, and &lt;ul&gt;&lt;li&gt; bullet lists.
        /// HTML-encodes every line first, then converts "**bold**" to &lt;strong&gt; on the encoded
        /// text (safe since ** isn't affected by HTML entity escaping).
        /// </summary>
        /// <param name="text">The raw summary text returned by the LLM.</param>
        /// <returns>HTML markup ready to embed directly in the page.</returns>
        internal static string FormatSummaryText(string text)
        {
            var sb = new StringBuilder();
            var inList = false;

            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    if (!inList)
                    {
                        sb.Append("<ul>");
                        inList = true;
                    }
                    sb.Append($"<li>{ApplyBold(Encode(line[2..].Trim()))}</li>");
                    continue;
                }

                if (inList)
                {
                    sb.Append("</ul>");
                    inList = false;
                }

                var statusMatch = StatusLineRegex.Match(line);
                if (statusMatch.Success)
                {
                    sb.Append($"<p class=\"status-line\"><strong>Status:</strong> {ApplyBold(Encode(statusMatch.Groups[1].Value.Trim()))}</p>");
                    continue;
                }

                var headerMatch = SectionHeaderRegex.Match(line);
                if (headerMatch.Success)
                {
                    sb.Append($"<p class=\"summary-heading\">{Encode(headerMatch.Groups[1].Value)}</p>");
                    continue;
                }

                sb.Append($"<p>{ApplyBold(Encode(line))}</p>");
            }

            if (inList)
                sb.Append("</ul>");

            return sb.ToString();
        }

        /// <summary>Converts "**bold**" markers to <c>&lt;strong&gt;</c> tags on already-HTML-encoded text.</summary>
        /// <param name="encodedText">Text that has already been passed through <see cref="Encode"/>.</param>
        internal static string ApplyBold(string encodedText) => BoldRegex.Replace(encodedText, "<strong>$1</strong>");

        /// <summary>The report page's embedded stylesheet - self-contained, no external dependencies.</summary>
        private const string ReportCss = """
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
                font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
                background: #f4f6f8;
                color: #1a1a2e;
                line-height: 1.6;
                padding: 40px 20px;
            }
            header { max-width: 900px; margin: 0 auto 32px auto; text-align: center; }
            header h1 { font-size: 28px; color: #0f2847; margin-bottom: 6px; }
            .subtitle { color: #5a6b7d; font-size: 15px; }
            main { max-width: 900px; margin: 0 auto; display: flex; flex-direction: column; gap: 24px; }
            .initiative {
                background: #ffffff;
                border-radius: 10px;
                box-shadow: 0 1px 3px rgba(0,0,0,0.08), 0 1px 2px rgba(0,0,0,0.06);
                padding: 24px 28px;
                border-left: 4px solid #2563eb;
            }
            .initiative-header { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
            .priority-badge {
                background: #2563eb; color: #ffffff; font-weight: 600; font-size: 13px;
                padding: 3px 10px; border-radius: 12px; flex-shrink: 0;
            }
            .initiative-header h2 { font-size: 20px; color: #0f2847; flex: 1; }
            .jira-id {
                font-family: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
                font-size: 12px; color: #8a94a3; background: #f0f2f5; padding: 2px 8px; border-radius: 6px;
            }
            .status-badge { background: #eef2f7; color: #445468; font-size: 12px; font-weight: 500; padding: 3px 10px; border-radius: 12px; }
            .business-summary { font-size: 15px; color: #2c3542; }
            .epics { margin-top: 18px; padding-top: 18px; border-top: 1px solid #eef0f3; display: flex; flex-direction: column; gap: 14px; }
            .epic { background: #f8fafc; border-radius: 8px; padding: 16px 20px; border-left: 3px solid #64748b; }
            .epic-header { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; flex-wrap: wrap; }
            .epic-header h3 { font-size: 15px; color: #24303f; }
            .engineering-summary { font-size: 13.5px; color: #445468; }
            .business-summary p, .engineering-summary p { margin: 6px 0; }
            .business-summary .status-line, .engineering-summary .status-line { margin-top: 0; }
            .business-summary ul, .engineering-summary ul { margin: 2px 0 10px 20px; padding: 0; }
            .business-summary li, .engineering-summary li { margin: 3px 0; }
            .summary-heading {
                font-weight: 600; font-size: 0.8em; text-transform: uppercase; letter-spacing: 0.05em;
                color: #64748b; margin-top: 12px; margin-bottom: 2px;
            }
            .placeholder { font-style: italic; color: #9aa5b1; }
            footer { max-width: 900px; margin: 40px auto 0 auto; text-align: center; color: #9aa5b1; font-size: 12px; }
            """;

        #endregion

        #region Output path resolution

        /// <summary>
        /// Walks up from the build output directory looking for the .slnx marker file rather than
        /// hardcoding a fixed number of "..\..\.." levels - robust to Debug/Release/publish layout
        /// changes, still with zero CWD-dependence (same "avoid CWD" philosophy as the Logs/ path
        /// in Program.cs, just a smarter way to find "repo root" instead of "next to the exe").
        /// </summary>
        /// <param name="startDirectory">The directory to start the upward search from; defaults to <see cref="AppDomain.CurrentDomain"/>'s <c>BaseDirectory</c>. Overridable so tests can point this at an isolated temp directory instead of the real repo root.</param>
        /// <returns>The absolute path to the repo root.</returns>
        internal static string FindRepoRoot(string? startDirectory = null)
        {
            var dir = new DirectoryInfo(startDirectory ?? AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
                dir = dir.Parent;

            return dir?.FullName
                ?? throw new InvalidOperationException(
                    $"Could not locate repo root ('{SolutionFileName}' not found in any parent directory of {startDirectory ?? AppDomain.CurrentDomain.BaseDirectory}).");
        }

        /// <summary>Writes to a single fixed filename in report/, overwritten every run - same "overwrite, no history" philosophy as the summary tables.</summary>
        /// <param name="html">The complete HTML document to write.</param>
        /// <param name="repoRootOverride">Overrides where <see cref="FindRepoRoot"/> starts its search; <c>null</c> uses the real repo root. Used by tests to write into an isolated temp directory.</param>
        /// <returns>The absolute path the report was written to.</returns>
        internal async Task<string> WriteReportAsync(string html, string? repoRootOverride = null)
        {
            var reportDirectory = Path.Combine(FindRepoRoot(repoRootOverride), "report");
            Directory.CreateDirectory(reportDirectory);

            var outputPath = Path.Combine(reportDirectory, ReportFileName);
            await File.WriteAllTextAsync(outputPath, html);
            return outputPath;
        }

        #endregion
    }
}
