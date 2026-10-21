using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAPSuperAdmin.Controllers
{
    [Area("MSAPSuperAdmin")]
    [Authorize(Policy = MsapRoles.SuperAdminPolicy)]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
