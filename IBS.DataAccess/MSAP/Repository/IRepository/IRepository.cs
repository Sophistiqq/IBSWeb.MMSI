using System.Linq.Expressions;

namespace IBS.DataAccess.MSAP.Repository.IRepository
{
    public interface IRepository<T> where T : class
    {
        Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default);

        Task<(IEnumerable<T> Data, int Total)> GetPagedAsync(
            Expression<Func<T, bool>>? filter,
            string? orderBy, string orderDir, int skip, int take,
            CancellationToken cancellationToken = default);

        Task<T?> GetAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default);

        Task AddAsync(T entity, CancellationToken cancellationToken = default);

        Task RemoveAsync(T entity, CancellationToken cancellationToken = default);

        decimal ComputeNetOfVat(decimal grossAmount);

        decimal ComputeVatAmount(decimal netOfVatAmount);
    }
}
