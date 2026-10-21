using ApplicationUser = IBS.Models.ApplicationUser;
using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Identity;

namespace IBS.Services.MSAP
{
    public class RoleService : IRoleService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly Dictionary<string, string?> _userRoles = [];

        public RoleService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public Task<IEnumerable<IdentityRole>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            IEnumerable<IdentityRole> roles =
            [
                new(MsapRoles.Admin),
                new(MsapRoles.User),
                new(MsapRoles.SuperAdmin)
            ];
            return Task.FromResult(roles);
        }

        public async Task<string?> GetUserRoleAsync(string userId)
        {
            if (_userRoles.TryGetValue(userId, out string? role))
            {
                return role;
            }

            ApplicationUser? user = await _userManager.FindByIdAsync(userId);
            if (user is null || !user.IsActive)
            {
                _userRoles[userId] = null;
                return null;
            }

            role = MsapRoles.GetRole(await _userManager.GetClaimsAsync(user),
                await _userManager.IsInRoleAsync(user, MsapRoles.Admin));
            _userRoles[userId] = role;
            return role;
        }

        public async Task<(IEnumerable<object> Data, int TotalRecords)> GetPagedRolesAsync(
            DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            IEnumerable<IdentityRole> roles = await GetAllRolesAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(parameters.Search.Value))
            {
                roles = roles.Where(role => role.Name!.Contains(parameters.Search.Value, StringComparison.OrdinalIgnoreCase));
            }

            roles = parameters.Order?.FirstOrDefault()?.Dir == "desc"
                ? roles.OrderByDescending(role => role.Name)
                : roles.OrderBy(role => role.Name);
            int totalRecords = roles.Count();
            IEnumerable<object> data = roles.Skip(parameters.Start).Take(parameters.Length)
                .Select(role => new { role.Name }).ToList();
            return (data, totalRecords);
        }
    }
}
