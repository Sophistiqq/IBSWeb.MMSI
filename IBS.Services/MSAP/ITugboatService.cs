using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface ITugboatService
    {
        Task<IEnumerable<Tugboat>> GetAllAsync(CancellationToken cancellationToken);
        Task<Tugboat?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<Tugboat> PopulateSelectListsAsync(Tugboat? model, CancellationToken cancellationToken);
        Task<ServiceResult<int>> CreateAsync(Tugboat model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> UpdateAsync(Tugboat model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> DeleteAsync(int id, string username, CancellationToken cancellationToken);
    }
}
