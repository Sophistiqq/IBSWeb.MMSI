using System.Reflection;
using System.Security.Claims;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Constants;
using IBSWeb.Areas.MSAP.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class WorkflowAccessCheck
    {
        public static async Task RunAsync()
        {
            UserAccess? grants = new() { UserId = "access-check" };
            string? role = MsapRoles.User;
            IRoleService roles = DispatchProxy.Create<IRoleService, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)roles).Handler = (_, _) => Task.FromResult(role);
            IUserAccessRepository repository = DispatchProxy.Create<IUserAccessRepository, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)repository).Handler = (_, _) => Task.FromResult(grants);
            IUnitOfWork work = DispatchProxy.Create<IUnitOfWork, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)work).Handler = (_, _) => repository;
            var access = new UserAccessService(work, null!, roles, NullLogger<UserAccessService>.Instance);
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IAccessControlService>(new AccessControlService(null!, access)).BuildServiceProvider();

            async Task AssertAccessAsync(ProcedureEnum permission, bool allowed)
            {
                Check(await access.CheckAccess("access-check", permission) == allowed, $"Unexpected grant for {role}: {permission}.");
            }
            async Task AssertEndpointAsync(Type controller, string action, bool allowed, bool post = false)
            {
                MethodInfo method = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Single(m => m.Name == action && (m.GetCustomAttribute<HttpPostAttribute>() != null) == post);
                RequireAccessBaseAttribute[] filters = controller.GetCustomAttributes<RequireAccessBaseAttribute>()
                    .Concat(method.GetCustomAttributes<RequireAccessBaseAttribute>()).ToArray();
                Check(filters.Length > 0, $"Missing access guard: {controller.Name}.{action}.");
                var http = new DefaultHttpContext { RequestServices = services };
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "access-check")], "check"));
                http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
                var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
                foreach (RequireAccessBaseAttribute filter in filters)
                {
                    await filter.OnAuthorizationAsync(context);
                    if (context.Result != null)
                    {
                        break;
                    }
                }
                Check((context.Result == null) == allowed, $"Unexpected endpoint access: {controller.Name}.{action}.");
            }

            var dashboardService = new DashboardService(null!, null!, NullLogger<DashboardService>.Instance);
            async Task<DashboardCountViewModel> DashboardAsync()
            {
                var model = new DashboardCountViewModel();
                await dashboardService.SetAccessAsync(model, "access-check", new AccessControlService(null!, access));
                return model;
            }
            async Task AssertEntryAsync(bool expected)
            {
                var requirement = new ClaimsAuthorizationRequirement(MsapRoles.ClaimType, [MsapRoles.User, MsapRoles.Admin, MsapRoles.SuperAdmin]);
                var principal = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "access-check"), new Claim(MsapRoles.ClaimType, MsapRoles.User)], "check"));
                var context = new AuthorizationHandlerContext([requirement], principal, null);
                await requirement.HandleAsync(context);
                await new MsapRoleHandler(roles, new AccessControlService(null!, access)).HandleAsync(context);
                Check(context.HasSucceeded == expected, "MSAP entry policy ignored effective grants or trusted a stale cookie.");
            }
            Check(!(await DashboardAsync()).ShowDashboard, "An unassigned User can open MSAP.");
            await AssertEntryAsync(false);
            var hidden = await DashboardAsync();
            await dashboardService.LoadAsync(hidden, default);
            Check(hidden.DataError == null, "A denied dashboard attempted to load data.");
            grants.CanCreateBilling = true;
            var billingDashboard = await DashboardAsync();
            Check(billingDashboard.ShowDashboard && billingDashboard.CanViewBilling && billingDashboard.CanReviewCalculations
                && !billingDashboard.CanViewCollections && !billingDashboard.CanViewBanking && !billingDashboard.CanViewDispatch,
                "Billing access exposed receipts, banking or operational dashboard data.");
            await AssertEntryAsync(true);
            grants.CanCreateBilling = false;
            await AssertEntryAsync(false);
            grants.CanCreateCollection = true;
            var collectionDashboard = await DashboardAsync();
            Check(collectionDashboard.CanViewCollections && !collectionDashboard.CanViewBilling && !collectionDashboard.CanViewBanking
                && !collectionDashboard.CanReviewCalculations && !collectionDashboard.CanViewDispatch,
                "Receipt preparation exposed unrelated dashboard data.");
            grants.CanCreateCollection = false;
            grants.CanAccessTreasury = true;
            var bankingDashboard = await DashboardAsync();
            Check(bankingDashboard.CanViewCollections && bankingDashboard.CanViewBanking && !bankingDashboard.CanCreateCollection
                && !bankingDashboard.CanViewBilling, "Banking access changed preparation authority or financial visibility.");
            grants.CanAccessTreasury = false;
            grants.CanManageRates = true;
            var rateDashboard = await DashboardAsync();
            Check(rateDashboard.CanViewRates && rateDashboard.CanReviewCalculations && !rateDashboard.CanReadCos
                && !rateDashboard.CanViewDispatch && !rateDashboard.CanViewFinance, "Rate preparation exposed unrelated dashboard data.");
            grants.CanManageRates = false;
            grants.CanViewRates = true;
            Check((await DashboardAsync()).ShowDashboard && !(await DashboardAsync()).CanReviewCalculations,
                "Rate-only viewing did not retain entry or inherited correction work.");
            grants.CanViewRates = false;
            grants.CanManageOtherTugCharges = true;
            Check((await DashboardAsync()).CanManageOtherTugCharges && !(await DashboardAsync()).CanConfirmOtherTugCharges
                && !(await DashboardAsync()).CanViewFinance, "Provider preparation inherited confirmation or unrelated summaries.");
            grants.CanManageOtherTugCharges = false;
            grants.CanConfirmOtherTugCharges = true;
            Check((await DashboardAsync()).CanConfirmOtherTugCharges && !(await DashboardAsync()).CanManageOtherTugCharges,
                "Provider review inherited preparation authority.");
            grants.CanConfirmOtherTugCharges = false;
            grants.CanViewMaritimeReport = true;
            var reportDashboard = await DashboardAsync();
            Check(reportDashboard.CanViewFinance && reportDashboard.CanViewBanking && reportDashboard.CanViewDispatch
                && !reportDashboard.CanCreateBilling && !reportDashboard.CanApproveBilling && !reportDashboard.CanCreateSchedule,
                "Reporting visibility granted workflow actions.");
            grants.CanViewMaritimeReport = false;
            foreach (string adminRole in new[] { MsapRoles.Admin, MsapRoles.SuperAdmin })
            {
                role = adminRole;
                var adminDashboard = await DashboardAsync();
                Check(adminDashboard.ShowDashboard && adminDashboard.CanViewDispatch && adminDashboard.CanViewFinance
                    && adminDashboard.CanViewBanking && adminDashboard.CanManageOtherTugCharges && adminDashboard.CanConfirmOtherTugCharges,
                    "Administrator dashboard lost supported sections.");
                await AssertEntryAsync(true);
            }
            Check(MsapRoles.GetRole([new Claim(MsapRoles.ClaimType, MsapRoles.User)], isSharedAdmin: true) == MsapRoles.Admin,
                "Shared Admin was downgraded by its MSAP User assignment.");
            role = null;
            await AssertEntryAsync(false);
            role = MsapRoles.User;

            foreach (ProcedureEnum permission in Enum.GetValues<ProcedureEnum>())
            {
                await AssertAccessAsync(permission, false);
            }
            await AssertEndpointAsync(typeof(VesselScheduleController), "Create", false, post: true);
            grants.CanApproveSchedule = true;
            await AssertEndpointAsync(typeof(VesselScheduleController), "Details", true);
            await AssertEndpointAsync(typeof(CustomerOrderSlipController), "GetCustomerOrderSlipList", true, post: true);
            await AssertEndpointAsync(typeof(VesselScheduleController), "Create", false, post: true);
            grants.CanApproveSchedule = false;
            grants.CanCreateCustomerOrderSlip = true;
            await AssertEndpointAsync(typeof(VesselScheduleController), "Create", true, post: true);
            await AssertEndpointAsync(typeof(VesselScheduleController), "Confirm", false, post: true);
            grants.CanCreateCustomerOrderSlip = false;
            grants.CanCreateCollection = true;
            await AssertEndpointAsync(typeof(CollectionController), "Create", true, post: true);
            await AssertEndpointAsync(typeof(CollectionController), "Banking", false, post: true);
            await AssertEndpointAsync(typeof(BillingController), "Review", true);
            await AssertEndpointAsync(typeof(BillingController), "ReviewConfirmed", false, post: true);
            grants.CanCreateCollection = false;
            grants.CanAccessTreasury = true;
            await AssertEndpointAsync(typeof(CollectionController), "Preview", true);
            await AssertEndpointAsync(typeof(CollectionController), "Banking", true, post: true);
            await AssertEndpointAsync(typeof(CollectionController), "Create", false, post: true);
            await AssertEndpointAsync(typeof(CollectionController), "Edit", false, post: true);
            grants.CanAccessTreasury = false;
            grants.CanEditBilling = true;
            await AssertEndpointAsync(typeof(BillingController), "GetBillingList", true, post: true);
            await AssertEndpointAsync(typeof(BillingController), "Review", true);
            await AssertEndpointAsync(typeof(BillingController), "ReviewConfirmed", false, post: true);
            grants.CanEditBilling = false;
            grants.CanApproveFinalBilling = true;
            await AssertEndpointAsync(typeof(BillingController), "ReviewConfirmed", true, post: true);
            await AssertEndpointAsync(typeof(BillingController), "Delete", false, post: true);
            grants.CanApproveFinalBilling = false;
            grants.CanManageRates = true;
            await AssertAccessAsync(ProcedureEnum.ViewRates, true);
            await AssertAccessAsync(ProcedureEnum.ApproveRateUpload, false);
            await AssertAccessAsync(ProcedureEnum.ViewOperations, false);
            grants.CanManageRates = false;
            grants.CanManageOtherTugCharges = true;
            await AssertAccessAsync(ProcedureEnum.ViewOperations, true);
            await AssertAccessAsync(ProcedureEnum.ConfirmOtherTugCharges, false);

            ProcedureEnum[] retired = [ProcedureEnum.SetTariff, ProcedureEnum.ApproveTariff, ProcedureEnum.DeleteDispatchTicket,
                ProcedureEnum.DeleteCustomerOrderSlip, ProcedureEnum.CloseCustomerOrderSlip, ProcedureEnum.ReverseBilling,
                ProcedureEnum.CreateDisbursement, ProcedureEnum.ManagePostedPeriod, ProcedureEnum.ViewInventoryReport, ProcedureEnum.ManageMsapImport];
            foreach (PropertyInfo property in typeof(UserAccess).GetProperties().Where(p => p.PropertyType == typeof(bool)))
            {
                property.SetValue(grants, true);
            }
            foreach (string? currentRole in new[] { MsapRoles.User, MsapRoles.Admin, MsapRoles.SuperAdmin, null })
            {
                role = currentRole;
                foreach (ProcedureEnum permission in retired)
                {
                    await AssertAccessAsync(permission, false);
                }
                if (role == null)
                {
                    await AssertAccessAsync(ProcedureEnum.ApproveSchedule, false);
                    await AssertAccessAsync(ProcedureEnum.ViewBillings, false);
                }
                else
                {
                    await AssertAccessAsync(ProcedureEnum.ApproveFinalBilling, true);
                }
            }
            role = MsapRoles.User;
            grants = null;
            await AssertAccessAsync(ProcedureEnum.ViewOperations, false);
            role = MsapRoles.Admin;
            await AssertAccessAsync(ProcedureEnum.ApproveFinalBilling, true);
            await AssertAccessAsync(ProcedureEnum.AccessTreasury, true);
            await AssertAccessAsync((ProcedureEnum)999, false);

            Check(typeof(BillingController).GetMethod("Reverse") == null, "Billing reversal is still exposed.");
            foreach (string name in new[] { "SetTariff", "EditTariff", "ApproveTariff", "DisapproveTariff", "BatchApproveTariff", "BatchSetTariff", "Delete", "Restore", "ChangeStatus", "DeleteImage", "DeleteVideo" })
            {
                Check(typeof(DispatchTicketController).GetMethods().All(m => m.Name != name), $"Retired action still exposed: {name}.");
            }
            Check(typeof(MsapImportController).GetCustomAttribute<AuthorizeAttribute>()?.Policy == MsapRoles.AdminPolicy,
                "Master-file import must remain Admin-only.");
            Check(typeof(MsapImportController).GetMethods().All(method => method.Name != "Reset"), "Legacy import reset is still exposed.");
            Check(typeof(IBSWeb.Areas.MSAPSuperAdmin.Controllers.DataController).GetCustomAttribute<NonControllerAttribute>() != null, "Raw financial editing is still exposed.");
            MethodInfo discard = typeof(BillingController).GetMethod("Delete")!;
            Check(discard.GetCustomAttribute<HttpPostAttribute>() != null && discard.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null,
                "Discarding a preparation must require POST and antiforgery validation.");
            Check(typeof(CustomerOrderSlipController).GetMethods().Where(m => m.Name == "Create").All(m => m.GetCustomAttribute<HttpPostAttribute>() == null),
                "Standalone COS creation can bypass schedule preparation.");
            Console.WriteLine("Workflow access: entry/revocation, dashboard responsibilities, independent action grants and retired routes passed.");
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
