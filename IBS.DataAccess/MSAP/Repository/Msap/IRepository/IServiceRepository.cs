using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IServiceRepository : IRepository<Service>
    {
        Task SaveAsync(CancellationToken cancellationToken);

        Task<List<SelectListItem>> GetMsapActivitiesServicesById(CancellationToken cancellationToken = default);
    }
}
