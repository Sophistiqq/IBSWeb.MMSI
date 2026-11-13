using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class MsapAdjustmentLogRepository(MsapDbContext db) : Repository<MsapAdjustmentLog>(db), IMsapAdjustmentLogRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<List<MsapAdjustmentLog>> GetLogsForRecordAsync(string documentType, int recordId, CancellationToken cancellationToken = default)
        {
            return await _db.MsapAdjustmentLogs
                .AsNoTracking()
                .Where(l => l.DocumentType == documentType && l.RecordId == recordId)
                .OrderByDescending(l => l.EditedDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<MsapAdjustmentLog>> GetLogsForPeriodAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            var fromDt = from.ToDateTime(TimeOnly.MinValue);
            var toDt = to.ToDateTime(TimeOnly.MaxValue);

            return await _db.MsapAdjustmentLogs
                .AsNoTracking()
                .Where(l => l.EditedDate >= fromDt && l.EditedDate <= toDt)
                .OrderBy(l => l.EditedDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<MsapAdjustmentLog>> GetFilteredLogsAsync(
            int? year,
            int? month,
            string? documentType,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            CancellationToken cancellationToken = default)
        {
            var query = _db.MsapAdjustmentLogs.AsNoTracking()
                .Where(l =>
                    (l.DocumentType == "Billing"
                        && !l.Reason.StartsWith("BIR invoice series override")
                        && _db.MsapBillings.Any(b => b.MsapBillingId == l.RecordId
                            && (b.PeriodStatus == MsapPeriodStatus.Adjusted || b.PeriodStatus == MsapPeriodStatus.Final)))
                    || (l.DocumentType == "DispatchTicket"
                        && _db.MsapDispatchTickets.Any(t => t.DispatchTicketId == l.RecordId
                            && (t.PeriodStatus == MsapPeriodStatus.Adjusted || t.PeriodStatus == MsapPeriodStatus.Final)))
                    || (l.DocumentType == "Collection"
                        && _db.MsapCollections.Any(c => c.MsapCollectionId == l.RecordId
                            && (c.PeriodStatus == MsapPeriodStatus.Adjusted || c.PeriodStatus == MsapPeriodStatus.Final)))
                    || (l.DocumentType == "OtherTugCharge"
                        && _db.MsapOtherTugCharges.Any(o => o.OtherTugChargeId == l.RecordId
                            && (o.PeriodStatus == MsapPeriodStatus.Adjusted || o.PeriodStatus == MsapPeriodStatus.Final))));

            if (year.HasValue)
            {
                query = query.Where(l => l.EditedDate.Year == year.Value);
            }

            if (month.HasValue)
            {
                query = query.Where(l => l.EditedDate.Month == month.Value);
            }

            if (!string.IsNullOrWhiteSpace(documentType))
            {
                var docType = documentType.Trim();
                query = query.Where(l => l.DocumentType == docType);
            }

            if (dateFrom.HasValue)
            {
                var fromDt = dateFrom.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(l => l.EditedDate >= fromDt);
            }

            if (dateTo.HasValue)
            {
                var toDt = dateTo.Value.ToDateTime(TimeOnly.MaxValue);
                query = query.Where(l => l.EditedDate <= toDt);
            }

            return await query
                .OrderByDescending(l => l.EditedDate)
                .ToListAsync(cancellationToken);
        }
    }
}
