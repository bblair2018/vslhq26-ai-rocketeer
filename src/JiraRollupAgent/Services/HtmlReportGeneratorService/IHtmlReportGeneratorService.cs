namespace JiraRollupAgent.Services.HtmlReportGeneratorService
{
    /// <summary>
    /// Generates the single HTML report — Initiatives ordered by priority rank, each showing its
    /// Business Summary with nested Epic Engineering Summaries.
    /// </summary>
    public interface IHtmlReportGeneratorService
    {
        /// <summary>Runs the report generation stage, gated by <c>AppSettings:RunReportGeneration</c>.</summary>
        /// <returns><c>true</c> on success or a skipped run; <c>false</c> if report generation failed.</returns>
        Task<bool> Run();
    }
}
