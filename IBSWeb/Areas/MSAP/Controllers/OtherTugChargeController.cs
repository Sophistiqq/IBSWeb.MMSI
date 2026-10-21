using System.Security.Claims;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Services.MSAP.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess(ProcedureEnum.ManageOtherTugCharges, ProcedureEnum.ConfirmOtherTugCharges)]
    public class OtherTugChargeController : Controller
    {
        private readonly OtherTugChargeService _charges;
        private readonly IUnitOfWork _work;
        private readonly IAccessControlService _access;
        private readonly ILogger<OtherTugChargeController> _logger;

        public OtherTugChargeController(OtherTugChargeService charges, IUnitOfWork work, IAccessControlService access,
            ILogger<OtherTugChargeController> logger)
        {
            _charges = charges;
            _work = work;
            _access = access;
            _logger = logger;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        [HttpGet]
        public async Task<IActionResult> Editor(int customerOrderSlipId, int? id, int? ticketId, CancellationToken ct)
        {
            var model = await _charges.GetEditorAsync(customerOrderSlipId, id, ticketId, ct);
            if (model == null)
            {
                return NotFound();
            }
            var job = await _work.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == customerOrderSlipId, ct);
            var schedule = await _work.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, ct);
            model.Progress = await JobProgressCalculator.LoadAsync(_work, job, schedule, ct);
            ViewData["CanManageCharges"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ManageOtherTugCharges);
            ViewData["CanConfirmCharges"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ConfirmOtherTugCharges);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(11 * 1024 * 1024)]
        [RequireAccess(ProcedureEnum.ManageOtherTugCharges)]
        public async Task<IActionResult> Save(OtherTugChargeViewModel model, IFormFile? document, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Review the provider reference, date and allocated amounts." });
            }
            var result = await _charges.SaveAsync(model, document, UserId, ct);
            return Json(new { success = result.IsSuccess, message = result.Message,
                redirectUrl = result.IsSuccess ? Url.Action(nameof(Editor), new { customerOrderSlipId = model.CustomerOrderSlipId, id = result.Data }) : null });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ConfirmOtherTugCharges)]
        public async Task<IActionResult> Confirm(int id, int reviewedRevision, CancellationToken ct)
        {
            try
            {
                var result = await _charges.ConfirmAsync(id, reviewedRevision, UserId, ct);
                return Json(new { success = result.IsSuccess, message = result.Message });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not confirm provider charge {ChargeId}", id);
                return Json(new { success = false, message = "Unable to confirm the charge. Reload and review the current details." });
            }
        }
    }
}
