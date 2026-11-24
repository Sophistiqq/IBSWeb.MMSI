using System.Security.Claims;

namespace IBS.Utility.MSAP.Constants
{
    public static class MsapRoles
    {
        public const string ClaimType = "MSAP.Role";
        public const string EntryClaimType = "MSAP.Entry";
        public const string Admin = "Admin";
        public const string User = "User";
        public const string IdentityUserRole = "MSAP - User";
        public const string IbsAndMsapUserRole = "IBS & MSAP - User";
        public const string AccessPolicy = "MSAP.Access";
        public const string EntryPolicy = "MSAP.Entry";
        public const string AdminPolicy = "MSAP.Admin";
        public const string IbsAccessPolicy = "IBS.Access";

        public static string? GetRole(IEnumerable<Claim> claims, bool isSharedAdmin = false, bool isMsapUser = false)
        {
            if (isSharedAdmin)
            {
                return Admin;
            }

            return isMsapUser ? User : null;
        }

        public static bool IsValid(string? role)
        {
            return role is Admin or User;
        }
    }
}
