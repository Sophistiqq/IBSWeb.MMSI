using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface ICustomerOrderSlipRepository : IRepository<CustomerOrderSlip>
    {
        Task<IEnumerable<CustomerOrderSlip>> GetAllCustomerOrderSlipsWithDetailsAsync(CancellationToken cancellationToken);
        Task<IEnumerable<CustomerOrderSlip>> GetCustomerOrderSlipsWithDetailsAsync(DateTime start, DateTime end, CancellationToken cancellationToken);
        Task<CustomerOrderSlip?> GetCustomerOrderSlipWithDetailsAsync(int id, CancellationToken cancellationToken);
        Task<CustomerOrderSlip?> GetForUpdateAsync(int id, CancellationToken ct = default);
        Task<string> GenerateCustomerOrderSlipNumber(CancellationToken cancellationToken);
        Task<List<CustomerOrderSlip>> SearchBillableCustomerOrderSlipsAsync(string term, int customerId, int limit, CancellationToken cancellationToken);
        Task<(IEnumerable<CustomerOrderSlip> Data, int RecordsFiltered, int TotalRecords)> GetPagedCustomerOrderSlipsAsync(DataTablesParameters parameters, CancellationToken cancellationToken);
    }
}
