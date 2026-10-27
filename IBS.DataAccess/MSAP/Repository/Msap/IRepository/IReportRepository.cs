using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IReportRepository
    {
        Task<byte[]?> GetFinalSalesReportAsync(DateOnly from, DateOnly to, CancellationToken ct);
        Task<byte[]> SaveFinalSalesReportAsync(DateOnly from, DateOnly to, byte[] content, DateTime generatedAt, CancellationToken ct);

        Task<List<IBS.Models.MSAP.Books.SalesBook>> GetSalesBookAsync(DateOnly from, DateOnly to, CancellationToken ct);

        Task<List<DispatchTicket>> GetDispatchReportData(DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default, bool filterByBillingDate = false);
    }
}
