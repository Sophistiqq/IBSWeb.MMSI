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
                new(MsapRoles.User)
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

            var isMsapUser = await _userManager.IsInRoleAsync(user, MsapRoles.IdentityUserRole)
                || await _userManager.IsInRoleAsync(user, MsapRoles.IbsAndMsapUserRole);

            role = MsapRoles.GetRole(
                await _userManager.GetClaimsAsync(user),
                await _userManager.IsInRoleAsync(user, MsapRoles.Admin),
                isMsapUser);
            _userRoles[userId] = role;
            return role;
        }

        public async Task<(IEnumerable<object> Data, int TotalRecords)> GetPagedRolesAsync(
            DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            var roles = await GetAllRolesAsync(cancellationToken);
            var searchValue = parameters?.Search?.Value;
            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                roles = roles.Where(role => role.Name!.Contains(searchValue, StringComparison.OrdinalIgnoreCase));
            }

            var order = parameters?.Order?.FirstOrDefault();
            var columnName = order != null
                && parameters?.Columns != null
                && order.Column >= 0
                && order.Column < parameters.Columns.Count
                ? parameters.Columns[order.Column]?.Data?.ToLowerInvariant()
                : null;
            var isDescending = string.Equals(order?.Dir, "desc", StringComparison.OrdinalIgnoreCase);

            roles = columnName switch
            {
                "name" => isDescending ? roles.OrderByDescending(role => role.Name) : roles.OrderBy(role => role.Name),
                _ => isDescending ? roles.OrderByDescending(role => role.Name) : roles.OrderBy(role => role.Name)
            };

            var totalRecords = roles.Count();
            var start = Math.Max(0, parameters?.Start ?? 0);
            var length = parameters?.Length >= 0 ? parameters.Length : 10;
            var data = roles
                .Skip(start)
                .Take(length)
                .Select(role => (object)new { role.Name })
                .ToList();
            return (data, totalRecords);
        }
    }
}
