using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IMsapRepository
    {
        Task<List<SelectListItem>> GetMsapUsersSelectListById(CancellationToken cancellationToken = default);
    }
}
