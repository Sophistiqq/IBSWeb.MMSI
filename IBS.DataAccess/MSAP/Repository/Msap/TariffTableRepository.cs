using System.Linq.Expressions;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
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

        public async Task<List<TariffRate>> GetRatesForExportAsync(DateOnly? dateFrom, DateOnly dateTo, CancellationToken cancellationToken)
        {
            IQueryable<TariffRate> query = dbSet.AsNoTracking()
                .Where(rate => rate.Customer.Company == MsapConstants.Company_MMSI && rate.AsOfDate <= dateTo);
            if (dateFrom.HasValue)
            {
                query = query.Where(rate => rate.AsOfDate >= dateFrom.Value);
            }
            else
            {
                query = query.Where(rate => !dbSet.Any(newer => newer.CustomerId == rate.CustomerId
                    && newer.TerminalId == rate.TerminalId && newer.ServiceId == rate.ServiceId
                    && newer.AsOfDate <= dateTo
                    && (newer.AsOfDate > rate.AsOfDate || newer.AsOfDate == rate.AsOfDate && newer.TariffRateId > rate.TariffRateId)));
            }
            return await query.Include(rate => rate.Customer).Include(rate => rate.Port)
                .Include(rate => rate.Terminal).Include(rate => rate.Service)
                .OrderBy(rate => rate.Customer.CustomerCode).ThenBy(rate => rate.Terminal.TerminalNumber)
                .ThenBy(rate => rate.Service.ServiceNumber).ThenBy(rate => rate.AsOfDate).ThenBy(rate => rate.TariffRateId)
                .ToListAsync(cancellationToken);
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
