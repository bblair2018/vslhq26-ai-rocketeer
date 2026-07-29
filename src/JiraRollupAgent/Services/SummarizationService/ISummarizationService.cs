namespace JiraRollupAgent.Services.SummarizationService
{
    /// <summary>
    /// Produces the Azure OpenAI item/Epic-engineering/Initiative-business summaries and persists
    /// them to VSLiveJiraRollup via <see cref="JiraRollupAgent.DAL.Repositories.Interfaces.IUnitOfWork"/>.
    /// </summary>
    public interface ISummarizationService
    {
        /// <summary>Runs the summarization stage, gated by <c>AppSettings:RunSummarization</c>.</summary>
        /// <returns><c>true</c> on success or a skipped run; <c>false</c> if summarization failed.</returns>
        Task<bool> Run();
    }
}
