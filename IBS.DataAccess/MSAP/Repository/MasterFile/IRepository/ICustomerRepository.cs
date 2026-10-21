using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;

namespace IBS.DataAccess.MSAP.Repository.MasterFile.IRepository
{
    public interface ICustomerRepository : IRepository<Customer>
    {
        Task<bool> IsTinNoExistAsync(string tin, string company, CancellationToken cancellationToken = default);

        Task<string> GenerateCodeAsync(string customerType, CancellationToken cancellationToken = default);

        Task UpdateAsync(Customer model, CancellationToken cancellationToken = default);

        Task<List<Customer>> SearchCustomersAsync(string term, int limit, CancellationToken cancellationToken);

        Task<List<object>> SearchCustomersDtoAsync(string term, int limit, CancellationToken cancellationToken);
    }
}
