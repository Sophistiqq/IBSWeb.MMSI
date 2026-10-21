using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IVesselRepository : IRepository<Vessel>
    {
        Task SaveAsync(CancellationToken cancellationToken);

        Task<List<SelectListItem>> GetMsapVesselsSelectList(CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetMsapVesselsById(CancellationToken cancellationToken = default);
    }
}
