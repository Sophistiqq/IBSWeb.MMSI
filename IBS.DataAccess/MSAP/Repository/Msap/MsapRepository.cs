using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class MsapRepository(MsapDbContext db): IMsapRepository
    {
        public async Task<List<SelectListItem>> GetMsapUsersSelectListById(CancellationToken cancellationToken = default)
        {
            return await db.Users.AsNoTracking()
                .Where(u => u.IsActive
                    && !db.MsapUserAccesses.Any(ua => ua.UserId == u.Id)
                    && db.UserRoles.Any(ur => ur.UserId == u.Id
                        && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == MsapRoles.IdentityUserRole)))
                .OrderBy(dt => dt.Name)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name ?? "Name unavailable"
                }).ToListAsync(cancellationToken);
        }
    }
}
