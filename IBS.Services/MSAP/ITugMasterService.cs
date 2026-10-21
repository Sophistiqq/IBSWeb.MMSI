using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface ITugMasterService
    {
        Task<IEnumerable<TugMaster>> GetAllAsync(CancellationToken cancellationToken);
        Task<TugMaster?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<ServiceResult<int>> CreateAsync(TugMaster model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> UpdateAsync(TugMaster model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> DeleteAsync(int id, string username, CancellationToken cancellationToken);
    }
}
