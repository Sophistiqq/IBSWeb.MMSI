using IBS.Models.MSAP;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [Authorize]
    public class AuditTrailController(IAuditTrailService auditTrailService, CustomerOrderSlipService customerOrderSlipService, ILogger<AuditTrailController> logger): Controller
    {
        [Authorize(Policy = MsapRoles.AdminPolicy)]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            ViewBag.CustomerOrderSlips = await customerOrderSlipService.GetCustomerOrderSlipSelectListAsync(cancellationToken);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = MsapRoles.AdminPolicy)]
        public async Task<IActionResult> GetPagedAuditTrails([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            var (data, filtered, total) = await auditTrailService.GetPagedAuditTrailsAsync(parameters, cancellationToken);
            return Json(new
            {
                draw = parameters.Draw,
                recordsTotal = total,
                recordsFiltered = filtered,
                data
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetTimeline(int id, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid || id <= 0)
            {
                return BadRequest("Select a valid COS to load its history.");
            }

            try
            {
                var timeline = await auditTrailService.GetCustomerOrderSlipTimelineAsync(id, cancellationToken);
                return PartialView("_Timeline", timeline);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error fetching timeline for COS {CustomerOrderSlipId}", id);
                return StatusCode(500, "Internal server error while fetching timeline.");
            }
        }

    }
}
