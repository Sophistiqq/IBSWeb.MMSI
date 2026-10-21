using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface IPrincipalService
    {
        Task<IEnumerable<Principal>> GetAllAsync(CancellationToken cancellationToken);
        Task<Principal?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<Principal> PopulateSelectListsAsync(Principal? model, CancellationToken cancellationToken);
        Task<ServiceResult<int>> CreateAsync(Principal model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> UpdateAsync(Principal model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> DeleteAsync(int id, string username, CancellationToken cancellationToken);
    }
}
