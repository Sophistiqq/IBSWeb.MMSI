using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IReportRepository
    {
        Task<List<IBS.Models.MSAP.Books.SalesBook>> GetSalesBookAsync(DateOnly from, DateOnly to, CancellationToken ct);

        Task<List<DispatchTicket>> GetDispatchReportData(DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default, bool filterByBillingDate = false);
    }
}
