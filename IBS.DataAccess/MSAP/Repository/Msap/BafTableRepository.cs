using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class BafTableRepository : Repository<BafRate>, IBafTableRepository
    {
        public BafTableRepository(MsapDbContext db) : base(db)
        {
        }

        public async Task<BafRate?> GetApplicableRateAsync(int customerId, int terminalId, int serviceId, DateOnly serviceDate, CancellationToken ct)
        {
            return await dbSet.AsNoTracking()
                .Where(rate => rate.CustomerId == customerId && rate.ApprovedAt != null && rate.AsOfDate <= serviceDate)
                .OrderByDescending(rate => rate.TerminalId == terminalId && rate.ServiceId == serviceId)
                .ThenByDescending(rate => rate.TerminalId == terminalId)
                .ThenByDescending(rate => rate.ServiceId == serviceId)
                .ThenByDescending(rate => rate.AsOfDate).ThenByDescending(rate => rate.BafRateId)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<List<BafRate>> GetRatesForExportAsync(DateOnly? dateFrom, DateOnly dateTo, CancellationToken ct)
        {
            IQueryable<BafRate> query = dbSet.AsNoTracking().Where(rate => rate.Customer.Company == MsapConstants.Company_MMSI && rate.AsOfDate <= dateTo);
            if (dateFrom.HasValue)
            {
                query = query.Where(rate => rate.AsOfDate >= dateFrom.Value);
            }
            else
            {
                query = query.Where(rate => !dbSet.Any(newer => newer.CustomerId == rate.CustomerId
                    && newer.TerminalId == rate.TerminalId && newer.ServiceId == rate.ServiceId && newer.AsOfDate <= dateTo
                    && (newer.AsOfDate > rate.AsOfDate || newer.AsOfDate == rate.AsOfDate && newer.BafRateId > rate.BafRateId)));
            }
            return await query.Include(rate => rate.Customer).Include(rate => rate.Port).Include(rate => rate.Terminal).Include(rate => rate.Service)
                .OrderBy(rate => rate.Customer.CustomerCode).ThenBy(rate => rate.Terminal.TerminalNumber)
                .ThenBy(rate => rate.Service.ServiceNumber).ThenBy(rate => rate.AsOfDate).ThenBy(rate => rate.BafRateId).ToListAsync(ct);
        }
    }
}
