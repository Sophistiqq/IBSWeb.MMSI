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

        public async Task<IActionResult> Index(CancellationToken ct, bool awaitingReview = false, int page = 1)
        {
            const int pageSize = 10;
            var (items, totalCount) = await _rates.GetPagedAsync(awaitingReview, page, pageSize, ct);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            ViewData["CanUpload"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ManageRates);
            ViewData["AwaitingReview"] = awaitingReview;
            ViewData["Page"] = Math.Clamp(page, 1, totalPages);
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalCount"] = totalCount;
            return View(items);
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ManageRates)]
        public IActionResult Upload()
        {
            return RedirectToAction(nameof(Editor));
        }

        [HttpGet]
        public async Task<IActionResult> Editor(
            CancellationToken ct,
            RateTableKind kind = RateTableKind.Tariff,
            DateOnly? asOfDate = null,
            Guid? id = null,
            int? customerId = null,
            int? portId = null,
            int? terminalId = null,
            int? serviceId = null,
            decimal? amount = null,
            int? scheduleId = null,
            string? mode = null,
            string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Choose a valid table and effective date.");
            }
            ViewData["CanEditRates"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ManageRates);
            ViewData["CanApproveRates"] = await _access.HasAccessAsync(UserId, ProcedureEnum.ApproveRateUpload);
            RateEditorViewModel? model = await _rates.GetEditorAsync(
                kind,
                asOfDate ?? DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()),
                id,
                UserId,
                ct,
                scheduleId,
                mode,
                customerId,
                portId,
                terminalId,
                serviceId);
            if (model == null)
            {
                return NotFound();
            }
            model.InitialAmount = amount;
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                model.ReturnUrl = returnUrl;
            }
            else if (model.ScheduleId.HasValue)
            {
                model.ReturnUrl = Url.Action("Details", "VesselSchedule", new { id = model.ScheduleId.Value });
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ManageRates)]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> PreviewBatch([FromBody] BatchRateUpdateRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Some dates or amounts are invalid. Check the highlighted inputs." });
            }

            var result = await _rates.PreviewBatchAsync(request, ct);
            return result.IsSuccess ? Json(result.Data) : BadRequest(new { message = result.Message });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ManageRates)]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> SaveBatch([FromBody] BatchRateUpdateRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Some dates or amounts are invalid. Check the highlighted inputs." });
            }

            var canApprove = await _access.HasAccessAsync(UserId, ProcedureEnum.ApproveRateUpload);
            if (string.Equals(request.Action, "approve", StringComparison.OrdinalIgnoreCase) && !canApprove)
            {
                return Forbid();
            }

            try
            {
                var result = await _rates.SaveBatchAsync(request, UserId, canApprove, ct);
                if (!result.IsSuccess)
                {
                    return BadRequest(new { message = result.Message });
                }

                var data = result.Data!;
                TempData["success"] = result.Message;
                var primaryId = data.TariffUploadId ?? data.BafUploadId;
                return Json(new
                {
                    success = true,
                    message = result.Message,
                    approved = data.Approved,
                    tariffUploadId = data.TariffUploadId,
                    bafUploadId = data.BafUploadId,
                    tariffUrl = data.TariffUploadId.HasValue ? Url.Action(nameof(Details), new { id = data.TariffUploadId.Value }) : null,
                    bafUrl = data.BafUploadId.HasValue ? Url.Action(nameof(Details), new { id = data.BafUploadId.Value }) : null,
                    url = primaryId.HasValue ? Url.Action(nameof(Details), new { id = primaryId.Value }) : null
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not save batch rate update");
                return StatusCode(500, new { message = "The rate update could not be saved. Your edits remain on this page; try again." });
            }
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ManageRates)]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> ImportExcel(IFormFile? file, RateTableKind kind, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
            {
                TempData["error"] = "Choose an exported .xlsx workbook to stage.";
                return RedirectToAction(nameof(Index));
            }
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["error"] = "Only .xlsx Excel workbooks are supported.";
                return RedirectToAction(nameof(Index));
            }
            if (!Enum.IsDefined(kind))
            {
                TempData["error"] = "Choose Tariff or BAF.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await _rates.StageExcelUploadAsync(stream, file.FileName, kind, UserId, ct);
                if (result.IsSuccess)
                {
                    TempData["success"] = result.Message;
                    return RedirectToAction(nameof(Details), new { id = result.Data });
                }
                TempData["error"] = result.Message;
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not stage rate Excel upload");
                TempData["error"] = "Unable to read the uploaded workbook. Export a fresh template and try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ApproveRateUpload)]
        public async Task<IActionResult> PreviewScheduleRate(int scheduleId, int entryId, RateTableKind kind,
            DateOnly effectiveDate, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Choose a valid rate kind and effective date." });
            }

            if (!await _access.HasAccessAsync(UserId, ProcedureEnum.ApproveSchedule))
            {
                return Forbid();
            }

            ServiceResult<ScheduleRateCaptureViewModel> result = await _rates.PreviewScheduleCaptureAsync(scheduleId, entryId, kind, effectiveDate, ct);
            return result.IsSuccess ? Json(result.Data) : BadRequest(new { message = result.Message });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveRateUpload)]
        public async Task<IActionResult> CaptureScheduleRate(int scheduleId, int entryId, RateTableKind kind,
            DateOnly effectiveDate, string fingerprint, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Review the amount and effective date again." });
            }

            if (!await _access.HasAccessAsync(UserId, ProcedureEnum.ApproveSchedule))
            {
                return Forbid();
            }

            try
            {
                ServiceResult<Guid> result = await _rates.CaptureScheduleRateAsync(scheduleId, entryId, kind, effectiveDate,
                    fingerprint, UserId, ct);
                return result.IsSuccess
                    ? Json(new
                    {
                        message = result.Message, url = Url.Action(nameof(Details), new { id = result.Data })
                    })
                    : BadRequest(new { message = result.Message });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not capture schedule {ScheduleId} rate", scheduleId);
                return StatusCode(500, new { message = "The rate could not be saved. Review again before retrying." });
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveRateUpload)]
        [RequestFormLimits(ValueCountLimit = 10010)]
        public async Task<IActionResult> SubmitAndApprove(Guid id, int[] selectedRows, CancellationToken ct)
        {
            if (!await _access.HasAccessAsync(UserId, ProcedureEnum.ManageRates))
            {
                return Forbid();
            }

            try
            {
                var result = await _rates.SubmitAndApproveAsync(id, selectedRows, UserId, ct);
                TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not submit and approve rate update {Id}", id);
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
