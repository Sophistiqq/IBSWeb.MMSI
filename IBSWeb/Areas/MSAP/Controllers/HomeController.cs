using System.Diagnostics;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
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
        IUnitOfWork unitOfWork,
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

            await dashboardService.SetAccessAsync(model, userId, accessControl);
            if (!model.ShowDashboard)
            {
                return View(model);
            }

            await dashboardService.LoadAsync(model, ct);
            return View(model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        public async Task<IActionResult> Maintenance(CancellationToken ct)
        {
            if (await unitOfWork.AppSetting.GetAllQuery(s => s.SettingKey == "MaintenanceMode")
                    .Select(s => s.Value == "true")
                    .FirstOrDefaultAsync(ct))
            {
                return View("Maintenance");
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
