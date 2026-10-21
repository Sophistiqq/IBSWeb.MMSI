using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.Enums;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface IUserAccessService
    {
        Task<bool> CheckAccess(string id, ProcedureEnum procedure, CancellationToken cancellationToken = default);
        Task<IEnumerable<UserAccess>> GetAllAsync(CancellationToken cancellationToken);
        Task<UserAccess?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<UserAccess> PopulateUsersAsync(UserAccess? model, CancellationToken cancellationToken);
        Task<ServiceResult> CreateAsync(UserAccess model, string username, CancellationToken cancellationToken);
        Task<ServiceResult> UpdateAsync(UserAccess model, string username, CancellationToken cancellationToken);
    }
}
