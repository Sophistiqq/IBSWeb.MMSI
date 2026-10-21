using IBS.Models.MSAP;
using Microsoft.AspNetCore.Identity;

namespace IBS.Services.MSAP
{
    public interface IRoleService
    {
        Task<IEnumerable<IdentityRole>> GetAllRolesAsync(CancellationToken cancellationToken);
        Task<string?> GetUserRoleAsync(string userId);
        Task<(IEnumerable<object> Data, int TotalRecords)> GetPagedRolesAsync(DataTablesParameters parameters, CancellationToken cancellationToken);
    }
}
