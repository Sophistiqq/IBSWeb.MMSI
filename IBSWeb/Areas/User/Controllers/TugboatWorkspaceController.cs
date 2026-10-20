using IBS.Services;
using IBS.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.User.Controllers
{
    [Area("User")]
    [Authorize]
    public class TugboatWorkspaceController : Controller
    {
        private readonly TugboatWorkspaceService _service;

        public TugboatWorkspaceController(TugboatWorkspaceService service)
        {
            _service = service;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Select(int? tugboatId, CancellationToken ct)
        {
            if (!ModelState.IsValid || tugboatId is <= 0 || await _service.GetNameAsync(tugboatId, ct) == null)
            {
                return BadRequest("Select a company-owned tugboat or consolidated MMSI.");
            }

            Response.Cookies.Append(TugboatWorkspace.CookieName, _service.Protect(User, tugboatId), new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(30)
            });
            return RedirectToAction("Index", "Home", new { area = "User" });
        }
    }
}
