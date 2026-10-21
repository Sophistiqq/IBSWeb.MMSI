using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class MsapRepository(MsapDbContext db): IMsapRepository
    {
        public async Task<List<SelectListItem>> GetMsapUsersSelectListById(CancellationToken cancellationToken = default)
        {
            var existingUserIds = await db.MsapUserAccesses
                .Select(ua => ua.UserId)
                .ToListAsync(cancellationToken);

            var list = await db.Users
                .Where(u => !existingUserIds.Contains(u.Id))
                .OrderBy(dt => dt.Name).Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name ?? "Name unavailable"
                }).ToListAsync(cancellationToken);

            return list;
        }
    }
}
