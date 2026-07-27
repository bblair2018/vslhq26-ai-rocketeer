using JiraRollupAgent.DAL.Models;

namespace JiraRollupAgent.DAL.Repositories.Interfaces
{
    /// <summary>
    /// Unit of Work interface to manage transactions and coordinate access to all repositories.
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        // Generic access to any repository
        IRepository<T> Repository<T>() where T : class;

        IRepository<Initiative> Initiatives { get; }

        IRepository<Epic> Epics { get; }

        IRepository<WorkItem> WorkItems { get; }

        IRepository<Comment> Comments { get; }

        IRepository<TeamMember> TeamMembers { get; }

        /// <summary>
        /// Commit all changes made through repositories to the database.
        /// </summary>
        Task<int> CompleteAsync();

        /// <summary>
        /// Truncate all tables in the database (delete all records).
        /// </summary>
        Task DeleteAllRowsAsync();
    }
}
