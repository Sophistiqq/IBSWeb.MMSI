using System.Diagnostics;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApplicationUser = IBS.Models.ApplicationUser;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    public class HomeController(
        UserManager<ApplicationUser> userManager,
        MsapDbContext dbContext,
        IAccessControlService accessControl,
        DashboardService dashboardService) : Controller
    {
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var model = new DashboardCountViewModel { UpdatedAt = DateTimeHelper.GetCurrentPhilippineTime() };
            model.ShowDashboard = User.Identity?.IsAuthenticated == true;
            if (!model.ShowDashboard)
            {
                return View(model);
            }

            var userId = userManager.GetUserId(User);
            if (userId == null)
            {
                return Challenge();
            }

            model.CanViewDispatch = await accessControl.HasAnyAccessAsync(userId,
                ProcedureEnum.CreateDispatchTicket, ProcedureEnum.EditDispatchTicket, ProcedureEnum.SetTariff, ProcedureEnum.ApproveTariff);
            model.CanViewBilling = await accessControl.HasAnyAccessAsync(userId,
                ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling, ProcedureEnum.DeleteBilling, ProcedureEnum.ReverseBilling, ProcedureEnum.ApproveFinalBilling);
            model.CanCreateBilling = await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateBilling);
            model.CanCreateCollection = await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateCollection);
            model.CanApproveSchedule = await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveSchedule);
            model.CanApproveBilling = await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveFinalBilling);
            model.CanApproveRates = await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveRateUpload);
            model.CanManageRates = await accessControl.HasAccessAsync(userId, ProcedureEnum.ManageMaritimeMasterFile);
            model.CanViewReports = await accessControl.HasAccessAsync(userId, ProcedureEnum.ViewMaritimeReport);
            model.CanViewFinance = model.CanViewBilling || model.CanCreateCollection
                || model.CanViewReports;

            await dashboardService.LoadAsync(model, ct);
            return View(model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        public async Task<IActionResult> Maintenance()
        {
            if (await dbContext.AppSettings
                    .Where(s => s.SettingKey == "MaintenanceMode")
                    .Select(s => s.Value == "true")
                    .FirstOrDefaultAsync())
            {
                return View("Maintenance");
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
