using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface IPortService
    {
        Task<IEnumerable<Port>> GetAllAsync(CancellationToken cancellationToken);
        Task<Port?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<ServiceResult<int>> CreateAsync(Port model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> UpdateAsync(Port model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> DeleteAsync(int id, string username, CancellationToken cancellationToken);
    }
}
