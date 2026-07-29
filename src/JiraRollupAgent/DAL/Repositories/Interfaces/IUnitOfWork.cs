using JiraRollupAgent.DAL.Models;

namespace JiraRollupAgent.DAL.Repositories.Interfaces
{
    /// <summary>
    /// Unit of Work interface to manage transactions and coordinate access to all repositories.
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>Generic access to a repository for any entity type, cached per type for the lifetime of this Unit of Work.</summary>
        /// <typeparam name="T">The entity type to get a repository for.</typeparam>
        IRepository<T> Repository<T>() where T : class;

        /// <summary>Repository for <see cref="Initiative"/> rows.</summary>
        IRepository<Initiative> Initiatives { get; }

        /// <summary>Repository for <see cref="Epic"/> rows.</summary>
        IRepository<Epic> Epics { get; }

        /// <summary>Repository for <see cref="WorkItem"/> rows (Story/Bug/Task/Spike/Subtask/StoryBug).</summary>
        IRepository<WorkItem> WorkItems { get; }

        /// <summary>Repository for <see cref="Comment"/> rows.</summary>
        IRepository<Comment> Comments { get; }

        /// <summary>Repository for <see cref="TeamMember"/> rows.</summary>
        IRepository<TeamMember> TeamMembers { get; }

        /// <summary>Repository for <see cref="WorkItemSummary"/> rows.</summary>
        IRepository<WorkItemSummary> WorkItemSummaries { get; }

        /// <summary>Repository for <see cref="EpicEngineeringSummary"/> rows.</summary>
        IRepository<EpicEngineeringSummary> EpicEngineeringSummaries { get; }

        /// <summary>Repository for <see cref="InitiativeBusinessSummary"/> rows.</summary>
        IRepository<InitiativeBusinessSummary> InitiativeBusinessSummaries { get; }

        /// <summary>
        /// Commit all changes made through repositories to the database.
        /// </summary>
        Task<int> CompleteAsync();

        /// <summary>
        /// Truncate all tables in the database (delete all records).
        /// </summary>
        Task DeleteAllRowsAsync();

        /// <summary>
        /// Truncate just the three summary tables (WorkItemSummaries/EpicEngineeringSummaries/
        /// InitiativeBusinessSummaries) - used by SummarizationService to regenerate fresh on every
        /// run without touching the loaded hierarchy.
        /// </summary>
        Task DeleteAllSummariesAsync();
    }
}
