using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IBafTableRepository : IRepository<BafRate>
    {
        Task<BafRate?> GetApplicableRateAsync(int customerId, int terminalId, int serviceId, DateOnly serviceDate, CancellationToken ct);
        Task<List<BafRate>> GetRatesForExportAsync(DateOnly? dateFrom, DateOnly dateTo, CancellationToken ct);
    }
}
