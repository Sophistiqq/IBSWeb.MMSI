using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface IVesselService
    {
        Task<IEnumerable<Vessel>> GetAllAsync(CancellationToken cancellationToken);
        Task<Vessel?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<ServiceResult<int>> CreateAsync(Vessel model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> UpdateAsync(Vessel model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> DeleteAsync(int id, string username, CancellationToken cancellationToken);
    }
}
