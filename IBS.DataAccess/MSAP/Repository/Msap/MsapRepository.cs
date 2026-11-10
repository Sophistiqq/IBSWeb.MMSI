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
            return await db.Users.AsNoTracking()
                .Where(u => !db.MsapUserAccesses.Any(ua => ua.UserId == u.Id))
                .OrderBy(dt => dt.Name)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name ?? "Name unavailable"
                }).ToListAsync(cancellationToken);
        }
    }
}
