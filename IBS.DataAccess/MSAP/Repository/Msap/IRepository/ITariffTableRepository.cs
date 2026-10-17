using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface ITariffTableRepository : IRepository<TariffRate>
    {
        Task<TariffRate?> GetApplicableRateAsync(int customerId, int terminalId, int serviceId, DateOnly serviceDate, CancellationToken cancellationToken);

        Task<List<TariffRate>> GetRatesForExportAsync(DateOnly? dateFrom, DateOnly dateTo, CancellationToken cancellationToken);

        Task SaveAsync(CancellationToken cancellationToken);
    }
}
