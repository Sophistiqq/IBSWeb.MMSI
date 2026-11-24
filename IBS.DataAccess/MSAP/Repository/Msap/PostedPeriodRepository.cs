using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
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
            if (!MsapConstants.EnforceClosedPeriods || _db == null)
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

        public async Task FinalizePeriodRecordsAsync(int year, int month, CancellationToken cancellationToken = default)
        {
            var start = new DateOnly(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            // 1. Dispatch Tickets recognized in this period
            await _db.MsapDispatchTickets
                .Where(t => t.PeriodStatus == null && (t.DateLeft ?? t.Date) >= start && (t.DateLeft ?? t.Date) <= end)
                .Where(t => t.Status == MsapConstants.DispatchTicketStatus.ForBilling || t.Status == MsapConstants.DispatchTicketStatus.Billed)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.PeriodStatus, MsapPeriodStatus.Final), cancellationToken);

            // 2. Billings recognized in this period
            await _db.MsapBillings
                .Where(b => b.PeriodStatus == null && b.Date >= start && b.Date <= end)
                .Where(b => b.Status == MsapConstants.BillingStatus.ForCollection || b.Status == MsapConstants.BillingStatus.Collected)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.PeriodStatus, MsapPeriodStatus.Final), cancellationToken);

            // 3. Other Tug Charges confirmed in this period
            var startDateTime = new DateTime(year, month, 1, 0, 0, 0);
            var endDateTime = startDateTime.AddMonths(1);
            await _db.MsapOtherTugCharges
                .Where(c => c.PeriodStatus == null && c.ConfirmedAt.HasValue && c.ConfirmedAt.Value >= startDateTime && c.ConfirmedAt.Value < endDateTime)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PeriodStatus, MsapPeriodStatus.Final), cancellationToken);

            // 4. Collections recorded in this period
            await _db.MsapCollections
                .Where(c => c.PeriodStatus == null && c.VoidedDate == null && c.CanceledDate == null && c.Date >= start && c.Date <= end)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PeriodStatus, MsapPeriodStatus.Final), cancellationToken);
        }
    }
}
