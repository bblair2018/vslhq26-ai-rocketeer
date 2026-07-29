using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Extensions;
using Microsoft.Extensions.Configuration;

namespace JiraRollupAgent.Services.HtmlReportGeneratorService
{
    [ExcludeFromCodeCoverage]
    public class HtmlReportGeneratorService : IHtmlReportGeneratorService
    {
        //This is required to enable logging for this class.
        private readonly Serilog.ILogger _log = Serilog.Log.ForContext<HtmlReportGeneratorService>();
        private readonly IConfiguration _config;
        private readonly IUnitOfWork _unitOfWork;

        /// <summary>Marker file that only exists at the repo root - used to locate report/ regardless of build output layout.</summary>
        private const string SolutionFileName = "vslhq26-ai-rocketeer.slnx";
        private const string ReportFileName = "rollup-report.html";
        private const string NoSummaryPlaceholder = "Summary not yet generated.";

        public HtmlReportGeneratorService(IConfiguration config, IUnitOfWork unitOfWork)
        {
            _config = config;
            _unitOfWork = unitOfWork;
        }

        #region Run

        public async Task<bool> Run()
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
                    var outputPath = await WriteReportAsync(html);

                    _log.Here().Information(
                        "Process to generate the HTML rollup report completed successfully: wrote {InitiativeCount} initiatives, {EpicCount} epics to {OutputPath}.",
                        data.Initiatives.Count, data.EpicsByInitiativeId.Values.Sum(e => e.Count), outputPath);
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
        private sealed class ReportData
        {
            public required List<Initiative> Initiatives { get; init; }
            public required Dictionary<int, List<Epic>> EpicsByInitiativeId { get; init; }
            public required Dictionary<int, InitiativeBusinessSummary> InitiativeSummaries { get; init; }
            public required Dictionary<int, EpicEngineeringSummary> EpicSummaries { get; init; }
        }

        #endregion

        #region HTML rendering

        private string BuildReportHtml(ReportData data)
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

        private void AppendInitiative(StringBuilder sb, Initiative initiative, ReportData data)
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
            sb.AppendLine($"<p class=\"{summaryClass}\">{summaryHtml}</p>");

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

        private void AppendEpic(StringBuilder sb, Epic epic, ReportData data)
        {
            var hasSummary = data.EpicSummaries.TryGetValue(epic.Id, out var summary);
            var summaryHtml = hasSummary ? FormatSummaryText(summary!.SummaryText) : Encode(NoSummaryPlaceholder);
            var summaryClass = hasSummary ? "engineering-summary" : "engineering-summary placeholder";

            sb.AppendLine("<div class=\"epic\">");
            sb.AppendLine("<div class=\"epic-header\">");
            sb.AppendLine($"<h3>{Encode(epic.Title)}</h3>");
            sb.AppendLine($"<span class=\"jira-id\">{Encode(epic.JiraId)}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<p class=\"{summaryClass}\">{summaryHtml}</p>");
            sb.AppendLine("</div>");
        }

        private static string Encode(string text) => System.Net.WebUtility.HtmlEncode(text);

        /// <summary>HTML-encodes the summary, then renders the "**bold**" emphasis the model uses for risk/status callouts as &lt;strong&gt;.</summary>
        private static string FormatSummaryText(string text)
            => Regex.Replace(Encode(text), @"\*\*(.+?)\*\*", "<strong>$1</strong>");

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
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
                dir = dir.Parent;

            return dir?.FullName
                ?? throw new InvalidOperationException(
                    $"Could not locate repo root ('{SolutionFileName}' not found in any parent directory of {AppDomain.CurrentDomain.BaseDirectory}).");
        }

        /// <summary>Writes to a single fixed filename in report/, overwritten every run - same "overwrite, no history" philosophy as the summary tables.</summary>
        private async Task<string> WriteReportAsync(string html)
        {
            var reportDirectory = Path.Combine(FindRepoRoot(), "report");
            Directory.CreateDirectory(reportDirectory);

            var outputPath = Path.Combine(reportDirectory, ReportFileName);
            await File.WriteAllTextAsync(outputPath, html);
            return outputPath;
        }

        #endregion
    }
}
