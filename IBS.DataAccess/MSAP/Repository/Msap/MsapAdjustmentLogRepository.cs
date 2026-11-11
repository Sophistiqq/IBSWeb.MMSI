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
    }
}
