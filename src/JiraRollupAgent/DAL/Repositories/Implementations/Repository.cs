using System.Linq.Expressions;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace JiraRollupAgent.DAL.Repositories.Implementations
{
    /// <summary>
    /// A generic repository class for handling CRUD operations on a given entity type using Entity Framework Core.
    /// </summary>
    /// <typeparam name="T">The entity type this repository handles.</typeparam>
    public class Repository<T> : IRepository<T> where T : class
    {
        /// <summary>The shared EF Core context this repository operates against.</summary>
        protected readonly DbContext _context;

        /// <summary>The <see cref="DbSet{T}"/> for this repository's entity type.</summary>
        protected readonly DbSet<T> _dbSet;

        /// <summary>Creates a repository bound to the given context's <see cref="DbSet{T}"/> for <typeparamref name="T"/>.</summary>
        /// <param name="context">The EF Core context to operate against.</param>
        public Repository(DbContext context)
        {
            _context = context;
            _dbSet = _context.Set<T>();
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        /// <inheritdoc/>
        public async Task<T?> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            return await _dbSet.Where(predicate).ToListAsync();
        }

        /// <inheritdoc/>
        public async Task AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
        }

        /// <inheritdoc/>
        public async Task AddRangeAsync(IEnumerable<T> entities)
        {
            await _dbSet.AddRangeAsync(entities);
        }

        /// <inheritdoc/>
        public void Update(T entity)
        {
            _dbSet.Update(entity);
        }

        /// <inheritdoc/>
        public void Remove(T entity)
        {
            _dbSet.Remove(entity);
        }

        /// <inheritdoc/>
        public void RemoveRange(IEnumerable<T> entities)
        {
            _dbSet.RemoveRange(entities);
        }
    }
}
