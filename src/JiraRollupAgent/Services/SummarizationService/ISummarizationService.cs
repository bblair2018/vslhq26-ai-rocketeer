namespace JiraRollupAgent.Services.SummarizationService
{
    /// <summary>
    /// Produces the Azure OpenAI item/Epic-engineering/Initiative-business summaries and persists
    /// them to VSLiveJiraRollup via <see cref="JiraRollupAgent.DAL.Repositories.Interfaces.IUnitOfWork"/>.
    /// </summary>
    public interface ISummarizationService
    {
        Task<bool> Run();
    }
}
