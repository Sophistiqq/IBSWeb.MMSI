using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class TugboatRepository(MsapDbContext db): Repository<Tugboat>(db), ITugboatRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapTugboatsById(CancellationToken cancellationToken = default)
        {
            var tugBoats = await _db.MsapTugboats.OrderBy(s => s.TugboatNumber).Select(s => new SelectListItem
            {
                Value = s.TugboatId.ToString(),
                Text = s.TugboatNumber + " " + s.TugboatName
            }).ToListAsync(cancellationToken);

            return tugBoats;
        }

        public async Task<List<SelectListItem>> GetMsapCompanyOwnerSelectListById(CancellationToken cancellationToken = default)
        {
            var companyOwnerList = await _db.MsapTugboatOwners
                .OrderBy(dt => dt.TugboatOwnerNumber).Select(s => new SelectListItem
                {
                    Value = s.TugboatOwnerId.ToString(),
                    Text = $"{s.TugboatOwnerNumber} {s.TugboatOwnerName}"
                }).ToListAsync(cancellationToken);

            return companyOwnerList;
        }

        public async Task<IEnumerable<Tugboat>> GetTugboatsWithOwnersAsync(CancellationToken cancellationToken = default)
        {
            return await _db.MsapTugboats
                .Include(t => t.TugboatOwner)
                .Include(t => t.Port)
                .ToListAsync(cancellationToken);
        }
    }
}
