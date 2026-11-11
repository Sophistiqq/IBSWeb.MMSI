using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IMsapAdjustmentLogRepository : IRepository<MsapAdjustmentLog>
    {
        Task<List<MsapAdjustmentLog>> GetLogsForRecordAsync(string documentType, int recordId, CancellationToken cancellationToken = default);
        Task<List<MsapAdjustmentLog>> GetLogsForPeriodAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    }
}
