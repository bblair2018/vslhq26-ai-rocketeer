using System.Text;
using FluentAssertions;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using ReportGeneratorService = JiraRollupAgent.Services.HtmlReportGeneratorService.HtmlReportGeneratorService;

namespace JiraRollupAgent.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ReportGeneratorService"/>: the pure HTML/text-formatting helpers
    /// (<c>Encode</c>, <c>ApplyBold</c>, <c>FormatSummaryText</c>), the repo-root resolution helper,
    /// the HTML-assembly methods, and <c>Run()</c>'s skip/failure/success paths. The full success
    /// path is exercised via the <c>Run(string? repoRootOverride)</c>/<c>WriteReportAsync(html,
    /// repoRootOverride)</c> overloads pointed at an isolated temp directory, rather than the
    /// parameterless <c>Run()</c> - which walks up from the test binary's own directory looking for
    /// the real repo's <c>.slnx</c> marker file and would overwrite the real
    /// <c>report/rollup-report.html</c> if exercised end-to-end.
    /// </summary>
    public class HtmlReportGeneratorServiceTests
    {
        /// <summary>Builds an <see cref="IConfiguration"/> backed by an in-memory dictionary of the given key/value pairs.</summary>
        /// <param name="values">The <c>AppSettings:*</c> key/value pairs to seed.</param>
        private static IConfiguration BuildConfig(params (string Key, string Value)[] values)
        {
            var dict = new Dictionary<string, string?>();
            foreach (var (key, value) in values)
                dict[key] = value;

            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        /// <summary>Creates a service instance backed by a no-op configuration and a strict (never-called) unit of work - for tests that only exercise pure formatting/HTML-assembly helpers.</summary>
        private static ReportGeneratorService CreateService()
            => new(BuildConfig(), Mock.Of<IUnitOfWork>());

        #region Encode

        /// <summary>Encode escapes the HTML-significant characters &lt;, &gt;, &amp;, and " as entities.</summary>
        [Fact]
        public void Encode_WithHtmlSignificantCharacters_EscapesThem()
        {
            // Arrange
            var raw = "<script>alert(\"x\")</script> & more";

            // Act
            var result = ReportGeneratorService.Encode(raw);

            // Assert
            result.Should().Contain("&lt;").And.Contain("&gt;").And.Contain("&amp;").And.Contain("&quot;");
            result.Should().NotContain("<script>");
        }

        /// <summary>Encode leaves plain text with no special characters unchanged.</summary>
        [Fact]
        public void Encode_WithPlainText_ReturnsUnchanged()
        {
            // Arrange
            var raw = "Nothing special here 123";

            // Act
            var result = ReportGeneratorService.Encode(raw);

            // Assert
            result.Should().Be(raw);
        }

        #endregion

        #region ApplyBold

        /// <summary>ApplyBold converts a single "**bold**" span into a &lt;strong&gt; tag.</summary>
        [Fact]
        public void ApplyBold_WithSingleBoldSpan_ConvertsToStrongTag()
        {
            // Arrange
            var text = "This is **important** news.";

            // Act
            var result = ReportGeneratorService.ApplyBold(text);

            // Assert
            result.Should().Be("This is <strong>important</strong> news.");
        }

        /// <summary>ApplyBold converts every "**...**" span when there are multiple in the same string.</summary>
        [Fact]
        public void ApplyBold_WithMultipleBoldSpans_ConvertsAllOfThem()
        {
            // Arrange
            var text = "**First** and **second**.";

            // Act
            var result = ReportGeneratorService.ApplyBold(text);

            // Assert
            result.Should().Be("<strong>First</strong> and <strong>second</strong>.");
        }

        /// <summary>ApplyBold returns the text unchanged when there is no "**" marker present.</summary>
        [Fact]
        public void ApplyBold_WithNoBoldMarkers_ReturnsUnchanged()
        {
            // Arrange
            var text = "Plain text, no emphasis.";

            // Act
            var result = ReportGeneratorService.ApplyBold(text);

            // Assert
            result.Should().Be(text);
        }

        #endregion

        #region FormatSummaryText

        /// <summary>A summary with only a Status line renders a single status paragraph.</summary>
        [Fact]
        public void FormatSummaryText_WithStatusLineOnly_RendersStatusParagraph()
        {
            // Arrange
            var text = "Status: Everything is on track.";

            // Act
            var result = ReportGeneratorService.FormatSummaryText(text);

            // Assert
            result.Should().Be("<p class=\"status-line\"><strong>Status:</strong> Everything is on track.</p>");
        }

        /// <summary>Status + Key Progress bullets render a status paragraph, a heading, and a bullet list; no Risks/Blockers heading appears when that section is absent.</summary>
        [Fact]
        public void FormatSummaryText_WithStatusAndKeyProgress_RendersHeadingAndBulletList()
        {
            // Arrange
            var text = "Status: On track.\nKey Progress:\n- Did X\n- Did Y";

            // Act
            var result = ReportGeneratorService.FormatSummaryText(text);

            // Assert
            result.Should().Contain("<p class=\"status-line\"><strong>Status:</strong> On track.</p>");
            result.Should().Contain("<p class=\"summary-heading\">Key Progress</p>");
            result.Should().Contain("<ul><li>Did X</li><li>Did Y</li></ul>");
            result.Should().NotContain("Risks");
        }

        /// <summary>A Risks/Blockers section, when present, renders its own heading and bullet list after Key Progress.</summary>
        [Fact]
        public void FormatSummaryText_WithRisksSection_RendersRisksHeadingAndBullets()
        {
            // Arrange
            var text = "Status: At risk.\nKey Progress:\n- Did X\nRisks/Blockers:\n- Vendor delay";

            // Act
            var result = ReportGeneratorService.FormatSummaryText(text);

            // Assert
            result.Should().Contain("<p class=\"summary-heading\">Risks/Blockers</p>");
            result.Should().Contain("<ul><li>Vendor delay</li></ul>");
        }

        /// <summary>Bold markers inside a bullet point are converted to &lt;strong&gt; the same as anywhere else.</summary>
        [Fact]
        public void FormatSummaryText_WithBoldInsideABullet_ConvertsBoldMarker()
        {
            // Arrange
            var text = "Key Progress:\n- **Important**: shipped the feature.";

            // Act
            var result = ReportGeneratorService.FormatSummaryText(text);

            // Assert
            result.Should().Contain("<li><strong>Important</strong>: shipped the feature.</li>");
        }

        /// <summary>FormatSummaryText produces identical output whether lines are separated by \n or \r\n.</summary>
        [Fact]
        public void FormatSummaryText_WithCrLfLineEndings_MatchesLfOutput()
        {
            // Arrange
            var lf = "Status: Fine.\nKey Progress:\n- Did X";
            var crlf = "Status: Fine.\r\nKey Progress:\r\n- Did X";

            // Act
            var lfResult = ReportGeneratorService.FormatSummaryText(lf);
            var crlfResult = ReportGeneratorService.FormatSummaryText(crlf);

            // Assert
            crlfResult.Should().Be(lfResult);
        }

        /// <summary>Blank/empty input produces empty output rather than throwing.</summary>
        [Fact]
        public void FormatSummaryText_WithEmptyInput_ReturnsEmptyString()
        {
            // Arrange
            var text = "";

            // Act
            var result = ReportGeneratorService.FormatSummaryText(text);

            // Assert
            result.Should().BeEmpty();
        }

        /// <summary>A line that is neither blank, a bullet, a "Status:" line, nor a recognized section heading renders as an ordinary paragraph.</summary>
        [Fact]
        public void FormatSummaryText_WithPlainProseLine_RendersPlainParagraph()
        {
            // Arrange
            var text = "Just a plain sentence with no special formatting.";

            // Act
            var result = ReportGeneratorService.FormatSummaryText(text);

            // Assert
            result.Should().Be("<p>Just a plain sentence with no special formatting.</p>");
        }

        #endregion

        #region FindRepoRoot

        /// <summary>FindRepoRoot walks up from a nested start directory and finds the marker file several levels above it.</summary>
        [Fact]
        public void FindRepoRoot_WithMarkerFileAboveStartDirectory_ReturnsMarkerDirectory()
        {
            // Arrange
            var root = Directory.CreateTempSubdirectory("HtmlReportGeneratorServiceTests_");
            try
            {
                File.WriteAllText(Path.Combine(root.FullName, "vslhq26-ai-rocketeer.slnx"), "");
                var nested = Path.Combine(root.FullName, "a", "b");
                Directory.CreateDirectory(nested);

                // Act
                var result = ReportGeneratorService.FindRepoRoot(nested);

                // Assert
                result.Should().Be(root.FullName);
            }
            finally
            {
                root.Delete(recursive: true);
            }
        }

        /// <summary>FindRepoRoot throws when no marker file exists anywhere up the directory chain.</summary>
        [Fact]
        public void FindRepoRoot_WithNoMarkerFileAnywhereAbove_Throws()
        {
            // Arrange
            var root = Directory.CreateTempSubdirectory("HtmlReportGeneratorServiceTests_");
            try
            {
                // Act
                Action act = () => ReportGeneratorService.FindRepoRoot(root.FullName);

                // Assert
                act.Should().Throw<InvalidOperationException>();
            }
            finally
            {
                root.Delete(recursive: true);
            }
        }

        #endregion

        #region BuildReportHtml / AppendInitiative / AppendEpic

        /// <summary>BuildReportHtml renders the Initiative's priority badge, title, and Business Summary, plus its nested Epic's title and Engineering Summary.</summary>
        [Fact]
        public void BuildReportHtml_WithInitiativeAndEpic_RendersBothSummaries()
        {
            // Arrange
            var initiative = new Initiative { Id = 1, JiraId = "INIT-1", Title = "Cockpit Avionics", PriorityRank = 1, Status = "In Progress" };
            var epic = new Epic { Id = 10, JiraId = "EPIC-1", Title = "Glass Cockpit Display", InitiativeId = 1 };
            var initiativeSummary = new InitiativeBusinessSummary
            {
                InitiativeId = 1,
                SummaryText = "Status: Good.\nKey Progress:\n- Shipped the display firmware",
                RangeStart = new DateTime(2026, 7, 1),
                RangeEnd = new DateTime(2026, 7, 31)
            };
            var epicSummary = new EpicEngineeringSummary
            {
                EpicId = 10,
                SummaryText = "Status: Good.\nKey Progress:\n- Built the rendering pipeline"
            };
            var data = new ReportGeneratorService.ReportData
            {
                Initiatives = [initiative],
                EpicsByInitiativeId = new Dictionary<int, List<Epic>> { [1] = [epic] },
                InitiativeSummaries = new Dictionary<int, InitiativeBusinessSummary> { [1] = initiativeSummary },
                EpicSummaries = new Dictionary<int, EpicEngineeringSummary> { [10] = epicSummary }
            };
            var service = CreateService();

            // Act
            var html = service.BuildReportHtml(data);

            // Assert
            html.Should().Contain("#1");
            html.Should().Contain("Cockpit Avionics");
            html.Should().Contain("Glass Cockpit Display");
            html.Should().Contain("Shipped the display firmware");
            html.Should().Contain("Built the rendering pipeline");
        }

        /// <summary>AppendEpic renders the placeholder text and CSS class when an Epic has no generated summary yet.</summary>
        [Fact]
        public void AppendEpic_WithNoSummaryForEpic_RendersPlaceholder()
        {
            // Arrange
            var epic = new Epic { Id = 99, JiraId = "EPIC-99", Title = "No Summary Yet Epic", InitiativeId = 1 };
            var data = new ReportGeneratorService.ReportData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                InitiativeSummaries = [],
                EpicSummaries = []
            };
            var service = CreateService();
            var sb = new StringBuilder();

            // Act
            service.AppendEpic(sb, epic, data);
            var html = sb.ToString();

            // Assert
            html.Should().Contain(ReportGeneratorService.NoSummaryPlaceholder);
            html.Should().Contain("engineering-summary placeholder");
        }

        /// <summary>AppendInitiative renders the placeholder text and CSS class when an Initiative has no generated summary yet.</summary>
        [Fact]
        public void AppendInitiative_WithNoSummaryForInitiative_RendersPlaceholder()
        {
            // Arrange
            var initiative = new Initiative { Id = 42, JiraId = "INIT-42", Title = "No Summary Yet Initiative", PriorityRank = 2, Status = "Open" };
            var data = new ReportGeneratorService.ReportData
            {
                Initiatives = [initiative],
                EpicsByInitiativeId = [],
                InitiativeSummaries = [],
                EpicSummaries = []
            };
            var service = CreateService();
            var sb = new StringBuilder();

            // Act
            service.AppendInitiative(sb, initiative, data);
            var html = sb.ToString();

            // Assert
            html.Should().Contain(ReportGeneratorService.NoSummaryPlaceholder);
            html.Should().Contain("business-summary placeholder");
        }

        #endregion

        #region DisableReportGenerationFlagAsync

        /// <summary>DisableReportGenerationFlagAsync leaves the file untouched and just logs a warning when the JSON has no "AppSettings" section.</summary>
        [Fact]
        public async Task DisableReportGenerationFlagAsync_WithNoAppSettingsSection_LeavesFileUnchanged()
        {
            // Arrange
            var path = Path.GetTempFileName();
            try
            {
                const string original = "{\"SomethingElse\":{}}";
                await File.WriteAllTextAsync(path, original);
                var service = CreateService();

                // Act
                await service.DisableReportGenerationFlagAsync(path);

                // Assert
                (await File.ReadAllTextAsync(path)).Should().Be(original);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>DisableReportGenerationFlagAsync swallows a read/parse failure (e.g. malformed JSON) rather than propagating it.</summary>
        [Fact]
        public async Task DisableReportGenerationFlagAsync_WithMalformedJson_DoesNotThrow()
        {
            // Arrange
            var path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "{ not valid json");
                var service = CreateService();

                // Act
                Func<Task> act = () => service.DisableReportGenerationFlagAsync(path);

                // Assert
                await act.Should().NotThrowAsync();
            }
            finally
            {
                File.Delete(path);
            }
        }

        #endregion

        #region WriteReportAsync

        /// <summary>WriteReportAsync, given a repo-root override, writes the HTML to report/rollup-report.html under that directory rather than the real repo root.</summary>
        [Fact]
        public async Task WriteReportAsync_WithRepoRootOverride_WritesFileUnderReportSubdirectory()
        {
            // Arrange
            var root = Directory.CreateTempSubdirectory("HtmlReportGeneratorServiceTests_");
            try
            {
                File.WriteAllText(Path.Combine(root.FullName, "vslhq26-ai-rocketeer.slnx"), "");
                var service = CreateService();

                // Act
                var outputPath = await service.WriteReportAsync("<html>hi</html>", root.FullName);

                // Assert
                outputPath.Should().Be(Path.Combine(root.FullName, "report", "rollup-report.html"));
                File.Exists(outputPath).Should().BeTrue();
                (await File.ReadAllTextAsync(outputPath)).Should().Be("<html>hi</html>");
            }
            finally
            {
                root.Delete(recursive: true);
            }
        }

        #endregion

        #region Run

        /// <summary>Run() returns true and never touches the unit of work when AppSettings:RunReportGeneration is false.</summary>
        [Fact]
        public async Task Run_WhenFlagIsFalse_ReturnsTrueWithoutTouchingUnitOfWork()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:RunReportGeneration", "false"));
            var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
            var service = new ReportGeneratorService(config, unitOfWork.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeTrue();
        }

        /// <summary>Run() returns false when no Initiative summaries exist yet, without attempting to write a report file.</summary>
        [Fact]
        public async Task Run_WhenNoInitiativeSummariesExist_ReturnsFalse()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:RunReportGeneration", "true"));
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Initiatives.GetAllAsync()).ReturnsAsync(new List<Initiative>());
            unitOfWork.Setup(u => u.Epics.GetAllAsync()).ReturnsAsync(new List<Epic>());
            unitOfWork.Setup(u => u.InitiativeBusinessSummaries.GetAllAsync()).ReturnsAsync(new List<InitiativeBusinessSummary>());
            unitOfWork.Setup(u => u.EpicEngineeringSummaries.GetAllAsync()).ReturnsAsync(new List<EpicEngineeringSummary>());
            var service = new ReportGeneratorService(config, unitOfWork.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeFalse();
        }

        /// <summary>Run() catches an exception thrown by a dependency (not just the "no summaries" case), logs it, and returns false.</summary>
        [Fact]
        public async Task Run_WhenUnitOfWorkThrows_ReturnsFalse()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:RunReportGeneration", "true"));
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Initiatives.GetAllAsync()).ThrowsAsync(new InvalidOperationException("boom"));
            var service = new ReportGeneratorService(config, unitOfWork.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeFalse();
        }

        /// <summary>Run(repoRootOverride), given a full Initiative/Epic/summary data set and an isolated temp directory, writes the real HTML file, disables the flag, and returns true.</summary>
        [Fact]
        public async Task Run_WithRepoRootOverrideAndFullData_WritesReportAndReturnsTrue()
        {
            // Arrange
            var root = Directory.CreateTempSubdirectory("HtmlReportGeneratorServiceTests_");
            try
            {
                File.WriteAllText(Path.Combine(root.FullName, "vslhq26-ai-rocketeer.slnx"), "");

                var config = BuildConfig(("AppSettings:RunReportGeneration", "true"));
                var initiative = new Initiative { Id = 1, JiraId = "INIT-1", Title = "Cockpit Avionics", PriorityRank = 1, Status = "In Progress" };
                var epic = new Epic { Id = 10, JiraId = "EPIC-1", Title = "Glass Cockpit Display", InitiativeId = 1 };
                var initiativeSummary = new InitiativeBusinessSummary
                {
                    InitiativeId = 1,
                    SummaryText = "Status: Good.\nKey Progress:\n- Shipped the display firmware",
                    RangeStart = new DateTime(2026, 7, 1),
                    RangeEnd = new DateTime(2026, 7, 31)
                };
                var epicSummary = new EpicEngineeringSummary { EpicId = 10, SummaryText = "Status: Good.\nKey Progress:\n- Built the rendering pipeline" };

                var unitOfWork = new Mock<IUnitOfWork>();
                unitOfWork.Setup(u => u.Initiatives.GetAllAsync()).ReturnsAsync(new List<Initiative> { initiative });
                unitOfWork.Setup(u => u.Epics.GetAllAsync()).ReturnsAsync(new List<Epic> { epic });
                unitOfWork.Setup(u => u.InitiativeBusinessSummaries.GetAllAsync()).ReturnsAsync(new List<InitiativeBusinessSummary> { initiativeSummary });
                unitOfWork.Setup(u => u.EpicEngineeringSummaries.GetAllAsync()).ReturnsAsync(new List<EpicEngineeringSummary> { epicSummary });
                var service = new ReportGeneratorService(config, unitOfWork.Object);

                // Act
                var result = await service.Run(root.FullName);

                // Assert
                result.Should().BeTrue();
                var outputPath = Path.Combine(root.FullName, "report", "rollup-report.html");
                File.Exists(outputPath).Should().BeTrue();
                var html = await File.ReadAllTextAsync(outputPath);
                html.Should().Contain("Cockpit Avionics");
                html.Should().Contain("Shipped the display firmware");
                html.Should().Contain("Built the rendering pipeline");
            }
            finally
            {
                root.Delete(recursive: true);
            }
        }

        #endregion
    }
}
