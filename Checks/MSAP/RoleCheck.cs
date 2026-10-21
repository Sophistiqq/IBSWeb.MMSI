using System.Security.Claims;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class RoleCheck
    {
        public static async Task RunAsync(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IUnitOfWork work)
        {
            ApplicationUser user = (await users.FindByIdAsync("shared-check"))!;
            Check((await users.UpdateAsync(user)).Succeeded, "Cannot normalize the shared test account.");
            Check((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded, "Cannot create the Filpride test role.");
            var unrelatedAdmin = new ApplicationUser
            {
                Id = "approval-admin-check",
                UserName = "approval-admin-check",
                Name = "Unassigned Administrator",
                Department = "MIS"
            };
            Check((await users.CreateAsync(unrelatedAdmin)).Succeeded, "Cannot create the unassigned administrator.");
            Check((await users.AddToRoleAsync(unrelatedAdmin, "Admin")).Succeeded, "Cannot assign the unrelated administrator role.");
            await CheckApprovalsAsync(users, work, unrelatedAdmin.Id, false, scheduleAdmin: true);
            Check((await users.AddToRoleAsync(user, "Admin")).Succeeded, "Cannot assign the Filpride test role.");
            Check((await users.AddClaimAsync(user, new Claim("Company", "Filpride"))).Succeeded, "Cannot prepare the shared company claim.");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Role, "Admin")], "check"));
            var permission = new UserAccess { UserId = user.Id, UserName = user.UserName, CanCreateCustomerOrderSlip = true };
            await work.UserAccess.AddAsync(permission);
            await work.SaveAsync();

            Check((int)ProcedureEnum.CreateDispatchTicket == 0 && (int)ProcedureEnum.ViewMaritimeReport == 21
                && (int)ProcedureEnum.ApproveSchedule == 22 && (int)ProcedureEnum.ApproveRateUpload == 23
                && (int)ProcedureEnum.ApproveFinalBilling == 24, "Procedure values shifted.");
            await CheckApprovalsAsync(users, work, user.Id, false, scheduleAdmin: true);

            Check(await HasRoleAsync(users, work, principal, MsapRoles.Admin), "Filpride Admin did not default to MSAP Admin.");
            Check(!await HasRoleAsync(users, work, principal, MsapRoles.SuperAdmin), "Filpride Admin inherited MSAP SuperAdmin.");
            Check(await HasProcedureAsync(users, work, user.Id, ProcedureEnum.CreateBilling), "Inherited MSAP Admin lacks procedure access.");
            Check((await users.RemoveFromRoleAsync(user, "Admin")).Succeeded, "Cannot prepare ordinary shared user check.");
            Check(await HasRoleAsync(users, work, principal, MsapRoles.User)
                && !await HasRoleAsync(users, work, principal, MsapRoles.Admin), "Stored role changes were ignored in favor of cookie roles.");
            Check(await HasProcedureAsync(users, work, user.Id, ProcedureEnum.CreateCustomerOrderSlip)
                && !await HasProcedureAsync(users, work, user.Id, ProcedureEnum.CreateBilling),
                "The default MSAP User does not retain its procedure permissions.");
            await CheckApprovalsAsync(users, work, user.Id, false);
            var accessService = new UserAccessService(work, users, new RoleService(users), NullLogger<UserAccessService>.Instance);
            var update = new UserAccess
            {
                Id = permission.Id,
                UserId = permission.UserId,
                CanCreateCustomerOrderSlip = true,
                CanApproveSchedule = true,
                CanApproveRateUpload = true,
                CanApproveFinalBilling = true
            };
            Check((await accessService.UpdateAsync(update, "approval-check", default)).IsSuccess, "Cannot save approval assignments.");
            await CheckApprovalsAsync(users, work, user.Id, true);
            foreach (ProcedureEnum procedure in new[] { ProcedureEnum.ApproveSchedule, ProcedureEnum.ApproveRateUpload, ProcedureEnum.ApproveFinalBilling })
            {
                var recipients = (await work.UserAccess.GetUserIdsWithAccessAsync(procedure)).ToHashSet();
                Check(recipients.SetEquals(procedure == ProcedureEnum.ApproveSchedule ? new[] { user.Id, unrelatedAdmin.Id } : new[] { user.Id }),
                    "Schedule recipients must include active administrators; other approvals must remain explicitly assigned.");
            }
            update.CanApproveRateUpload = false;
            Check((await accessService.UpdateAsync(update, "approval-check", default)).IsSuccess, "Cannot revoke rate approval.");
            Check(await HasProcedureAsync(users, work, user.Id, ProcedureEnum.ApproveSchedule)
                && !await HasProcedureAsync(users, work, user.Id, ProcedureEnum.ApproveRateUpload)
                && await HasProcedureAsync(users, work, user.Id, ProcedureEnum.ApproveFinalBilling), "Approval assignments are not independent.");
            update.CanApproveRateUpload = true;
            Check((await accessService.UpdateAsync(update, "approval-check", default)).IsSuccess, "Cannot restore rate approval.");
            Check((await users.AddToRoleAsync(user, "Admin")).Succeeded, "Cannot restore Filpride Admin for override check.");
            Check((await users.AddClaimAsync(user, new Claim(MsapRoles.ClaimType, MsapRoles.User))).Succeeded,
                "Cannot assign MSAP User.");
            Check(await HasRoleAsync(users, work, principal, MsapRoles.Admin), "Shared Admin lost MSAP administrator access.");
            Check(!await HasRoleAsync(users, work, principal, MsapRoles.User), "Shared Admin was downgraded by its MSAP User claim.");
            Check(await HasProcedureAsync(users, work, user.Id, ProcedureEnum.CreateCustomerOrderSlip)
                && await HasProcedureAsync(users, work, user.Id, ProcedureEnum.CreateBilling),
                "Shared Admin does not retain supported MSAP actions.");

            Check((await users.ReplaceClaimAsync(user, new Claim(MsapRoles.ClaimType, MsapRoles.User),
                new Claim(MsapRoles.ClaimType, MsapRoles.Admin))).Succeeded, "Cannot assign MSAP Admin for authorization check.");
            Check(await users.IsInRoleAsync(user, "Admin"), "MSAP role editing removed the Filpride role.");
            Check((await users.GetClaimsAsync(user)).Any(claim => claim.Type == "Company" && claim.Value == "Filpride"),
                "MSAP role editing removed an unrelated shared claim.");
            Check((await users.GetClaimsAsync(user)).Count(claim => claim.Type == MsapRoles.ClaimType) == 1,
                "MSAP role editing left multiple role claims.");
            Check(await HasRoleAsync(users, work, principal, MsapRoles.Admin)
                && !await HasRoleAsync(users, work, principal, MsapRoles.SuperAdmin), "MSAP Admin boundary failed.");
            Check(await HasProcedureAsync(users, work, user.Id, ProcedureEnum.CreateBilling), "MSAP Admin lacks procedure access.");
            await CheckApprovalsAsync(users, work, user.Id, false, scheduleAdmin: true);
            Check((await users.ReplaceClaimAsync(user, new Claim(MsapRoles.ClaimType, MsapRoles.Admin),
                new Claim(MsapRoles.ClaimType, MsapRoles.SuperAdmin))).Succeeded, "Cannot bootstrap MSAP SuperAdmin.");
            Check(await HasRoleAsync(users, work, principal, MsapRoles.SuperAdmin)
                && await HasRoleAsync(users, work, principal, MsapRoles.Admin, MsapRoles.SuperAdmin),
                "MSAP SuperAdmin cannot access both administration areas.");
            await CheckApprovalsAsync(users, work, user.Id, false, scheduleAdmin: true);
            Check((await work.UserAccess.GetUserIdsWithAccessAsync(ProcedureEnum.ApproveSchedule)).Contains(user.Id), "MSAP SuperAdmin is missing from schedule recipients.");
            Check((await work.UserAccess.GetUserIdsWithAccessAsync(ProcedureEnum.ApproveFinalBilling)).Contains(user.Id), "MSAP SuperAdmin is missing from final-bill approvers.");
            user.IsActive = false;
            Check((await users.UpdateAsync(user)).Succeeded, "Cannot deactivate the test account.");
            principal.Identities.First().AddClaim(new Claim(MsapRoles.ClaimType, MsapRoles.SuperAdmin));
            Check(!await HasRoleAsync(users, work, principal, MsapRoles.SuperAdmin)
                && !await HasProcedureAsync(users, work, user.Id, ProcedureEnum.CreateBilling),
                "An inactive account retained MSAP access through its cookie or permissions.");
            await CheckApprovalsAsync(users, work, user.Id, false);
            Check(!(await work.UserAccess.GetUserIdsWithAccessAsync(ProcedureEnum.ApproveSchedule)).Contains(user.Id), "Inactive Admin remained a schedule recipient.");
            user.IsActive = true;
            Check((await users.UpdateAsync(user)).Succeeded, "Cannot reactivate the test account.");
            Check((await users.ReplaceClaimAsync(user, new Claim(MsapRoles.ClaimType, MsapRoles.SuperAdmin),
                new Claim(MsapRoles.ClaimType, MsapRoles.User))).Succeeded, "Cannot demote MSAP SuperAdmin for stale cookie check.");
            Check(!await HasRoleAsync(users, work, principal, MsapRoles.SuperAdmin), "A stale cookie retained SuperAdmin after demotion.");

            Check((await users.AddClaimAsync(user, new Claim(MsapRoles.ClaimType, MsapRoles.Admin))).Succeeded,
                "Cannot prepare duplicate role check.");
            Check(!await HasRoleAsync(users, work, principal, MsapRoles.User, MsapRoles.Admin), "Ambiguous MSAP roles granted access.");
            Check((await users.RemoveClaimAsync(user, new Claim(MsapRoles.ClaimType, MsapRoles.Admin))).Succeeded,
                "Cannot clean up duplicate role check.");
            string?[] allowed = (await new RoleService(users).GetAllRolesAsync(default)).Select(role => role.Name).ToArray();
            Check(allowed.Length == 3 && allowed.All(MsapRoles.IsValid), "MSAP role list contains other roles.");
            await work.UserAccess.RemoveAsync(permission);
            Check((await users.RemoveFromRoleAsync(unrelatedAdmin, "Admin")).Succeeded, "Cannot prepare the unassigned User check.");
            await CheckApprovalsAsync(users, work, unrelatedAdmin.Id, false);
            Check((await users.DeleteAsync(unrelatedAdmin)).Succeeded, "Cannot remove the unassigned test account.");
            await work.SaveAsync();
            Check((await users.RemoveFromRoleAsync(user, "Admin")).Succeeded, "Cannot clean up Filpride test membership.");
            Check((await roles.DeleteAsync((await roles.FindByNameAsync("Admin"))!)).Succeeded, "Cannot clean up Filpride test role.");
            Console.WriteLine("PASS: MSAP Admin defaults, explicit role overrides, procedure permissions, shared role preservation, inactive accounts and stale cookies.");
            Console.WriteLine("PASS: explicit workflow approval grants/revocation, stable procedure values, Admin/SuperAdmin approval access, explicit User approval grants and inactive-account denial.");
        }

        private static async Task CheckApprovalsAsync(UserManager<ApplicationUser> users, IUnitOfWork work, string userId, bool expected, bool scheduleAdmin = false)
        {
            foreach (ProcedureEnum procedure in new[] { ProcedureEnum.ApproveSchedule, ProcedureEnum.ApproveRateUpload, ProcedureEnum.ApproveFinalBilling })
            {
                bool allowed = expected || scheduleAdmin;
                Check(await HasProcedureAsync(users, work, userId, procedure) == allowed, $"Unexpected access to {procedure}.");
            }
        }

        private static async Task<bool> HasRoleAsync(UserManager<ApplicationUser> users, IUnitOfWork work, ClaimsPrincipal principal, params string[] roles)
        {
            var requirement = new ClaimsAuthorizationRequirement(MsapRoles.ClaimType, roles);
            var context = new AuthorizationHandlerContext([requirement], principal, null);
            // Run the default claim handler too: persisted cookie claims must not bypass the live role check.
            await requirement.HandleAsync(context);
            await new MsapRoleHandler(new RoleService(users), new AccessControlService(users,
                new UserAccessService(work, users, new RoleService(users), NullLogger<UserAccessService>.Instance))).HandleAsync(context);
            return context.HasSucceeded;
        }

        private static Task<bool> HasProcedureAsync(UserManager<ApplicationUser> users, IUnitOfWork work, string userId, ProcedureEnum procedure)
        {
            return new UserAccessService(work, users, new RoleService(users), NullLogger<UserAccessService>.Instance)
                .CheckAccess(userId, procedure);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
