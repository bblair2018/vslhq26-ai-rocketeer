using JiraRollupAgent.DAL.Context;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace JiraRollupAgent.DAL.Repositories.Implementations
{
    /// <summary>
    /// Unit of Work implementation that manages the repositories and ensures transactional consistency.
    /// </summary>
    public class UnitOfWork : IUnitOfWork
    {
        /// <summary>The shared EF Core context all repositories in this Unit of Work operate against.</summary>
        private readonly JiraRollupDBContext _context;

        private IRepository<Initiative>? _initiativeRepository;
        private IRepository<Epic>? _epicRepository;
        private IRepository<WorkItem>? _workItemRepository;
        private IRepository<Comment>? _commentRepository;
        private IRepository<TeamMember>? _teamMemberRepository;
        private IRepository<WorkItemSummary>? _workItemSummaryRepository;
        private IRepository<EpicEngineeringSummary>? _epicEngineeringSummaryRepository;
        private IRepository<InitiativeBusinessSummary>? _initiativeBusinessSummaryRepository;

        // Cache for generic Repository<T>() calls to avoid creating a new instance on every invocation.
        // Keyed by entity Type so each type gets at most one repository instance per UnitOfWork lifetime.
        private readonly Dictionary<Type, object> _repositories = new();

        /// <summary>Creates a Unit of Work bound to the given EF Core context.</summary>
        /// <param name="context">The context all repositories will share.</param>
        public UnitOfWork(JiraRollupDBContext context)
        {
            _context = context;
        }

        #region Repositories Implementation

        /// <inheritdoc/>
        public IRepository<Initiative> Initiatives
            => _initiativeRepository ??= new Repository<Initiative>(_context);

        /// <inheritdoc/>
        public IRepository<Epic> Epics
            => _epicRepository ??= new Repository<Epic>(_context);

        /// <inheritdoc/>
        public IRepository<WorkItem> WorkItems
            => _workItemRepository ??= new Repository<WorkItem>(_context);

        /// <inheritdoc/>
        public IRepository<Comment> Comments
            => _commentRepository ??= new Repository<Comment>(_context);

        /// <inheritdoc/>
        public IRepository<TeamMember> TeamMembers
            => _teamMemberRepository ??= new Repository<TeamMember>(_context);

        /// <inheritdoc/>
        public IRepository<WorkItemSummary> WorkItemSummaries
            => _workItemSummaryRepository ??= new Repository<WorkItemSummary>(_context);

        /// <inheritdoc/>
        public IRepository<EpicEngineeringSummary> EpicEngineeringSummaries
            => _epicEngineeringSummaryRepository ??= new Repository<EpicEngineeringSummary>(_context);

        /// <inheritdoc/>
        public IRepository<InitiativeBusinessSummary> InitiativeBusinessSummaries
            => _initiativeBusinessSummaryRepository ??= new Repository<InitiativeBusinessSummary>(_context);

        #endregion

        /// <inheritdoc/>
        public async Task DeleteAllRowsAsync()
        {
            var tables = new List<string>
            {
                "Comments",
                "WorkItems",
                "TeamMembers",
                "Epics",
                "Initiatives"
            };

            foreach (var table in tables)
            {
                var command = $"DELETE FROM {table}";
                await _context.Database.ExecuteSqlRawAsync(command);
            }
        }

        /// <inheritdoc/>
        public async Task DeleteAllSummariesAsync()
        {
            var tables = new List<string>
            {
                "WorkItemSummaries",
                "EpicEngineeringSummaries",
                "InitiativeBusinessSummaries"
            };

            foreach (var table in tables)
            {
                var command = $"DELETE FROM {table}";
                await _context.Database.ExecuteSqlRawAsync(command);
            }
        }

        /// <summary>Persists all pending changes tracked by the context. Called by <see cref="CompleteAsync"/>.</summary>
        /// <returns>The number of state entries written to the database.</returns>
        public async Task<int> SaveAsync()
        {
            return await _context.SaveChangesAsync();
        }

        /// <inheritdoc/>
        public async Task<int> CompleteAsync()
        {
            return await SaveAsync();
        }

        /// <summary>
        /// Generic method to get a repository for any entity type.
        /// Uses a dictionary cache to ensure only one repository instance is created per entity type,
        /// matching the same lazy-instantiation pattern used by the named repository properties above.
        /// </summary>
        /// <typeparam name="T">The entity type to get a repository for.</typeparam>
        /// <returns>A repository for <typeparamref name="T"/>, cached for the lifetime of this Unit of Work.</returns>
        public IRepository<T> Repository<T>() where T : class
        {
            var type = typeof(T);

            if (!_repositories.ContainsKey(type))
                _repositories[type] = new Repository<T>(_context);

            return (IRepository<T>)_repositories[type];
        }

        /// <summary>Disposes the underlying EF Core context.</summary>
        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
