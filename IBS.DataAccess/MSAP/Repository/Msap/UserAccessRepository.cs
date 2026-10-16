using System.Linq.Expressions;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class UserAccessRepository(MsapDbContext db): Repository<UserAccess>(db), IUserAccessRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        public override async Task<IEnumerable<UserAccess>> GetAllAsync(Expression<Func<UserAccess, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<UserAccess> query = dbSet
                .OrderBy(ua => ua.UserName);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<List<string>> GetUserIdsWithAccessAsync(ProcedureEnum procedure, CancellationToken cancellationToken = default)
        {
            var query = _db.MsapUserAccesses.AsNoTracking();

            query = procedure switch
            {
                ProcedureEnum.CreateDispatchTicket => query.Where(u => u.CanCreateDispatchTicket),
                ProcedureEnum.EditDispatchTicket => query.Where(u => u.CanEditDispatchTicket),
                ProcedureEnum.DeleteDispatchTicket => query.Where(u => u.CanDeleteDispatchTicket),
                ProcedureEnum.SetTariff => query.Where(u => u.CanSetTariff),
                ProcedureEnum.ApproveTariff => query.Where(u => u.CanApproveTariff),
                ProcedureEnum.CreateBilling => query.Where(u => u.CanCreateBilling),
                ProcedureEnum.EditBilling => query.Where(u => u.CanEditBilling),
                ProcedureEnum.DeleteBilling => query.Where(u => u.CanDeleteBilling),
                ProcedureEnum.ReverseBilling => query.Where(u => u.CanReverseBilling),
                ProcedureEnum.CreateCollection => query.Where(u => u.CanCreateCollection),
                ProcedureEnum.CreateJobOrder => query.Where(u => u.CanCreateJobOrder),
                ProcedureEnum.EditJobOrder => query.Where(u => u.CanEditJobOrder),
                ProcedureEnum.DeleteJobOrder => query.Where(u => u.CanDeleteJobOrder),
                ProcedureEnum.CloseJobOrder => query.Where(u => u.CanCloseJobOrder),
                ProcedureEnum.ApproveSchedule => query.Where(u => u.CanApproveSchedule),
                ProcedureEnum.ApproveRateUpload => query.Where(u => u.CanApproveRateUpload),
                ProcedureEnum.ApproveFinalBilling => query.Where(u => u.CanApproveFinalBilling),
                ProcedureEnum.AccessTreasury => query.Where(u => u.CanAccessTreasury),
                ProcedureEnum.CreateDisbursement => query.Where(u => u.CanCreateDisbursement),
                ProcedureEnum.ManageMsapImport => query.Where(u => u.CanManageMsapImport),
                ProcedureEnum.ViewInventoryReport => query.Where(u => u.CanViewInventoryReport),
                ProcedureEnum.ViewMaritimeReport => query.Where(u => u.CanViewMaritimeReport),
                _ => query.Where(u => false)
            };

            var userIds = await query.Select(u => u.UserId).ToListAsync(cancellationToken);

            if (procedure is ProcedureEnum.ApproveSchedule or ProcedureEnum.ApproveRateUpload or ProcedureEnum.ApproveFinalBilling)
            {
                return userIds;
            }

            // Also include all Admins
            var admins = await _db.UserRoles
                .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                .Where(x => x.r.Name == "Admin")
                .Select(x => x.ur.UserId)
                .ToListAsync(cancellationToken);

            return userIds.Concat(admins).Distinct().ToList();
        }
    }
}
