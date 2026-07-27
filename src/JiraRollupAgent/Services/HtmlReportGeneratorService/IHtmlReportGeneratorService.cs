namespace JiraRollupAgent.Services.HtmlReportGeneratorService
{
    /// <summary>
    /// Generates the single HTML report — Initiatives ordered by priority rank, each showing its
    /// Business Summary with nested Epic Engineering Summaries.
    /// </summary>
    public interface IHtmlReportGeneratorService
    {
        Task<bool> Run();
    }
}
