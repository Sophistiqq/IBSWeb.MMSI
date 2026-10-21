using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface ITugboatOwnerRepository : IRepository<TugboatOwner>
    {
        Task<List<SelectListItem>> GetMsapTugboatOwnersSelectList(CancellationToken cancellationToken = default);
    }
}
