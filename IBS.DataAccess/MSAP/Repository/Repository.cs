using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IBS.DataAccess.MSAP.Repository
{
    public class Repository<T> : IRepository<T> where T : class
    {
        private readonly MsapDbContext _db;
        internal DbSet<T> dbSet;

        private const decimal VatRate = 0.12m;

        public Repository(MsapDbContext db)
        {
            _db = db;
            dbSet = _db.Set<T>();
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<T> query = dbSet;
            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public virtual async Task<(IEnumerable<T> Data, int Total)> GetPagedAsync(
            Expression<Func<T, bool>>? filter,
            string? orderBy, string orderDir, int skip, int take,
            CancellationToken cancellationToken = default)
        {
            IQueryable<T> query = dbSet.AsNoTracking();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            var total = await query.CountAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                var prop = typeof(T).GetProperty(orderBy, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    var name = char.ToUpper(orderBy[0]) + orderBy.Substring(1);
                    query = orderDir == "desc"
                        ? query.OrderByDescending(e => EF.Property<object>(e, name))
                        : query.OrderBy(e => EF.Property<object>(e, name));
                }
            }

            var data = await query.Skip(skip).Take(take).ToListAsync(cancellationToken);
            return (data, total);
        }

        public virtual async Task<T?> GetAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            return await dbSet.Where(filter).FirstOrDefaultAsync(cancellationToken);
        }

        public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            await dbSet.AddAsync(entity, cancellationToken);
        }

        public async Task RemoveAsync(T entity, CancellationToken cancellationToken = default)
        {
            dbSet.Remove(entity);
        }

        public decimal ComputeNetOfVat(decimal grossAmount)
        {
            if (grossAmount == 0)
            {
                return grossAmount;
            }

            return grossAmount / (1 + VatRate);
        }

        public decimal ComputeVatAmount(decimal netOfVatAmount)
        {
            return netOfVatAmount * VatRate;
        }

        public decimal ComputeEwtAmount(decimal netOfVatAmount, decimal percent)
        {
            return netOfVatAmount * percent;
        }

        public decimal ComputeNetOfEwt(decimal grossAmount, decimal ewtAmount)
        {
            return grossAmount - ewtAmount;
        }
    }
}
