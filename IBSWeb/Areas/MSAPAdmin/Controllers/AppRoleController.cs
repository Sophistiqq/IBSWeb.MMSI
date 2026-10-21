using IBS.Models.MSAP;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAPAdmin.Controllers
{
    [Area("MSAPAdmin")]
    [Authorize(Policy = MsapRoles.AdminPolicy)]
    public class AppRoleController(IRoleService roleService)
        : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var roles = await roleService.GetAllRolesAsync(cancellationToken);
            return View(roles);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetRolesList([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var (data, totalRecords) = await roleService.GetPagedRolesAsync(parameters, cancellationToken);

                return Json(new
                {
                    draw = parameters.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = totalRecords,
                    data
                });
            }
            catch (Exception)
            {
                return Json(new { error = "Internal server error" });
            }
        }
    }
}
