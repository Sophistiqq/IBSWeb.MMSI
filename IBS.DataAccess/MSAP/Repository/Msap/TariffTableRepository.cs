using System.Linq.Expressions;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class TariffTableRepository(MsapDbContext db): Repository<TariffRate>(db), ITariffTableRepository
    {
        private readonly MsapDbContext _db = db;

        public override async Task<TariffRate?> GetAsync(Expression<Func<TariffRate, bool>> filter, CancellationToken cancellationToken = default)
        {
            var model =  await dbSet
                .Include(t => t.Terminal).ThenInclude(t => t.Port)
                .Where(filter)
                .OrderByDescending(t => t.AsOfDate)
                .FirstOrDefaultAsync(cancellationToken);

            return model;
        }

        public override async Task<IEnumerable<TariffRate>> GetAllAsync(Expression<Func<TariffRate, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<TariffRate> query = dbSet
                .Include(t => t.Customer)
                .Include(t => t.Terminal).ThenInclude(t => t.Port)
                .Include(t => t.Service);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<TariffRate?> GetApplicableRateAsync(int customerId, int terminalId, int serviceId, DateOnly serviceDate, CancellationToken cancellationToken)
        {
            return await dbSet
                .Where(t => t.CustomerId == customerId && t.AsOfDate <= serviceDate)
                .OrderByDescending(t => t.TerminalId == terminalId && t.ServiceId == serviceId)
                .ThenByDescending(t => t.TerminalId == terminalId)
                .ThenByDescending(t => t.ServiceId == serviceId)
                .ThenByDescending(t => t.AsOfDate)
                .ThenByDescending(t => t.TariffRateId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
