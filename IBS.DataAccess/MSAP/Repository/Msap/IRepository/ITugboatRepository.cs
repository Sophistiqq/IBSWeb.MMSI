using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface ITugboatRepository : IRepository<Tugboat>
    {
        Task SaveAsync(CancellationToken cancellationToken);

        Task<List<SelectListItem>> GetMsapTugboatsById(CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetMsapCompanyOwnerSelectListById(CancellationToken cancellationToken = default);

        Task<IEnumerable<Tugboat>> GetTugboatsWithOwnersAsync(CancellationToken cancellationToken = default);
    }
}
