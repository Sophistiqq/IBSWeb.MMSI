using System.Security.Claims;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess(ProcedureEnum.ViewRates, ProcedureEnum.ManageRates, ProcedureEnum.ApproveRateUpload)]
    public class RateUploadController : Controller
    {
        private readonly RateUploadService _rates;
        private readonly IAccessControlService _access;
        private readonly ILogger<RateUploadController> _logger;
        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        public RateUploadController(RateUploadService rates, IAccessControlService access, ILogger<RateUploadController> logger)
        {
            _rates = rates;
            _access = access;
            _logger = logger;
        }

        public async Task<IActionResult> Index(CancellationToken ct, bool awaitingReview = false)
        {
            ViewData["CanUpload"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ManageRates);
            ViewData["AwaitingReview"] = awaitingReview;
            return View(await _rates.GetRecentAsync(awaitingReview, ct));
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ManageRates)]
        public IActionResult Upload()
        {
            return RedirectToAction(nameof(Editor));
        }

        [HttpGet]
        public async Task<IActionResult> Editor(CancellationToken ct, RateTableKind kind = RateTableKind.Tariff, DateOnly? asOfDate = null, Guid? id = null)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Choose a valid table and effective date.");
            }
            ViewData["CanEditRates"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ManageRates);
            RateEditorViewModel? model = await _rates.GetEditorAsync(kind, asOfDate ?? DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()), id, UserId, ct);
            return model == null ? NotFound() : View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ManageRates)]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> SaveDraft([FromBody] RateEditorViewModel model, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Some dates or amounts are invalid. Check the highlighted cells." });
            }
            try
            {
                ServiceResult<Guid> result = await _rates.SaveDraftAsync(model, UserId, ct);
                return result.IsSuccess ? Json(new { url = Url.Action(nameof(Details), new { id = result.Data }) }) : BadRequest(new { message = result.Message });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not save rate draft");
                return StatusCode(500, new { message = "The draft could not be saved. Your edits remain on this page; try again." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(Guid id, CancellationToken ct)
        {
            RateUploadViewModel? model = await _rates.GetAsync(id, UserId, ct);
            if (model == null)
            {
                return NotFound();
            }
            ViewData["CanUpload"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ManageRates);
            ViewData["CanApprove"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ApproveRateUpload);
            ViewData["CanReviewTickets"] = await _access.HasAccessAsync(UserId, ProcedureEnum.EditDispatchTicket);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ManageRates)]
        [RequestFormLimits(ValueCountLimit = 10010)]
        public async Task<IActionResult> Submit(Guid id, int[] selectedRows, CancellationToken ct)
        {
            return await SaveAsync(id, false, selectedRows, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ManageRates)]
        public async Task<IActionResult> Refresh(Guid id, CancellationToken ct)
        {
            return await SaveAsync(id, true, [], ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveRateUpload)]
        public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
        {
            try
            {
                ServiceResult result = await _rates.ApproveAsync(id, UserId, ct);
                TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not approve rate update {Id}", id);
                TempData["error"] = "The update could not be approved. Refresh and try again.";
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<IActionResult> SaveAsync(Guid id, bool refresh, int[] selectedRows, CancellationToken ct)
        {
            try
            {
                ServiceResult result = await _rates.SaveAsync(id, UserId, refresh, selectedRows, ct);
                TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not save rate update {Id}", id);
                TempData["error"] = "The update could not be saved. Refresh and try again.";
            }
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
