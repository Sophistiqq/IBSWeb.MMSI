using System.Security.Claims;

namespace IBS.Utility.MSAP.Constants
{
    public static class MsapRoles
    {
        public const string ClaimType = "MSAP.Role";
        public const string Admin = "Admin";
        public const string User = "User";
        public const string SuperAdmin = "SuperAdmin";
        public const string AccessPolicy = "MSAP.Access";
        public const string AdminPolicy = "MSAP.Admin";
        public const string SuperAdminPolicy = "MSAP.SuperAdmin";

        public static string? GetRole(IEnumerable<Claim> claims, bool isSharedAdmin = false)
        {
            string[] roles = claims.Where(claim => claim.Type == ClaimType).Select(claim => claim.Value).ToArray();
            if (isSharedAdmin && (roles.Length == 0 || roles.Length == 1 && IsValid(roles[0])))
            {
                return roles.FirstOrDefault() == SuperAdmin ? SuperAdmin : Admin;
            }
            return roles.Length switch
            {
                0 => isSharedAdmin ? Admin : User,
                1 when IsValid(roles[0]) => roles[0],
                _ => null
            };
        }

        public static bool IsValid(string? role)
        {
            return role is Admin or User or SuperAdmin;
        }
    }
}
