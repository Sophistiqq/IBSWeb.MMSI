using System.Linq.Expressions;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class UserAccessRepository(MsapDbContext db, ApplicationDbContext sharedDb): Repository<UserAccess>(db), IUserAccessRepository
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

            List<UserAccess> records = await query.ToListAsync(cancellationToken);
            List<string> ids = records.Select(record => record.UserId).Distinct().ToList();
            Dictionary<string, string> names = await _db.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);
            foreach (UserAccess record in records)
            {
                record.DisplayName = names.TryGetValue(record.UserId, out string? name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : "Name unavailable";
            }
            return records;
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
                ProcedureEnum.CreateCustomerOrderSlip => query.Where(u => u.CanCreateCustomerOrderSlip),
                ProcedureEnum.EditCustomerOrderSlip => query.Where(u => u.CanEditCustomerOrderSlip),
                ProcedureEnum.DeleteCustomerOrderSlip => query.Where(u => u.CanDeleteCustomerOrderSlip),
                ProcedureEnum.CloseCustomerOrderSlip => query.Where(u => u.CanCloseCustomerOrderSlip),
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
                var activeUsers = await _db.Users.AsNoTracking().Where(user => user.IsActive)
                    .Select(user => user.Id).ToListAsync(cancellationToken);
                var claims = (await sharedDb.UserClaims.AsNoTracking().Where(claim => claim.ClaimType == MsapRoles.ClaimType)
                    .ToListAsync(cancellationToken)).ToLookup(claim => claim.UserId);
                var sharedAdmins = (await _db.UserRoles
                    .Join(_db.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.Name })
                    .Where(role => role.Name == MsapRoles.Admin).Select(role => role.UserId).ToListAsync(cancellationToken)).ToHashSet();
                var assignedUsers = userIds.ToHashSet();
                return activeUsers.Where(id =>
                {
                    string? role = MsapRoles.GetRole(claims[id].Select(claim => claim.ToClaim()), sharedAdmins.Contains(id));
                    return role is MsapRoles.Admin or MsapRoles.SuperAdmin || role == MsapRoles.User && assignedUsers.Contains(id);
                }).ToList();
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
