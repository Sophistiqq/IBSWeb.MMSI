using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class RateAccessCheck
    {
        public static async Task RunAsync()
        {
            var access = new UserAccess { UserId = "rate-check", CanManageMaritimeMasterFile = true };
            string? role = MsapRoles.User;
            IRoleService roles = DispatchProxy.Create<IRoleService, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)roles).Handler = (method, args) => Task.FromResult(role);
            IUserAccessRepository permissions = DispatchProxy.Create<IUserAccessRepository, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)permissions).Handler = (method, args) => Task.FromResult<UserAccess?>(access);
            IUnitOfWork work = DispatchProxy.Create<IUnitOfWork, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)work).Handler = (method, args) => permissions;
            var service = new UserAccessService(work, null!, roles, NullLogger<UserAccessService>.Instance);
            async Task CheckAsync(bool view, bool prepare, bool approve)
            {
                if (await service.CheckAccess(access.UserId, ProcedureEnum.ViewRates) != view
                    || await service.CheckAccess(access.UserId, ProcedureEnum.ManageRates) != prepare
                    || await service.CheckAccess(access.UserId, ProcedureEnum.ApproveRateUpload) != approve)
                {
                    throw new InvalidOperationException("Rate permission boundary failed.");
                }
            }
            await CheckAsync(false, false, false);
            access.CanViewRates = true;
            await CheckAsync(true, false, false);
            access.CanViewRates = false;
            access.CanManageRates = true;
            await CheckAsync(true, true, false);
            access.CanManageRates = false;
            access.CanApproveRateUpload = true;
            await CheckAsync(true, false, true);
            access.CanApproveRateUpload = false;
            await CheckAsync(false, false, false);
            foreach (string adminRole in new[] { MsapRoles.Admin, MsapRoles.SuperAdmin })
            {
                role = adminRole;
                await CheckAsync(true, true, true);
            }
            role = null;
            access.CanViewRates = access.CanManageRates = access.CanApproveRateUpload = true;
            await CheckAsync(false, false, false);
            if ((int)ProcedureEnum.ApproveRateUpload != 23 || (int)ProcedureEnum.ApproveFinalBilling != 24)
            {
                throw new InvalidOperationException("Existing procedure IDs changed.");
            }
            Console.WriteLine("PASS: rate view/prepare/approval grants, revocation, master-file isolation, administrator defaults and no-role denial (no database used).");
        }
    }
}
