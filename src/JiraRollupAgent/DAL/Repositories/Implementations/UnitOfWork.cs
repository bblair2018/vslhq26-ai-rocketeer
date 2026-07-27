using System.Diagnostics.CodeAnalysis;
using JiraRollupAgent.DAL.Context;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace JiraRollupAgent.DAL.Repositories.Implementations
{
    /// <summary>
    /// Unit of Work implementation that manages the repositories and ensures transactional consistency.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class UnitOfWork : IUnitOfWork
    {
        private readonly JiraRollupDBContext _context;

        private IRepository<Initiative>? _initiativeRepository;
        private IRepository<Epic>? _epicRepository;
        private IRepository<WorkItem>? _workItemRepository;
        private IRepository<Comment>? _commentRepository;
        private IRepository<TeamMember>? _teamMemberRepository;

        // Cache for generic Repository<T>() calls to avoid creating a new instance on every invocation.
        // Keyed by entity Type so each type gets at most one repository instance per UnitOfWork lifetime.
        private readonly Dictionary<Type, object> _repositories = new();

        public UnitOfWork(JiraRollupDBContext context)
        {
            _context = context;
        }

        #region Repositories Implementation

        public IRepository<Initiative> Initiatives
            => _initiativeRepository ??= new Repository<Initiative>(_context);

        public IRepository<Epic> Epics
            => _epicRepository ??= new Repository<Epic>(_context);

        public IRepository<WorkItem> WorkItems
            => _workItemRepository ??= new Repository<WorkItem>(_context);

        public IRepository<Comment> Comments
            => _commentRepository ??= new Repository<Comment>(_context);

        public IRepository<TeamMember> TeamMembers
            => _teamMemberRepository ??= new Repository<TeamMember>(_context);

        #endregion

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

        public async Task<int> SaveAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task<int> CompleteAsync()
        {
            return await SaveAsync();
        }

        /// <summary>
        /// Generic method to get a repository for any entity type.
        /// Uses a dictionary cache to ensure only one repository instance is created per entity type,
        /// matching the same lazy-instantiation pattern used by the named repository properties above.
        /// </summary>
        public IRepository<T> Repository<T>() where T : class
        {
            var type = typeof(T);

            if (!_repositories.ContainsKey(type))
                _repositories[type] = new Repository<T>(_context);

            return (IRepository<T>)_repositories[type];
        }

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
