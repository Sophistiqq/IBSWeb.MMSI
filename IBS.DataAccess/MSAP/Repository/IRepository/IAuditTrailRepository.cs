using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.IRepository
{
    public interface IAuditTrailRepository : IRepository<AuditTrail>
    {
        Task<(IEnumerable<AuditTrail> Data, int RecordsFiltered, int TotalRecords)> GetPagedAuditTrailsAsync(DataTablesParameters parameters, CancellationToken cancellationToken);
    }
}
