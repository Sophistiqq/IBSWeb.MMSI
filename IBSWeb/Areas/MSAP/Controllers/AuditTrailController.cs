using IBS.Models.MSAP;
using IBS.Services.MSAP;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    public class AuditTrailController(IAuditTrailService auditTrailService, CustomerOrderSlipService customerOrderSlipService, ILogger<AuditTrailController> logger): Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            ViewBag.CustomerOrderSlips = await customerOrderSlipService.GetCustomerOrderSlipSelectListAsync(cancellationToken);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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

        public async Task<IActionResult> GetTimeline(int id, CancellationToken cancellationToken)
        {
            try
            {
                var timeline = await auditTrailService.GetCustomerOrderSlipTimelineAsync(id, cancellationToken);
                return PartialView("_Timeline", timeline);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching timeline for COS {CustomerOrderSlipId}", id);
                return StatusCode(500, "Internal server error while fetching timeline.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchCustomerOrderSlips(string term, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Json(new List<object>());
            }

            var parameters = new DataTablesParameters
            {
                Search = new DataTablesSearch { Value = term },
                Length = 10,
                Start = 0
            };

            (IEnumerable<CustomerOrderSlip> data, _, _) = await customerOrderSlipService.GetPagedCustomerOrderSlipsAsync(parameters, cancellationToken);

            var result = data.Select(jo => new
            {
                id = jo.CustomerOrderSlipId,
                text = $"{jo.CustomerOrderSlipNumber} - {jo.Vessel.VesselName}"
            });

            return Json(result);
        }
    }
}
