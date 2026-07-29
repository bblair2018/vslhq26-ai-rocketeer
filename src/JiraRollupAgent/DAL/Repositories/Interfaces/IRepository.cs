using System.Linq.Expressions;

namespace JiraRollupAgent.DAL.Repositories.Interfaces
{
    /// <summary>
    /// A generic repository interface for handling CRUD operations on a given entity type.
    /// </summary>
    /// <typeparam name="T">The entity type this repository handles.</typeparam>
    public interface IRepository<T> where T : class
    {
        /// <summary>Retrieves every row for this entity type.</summary>
        Task<IEnumerable<T>> GetAllAsync();

        /// <summary>Retrieves a single entity by its primary key, or <c>null</c> if not found.</summary>
        /// <param name="id">The primary key value to look up.</param>
        Task<T?> GetByIdAsync(int id);

        /// <summary>Retrieves every entity matching the given predicate.</summary>
        /// <param name="predicate">The filter condition to apply.</param>
        Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);

        /// <summary>Marks a single entity as added; not persisted until <see cref="IUnitOfWork.CompleteAsync"/> is called.</summary>
        /// <param name="entity">The entity to add.</param>
        Task AddAsync(T entity);

        /// <summary>Marks a batch of entities as added; not persisted until <see cref="IUnitOfWork.CompleteAsync"/> is called.</summary>
        /// <param name="entities">The entities to add.</param>
        Task AddRangeAsync(IEnumerable<T> entities);

        /// <summary>Marks an existing entity as modified.</summary>
        /// <param name="entity">The entity to update.</param>
        void Update(T entity);

        /// <summary>Marks a single entity as removed.</summary>
        /// <param name="entity">The entity to remove.</param>
        void Remove(T entity);

        /// <summary>Marks a batch of entities as removed.</summary>
        /// <param name="entities">The entities to remove.</param>
        void RemoveRange(IEnumerable<T> entities);
    }
}
