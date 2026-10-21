using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;

namespace IBS.DataAccess.MSAP.Repository.MasterFile.IRepository
{
    public interface ICompanyRepository : IRepository<Company>
    {
        Task<bool> IsCompanyExistAsync(string companyName, CancellationToken cancellationToken = default);

        Task<bool> IsTinNoExistAsync(string tinNo, CancellationToken cancellationToken = default);

        Task UpdateAsync(Company model, CancellationToken cancellationToken = default);

        Task<string> GenerateCodeAsync(CancellationToken cancellationToken = default);
    }
}
