using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IPrincipalRepository : IRepository<Principal>
    {
        Task SaveAsync(CancellationToken cancellationToken);

        Task<List<Principal>> SearchPrincipalsAsync(string term, int customerId, int limit, CancellationToken cancellationToken);
    }
}
