using System.Linq.Expressions;

namespace JiraRollupAgent.DAL.Repositories.Interfaces
{
    /// <summary>
    /// A generic repository interface for handling CRUD operations on a given entity type.
    /// </summary>
    /// <typeparam name="T">The entity type this repository handles.</typeparam>
    public interface IRepository<T> where T : class
    {
        Task<IEnumerable<T>> GetAllAsync();

        Task<T?> GetByIdAsync(int id);

        Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);

        Task AddAsync(T entity);

        Task AddRangeAsync(IEnumerable<T> entities);

        void Update(T entity);

        void Remove(T entity);

        void RemoveRange(IEnumerable<T> entities);
    }
}
