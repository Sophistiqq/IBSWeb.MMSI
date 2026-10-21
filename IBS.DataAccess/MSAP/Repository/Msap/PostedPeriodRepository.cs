using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class PostedPeriodRepository(MsapDbContext db) : IPostedPeriodRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<bool> IsMonthClosedAsync(int year, int month, CancellationToken cancellationToken = default)
        {
            if (!MsapConstants.EnforceClosedPeriods)
            {
                return false;
            }
            var period = await _db.MsapPostedPeriods
                .FirstOrDefaultAsync(p => p.Year == year && p.Month == month, cancellationToken);
            return period?.IsClosed ?? false;
        }

        public async Task<IEnumerable<MsapPostedPeriod>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _db.MsapPostedPeriods
                .OrderByDescending(p => p.Year)
                .ThenByDescending(p => p.Month)
                .ToListAsync(cancellationToken);
        }

        public async Task<MsapPostedPeriod?> GetByYearMonthAsync(int year, int month, CancellationToken cancellationToken = default)
        {
            return await _db.MsapPostedPeriods
                .FirstOrDefaultAsync(p => p.Year == year && p.Month == month, cancellationToken);
        }

        public async Task AddAsync(MsapPostedPeriod period, CancellationToken cancellationToken = default)
        {
            await _db.MsapPostedPeriods.AddAsync(period, cancellationToken);
        }

        public Task UpdateAsync(MsapPostedPeriod period, CancellationToken cancellationToken = default)
        {
            _db.MsapPostedPeriods.Update(period);
            return Task.CompletedTask;
        }
    }
}
