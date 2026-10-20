using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using IBS.DataAccess.MSAP.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.Services
{
    public class TugboatWorkspaceService
    {
        private readonly MsapDbContext _db;
        private readonly IDataProtector _protector;

        public TugboatWorkspaceService(MsapDbContext db, IDataProtectionProvider protection)
        {
            _db = db;
            _protector = protection.CreateProtector("IBS.Workspace.v1");
        }

        public async Task<List<SelectListItem>> GetOptionsAsync(CancellationToken ct)
        {
            return await _db.MsapTugboats.AsNoTracking().Where(t => t.IsCompanyOwned)
                .OrderBy(t => t.TugboatName)
                .Select(t => new SelectListItem(t.TugboatName, t.TugboatId.ToString())).ToListAsync(ct);
        }

        public async Task<string?> GetNameAsync(int? tugboatId, CancellationToken ct)
        {
            if (!tugboatId.HasValue)
            {
                return "MMSI / All tugboats";
            }

            return await _db.MsapTugboats.AsNoTracking()
                .Where(t => t.TugboatId == tugboatId && t.IsCompanyOwned)
                .Select(t => t.TugboatName).SingleOrDefaultAsync(ct);
        }

        public string Protect(ClaimsPrincipal user, int? tugboatId)
        {
            string userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("Sign in before selecting a workspace.");
            return _protector.Protect($"{userId}\n{tugboatId?.ToString(CultureInfo.InvariantCulture) ?? "all"}");
        }

        public bool TryRead(ClaimsPrincipal user, string token, out int? tugboatId)
        {
            tugboatId = null;
            try
            {
                string[] values = _protector.Unprotect(token).Split('\n');
                string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null || values.Length != 2 || values[0] != userId)
                {
                    return false;
                }

                if (values[1] == "all")
                {
                    return true;
                }

                if (int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0)
                {
                    tugboatId = id;
                    return true;
                }
            }
            catch (CryptographicException)
            {
                return false;
            }

            return false;
        }
    }
}
