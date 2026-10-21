using IBS.Models.MSAP;

namespace IBS.Services.MSAP
{
    public interface IAuditTrailService
    {
        Task<IEnumerable<AuditTrail>> GetAuditTrailsByEntityAsync(string documentType, int recordId, CancellationToken cancellationToken);

        Task<IEnumerable<AuditTrail>> GetCustomerOrderSlipTimelineAsync(int customerOrderSlipId, CancellationToken cancellationToken);

        Task<(IEnumerable<AuditTrail> Data, int RecordsFiltered, int TotalRecords)> GetPagedAuditTrailsAsync(DataTablesParameters parameters, CancellationToken cancellationToken);
    }
}
