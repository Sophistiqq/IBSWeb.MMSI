using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class TugboatOwnerRepository(MsapDbContext db) : Repository<TugboatOwner>(db), ITugboatOwnerRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<List<SelectListItem>> GetMsapTugboatOwnersSelectList(CancellationToken cancellationToken = default)
        {
            return await _db.MsapTugboatOwners
                .OrderBy(t => t.TugboatOwnerName)
                .Select(t => new SelectListItem
                {
                    Value = t.TugboatOwnerId.ToString(),
                    Text = t.TugboatOwnerName
                })
                .ToListAsync(cancellationToken);
        }
    }
}
