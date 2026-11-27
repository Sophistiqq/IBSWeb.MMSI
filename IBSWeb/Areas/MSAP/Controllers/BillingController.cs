using System.Security.Claims;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace IBSWeb.Areas.MSAP.Controllers
{
    /// <summary>
    /// Controller for managing Billing in the MMSI system.
    /// </summary>
    [Area("MSAP")]
    public class BillingController(
        IUnitOfWork unitOfWork,
        BillingService billingService,
        ICloudStorageService cloudStorageService,
        IAccessControlService accessControl,
        ILogger<BillingController> logger)
        : Controller
    {
        #region Index

        /// <summary>
        /// Displays the list of Billings.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewBillings)]
        public async Task<IActionResult> Index(string filterType, CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            ViewBag.FilterType = filterType;
            ViewBag.CustomerOrderSlipId = customerOrderSlipId;
            if (customerOrderSlipId.HasValue)
            {
                var job = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == customerOrderSlipId.Value, cancellationToken);
                if (job == null)
                {
                    return NotFound();
                }
                var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == job.CustomerOrderSlipId, cancellationToken);
                ViewBag.JobProgressItems = new[] { await JobProgressCalculator.LoadAsync(unitOfWork, job, schedule, cancellationToken) };
            }
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanPost = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateBilling);
            ViewBag.CanDelete = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.DeleteBilling);
            ViewBag.CanViewOperations = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ViewOperations);
            return View(Enumerable.Empty<Billing>());
        }

        #endregion

        #region Create

        /// <summary>
        /// Displays the form to create a new Billing.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling, "Access denied. You don't have permission to create Billings.")]
        public async Task<IActionResult> Create(CancellationToken cancellationToken, int? customerOrderSlipId = null, int? cosId = null)
        {
            var effectiveCosId = customerOrderSlipId ?? cosId;
            var model = new Billing();
            if (effectiveCosId.HasValue)
            {
                var job = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == effectiveCosId.Value, cancellationToken);
                if (job == null)
                {
                    return NotFound();
                }
                model.CustomerId = job.CustomerId;
                model.CustomerOrderSlipId = job.CustomerOrderSlipId;
                var existingDraft = await unitOfWork.Billing.GetAsync(
                    b => b.CustomerOrderSlipId == effectiveCosId.Value && b.Status == MsapConstants.BillingStatus.ForPosting && b.BasicBillingId == null,
                    cancellationToken);
                if (existingDraft != null)
                {
                    model.MsapBillingId = existingDraft.MsapBillingId;
                    model.MsapBillingNumber = existingDraft.MsapBillingNumber;
                    model.Date = existingDraft.Date;
                    model.BilledTo = existingDraft.BilledTo;
                    model.PrincipalId = existingDraft.PrincipalId;
                    model.Terms = existingDraft.Terms;
                    model.Remarks = existingDraft.Remarks;
                    model.PrintDiscount = existingDraft.PrintDiscount;
                    model.AgencyIncentiveRepresentativeId = existingDraft.AgencyIncentiveRepresentativeId;
                    model.AgentCommissionRepresentativeId = existingDraft.AgentCommissionRepresentativeId;
                    model.IsUndocumented = existingDraft.IsUndocumented;
                    model.DiscountOnTariff = existingDraft.DiscountOnTariff;
                    model.DiscountOnBaf = existingDraft.DiscountOnBaf;
                    model.SeparateBaf = existingDraft.BafBilling != null;
                    model.BafBillingNumber = existingDraft.BafBilling?.MsapBillingNumber;
                }
            }
            var company = User.Claims.FirstOrDefault(c => c.Type == "Company")?.Value ?? MsapConstants.Company_MMSI;
            var year = model.Date != default ? model.Date.Year : DateTimeHelper.GetCurrentPhilippineTime().Year;
            var suggestedBir = await unitOfWork.Billing.GetSuggestedBirBillingNumberAsync(company, year, cancellationToken);
            var suggestedBirBaf = unitOfWork.Billing.IncrementBillingSeries(suggestedBir);
            ViewData["SuggestedBirNumber"] = suggestedBir;
            ViewData["SuggestedBirBafNumber"] = suggestedBirBaf;
            if (!model.IsUndocumented && string.IsNullOrWhiteSpace(model.MsapBillingNumber))
            {
                model.MsapBillingNumber = suggestedBir;
            }
            model = await billingService.PopulateBillingSelectListsAsync(model, cancellationToken);
            return View(model);
        }

        /// <summary>
        /// Processes the creation of a new Billing and posts it immediately.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateBilling, "Access denied. You don't have permission to create Billings.")]
        public async Task<IActionResult> Create([Bind("CustomerId,PrincipalId,BilledTo,MsapBillingNumber,Date,CustomerOrderSlipId,SeparateBaf,BafBillingNumber,IsUndocumented,CosDiscountPercent,SkipCosDiscount,DiscountOnTariff,DiscountOnBaf,SbmaEnabled,Terms,Remarks,AgencyIncentiveRepresentativeId,AgentCommissionRepresentativeId,TicketBasisOverrides")] Billing model, CancellationToken cancellationToken)
        {
            try
            {
                if (ModelState.Any(entry => (entry.Key is nameof(Billing.CosDiscountPercent) or nameof(Billing.SkipCosDiscount) or nameof(Billing.SbmaEnabled))
                    && entry.Value!.Errors.Count > 0))
                {
                    return Json(new { success = false, message = "Check the billing fields and enter a discount between 0% and 100%." });
                }
                var username = User.Identity?.Name ?? "System";
                var company = User.Claims.FirstOrDefault(c => c.Type == "Company")?.Value ?? MsapConstants.Company_MMSI;

                var result = await billingService.CreateBillingAsync(model, username, company, cancellationToken);

                if (result.IsSuccess)
                {
                    return Json(new { success = true, message = result.Message, redirectUrl = Url.Action(nameof(Review), new { id = result.Data }) });
                }

                return Json(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create billing");
                return Json(new { success = false, message = ExceptionHelper.GetErrorMessage(ex) });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<IActionResult> PreviewPreparation(
            [Bind("CustomerId,CustomerOrderSlipId,PrincipalId,BilledTo,MsapBillingNumber,Date,SeparateBaf,BafBillingNumber,IsUndocumented,CosDiscountPercent,SkipCosDiscount,DiscountOnTariff,DiscountOnBaf,SbmaEnabled,Terms,Remarks,AgencyIncentiveRepresentativeId,AgentCommissionRepresentativeId,TicketBasisOverrides")] Billing model,
            CancellationToken cancellationToken)
        {
            if (ModelState.Any(entry => (entry.Key is nameof(Billing.CosDiscountPercent) or nameof(Billing.SkipCosDiscount) or nameof(Billing.SbmaEnabled))
                    && entry.Value!.Errors.Count > 0))
            {
                return BadRequest("Check the billing fields and enter a discount between 0% and 100%.");
            }
            var company = User.Claims.FirstOrDefault(c => c.Type == "Company")?.Value ?? MsapConstants.Company_MMSI;
            var result = await billingService.PrepareBillingAsync(model, User.Identity?.Name ?? "System", company, cancellationToken);
            if (!result.IsSuccess || result.Data == null)
            {
                return BadRequest(result.Message);
            }
            await PrepareStatementImagesAsync(model, true);
            ViewData["Statements"] = result.Data;
            ViewData["IsDraft"] = true;
            ViewData["CanPrint"] = false;
            ViewData["IncludeTicketImages"] = false;
            return View("Preview", model);
        }

        #endregion

        #region Delete

        /// <summary>
        /// Discards an unapproved billing preparation.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.DeleteBilling, "Access denied. You don't have permission to discard draft billings.")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            var result = await billingService.DeleteBillingAsync(id, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
            }
            else
            {
                TempData["error"] = result.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Billing review

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewBillings)]
        public async Task<IActionResult> Review(int id, CancellationToken cancellationToken)
        {
            var model = await billingService.GetBillingByIdAsync(id, cancellationToken);
            if (model == null)
            {
                return NotFound();
            }
            model = await billingService.PopulateTicketListsAsync(model, cancellationToken);
            await PrepareStatementImagesAsync(model, true);
            ViewBag.JobProgressItems = await JobProgressCalculator.LoadForBillingsAsync(unitOfWork, [id], cancellationToken);
            ViewData["JobProgressActionStage"] = JobProgressCalculator.PostingStage;
            ViewData["JobProgressActionTargetId"] = id;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewData["CanReviewBilling"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ReviewBilling);
            ViewData["CanApproveFinalBilling"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveFinalBilling);
            ViewData["CanReverseBilling"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ReverseBilling);
            ViewData["CanCreateBilling"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateBilling);
            var basic = model.BasicBilling ?? model;
            var baf = basic.BafBilling;
            var prepIds = new[] { basic.MsapBillingId, baf?.MsapBillingId ?? basic.MsapBillingId }.Distinct().ToList();
            var salesBookHistory = await unitOfWork.Billing.GetBillingIdsWithSalesBookAsync(prepIds, cancellationToken);
            var isUnapprovedDraft = BillingService.IsUnapprovedDraft(basic) && !salesBookHistory.Contains(basic.MsapBillingId)
                && (baf == null || (BillingService.IsUnapprovedDraft(baf) && !salesBookHistory.Contains(baf.MsapBillingId)));
            var isPeriodClosed = await unitOfWork.PostedPeriod.IsMonthClosedAsync(model.Date.Year, model.Date.Month, cancellationToken);
            var isReopenedRevision = model.UnpostedDate.HasValue || model.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
            ViewData["IsUnapprovedDraft"] = isUnapprovedDraft && model.PeriodStatus == null;
            ViewData["CanDiscardBilling"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.DeleteBilling) && isUnapprovedDraft && model.PeriodStatus == null;
            ViewData["PostingBlocker"] = await JobProgressCalculator.GetBillingBlockerAsync(unitOfWork, model, JobProgressCalculator.PostingStage, cancellationToken);
            ViewData["IsPeriodClosed"] = isPeriodClosed && !isReopenedRevision;
            var adjustmentLogs = await unitOfWork.MsapAdjustmentLog.GetAllAsync(
                a => a.DocumentType == "Billing" && a.RecordId == id, cancellationToken);
            List<MsapFieldChange> billingChanges = [];
            string? adjustmentReason = null;
            string? adjustmentAttachmentUrl = model.AdjustmentAttachmentUrl;
            var latestLog = adjustmentLogs
                .OrderByDescending(a => a.EditedDate)
                .ThenByDescending(a => a.Id)
                .FirstOrDefault();
            if (latestLog != null)
            {
                adjustmentAttachmentUrl ??= latestLog.AttachmentUrl;
                if (!string.IsNullOrWhiteSpace(latestLog.ChangesJson))
                {
                    try
                    {
                        var parsed = System.Text.Json.JsonSerializer.Deserialize<List<MsapFieldChange>>(latestLog.ChangesJson);
                        if (parsed != null)
                        {
                            billingChanges = parsed;
                        }
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Ignore malformed change JSON.
                    }
                }
                if (billingChanges.Count > 0)
                {
                    var masterDefaultTerms = (model.Principal?.Terms ?? model.Customer.CustomerTerms)?.Trim();
                    if (string.IsNullOrWhiteSpace(masterDefaultTerms))
                    {
                        masterDefaultTerms = "COD";
                    }
                    var currentTerms = model.Terms?.Trim() ?? string.Empty;
                    var isFormalAdjustment = !string.IsNullOrWhiteSpace(latestLog.AttachmentUrl)
                        || model.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
                    if (!isFormalAdjustment)
                    {
                        billingChanges = billingChanges
                            .Where(c => !(c.Field == nameof(Billing.Terms)
                                && (string.Equals(currentTerms, masterDefaultTerms, StringComparison.OrdinalIgnoreCase)
                                    || !string.Equals(c.After?.Trim(), currentTerms, StringComparison.OrdinalIgnoreCase))))
                            .ToList();
                    }
                }
                if (billingChanges.Count > 0 || !string.IsNullOrWhiteSpace(latestLog.AttachmentUrl))
                {
                    adjustmentReason = latestLog.Reason;
                }
            }
            ViewBag.BillingChanges = billingChanges;
            ViewBag.AdjustmentReason = adjustmentReason;
            ViewBag.AdjustmentAttachmentUrl = adjustmentAttachmentUrl;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ReviewBilling)]
        public async Task<IActionResult> EndorseReview(int id, int reviewedRevision, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var username = User.Identity?.Name ?? "System";
            var result = await billingService.ReviewBillingAsync(id, reviewedRevision, username, userId, cancellationToken);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Review), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAnyAccess(ProcedureEnum.ReviewBilling, ProcedureEnum.ApproveFinalBilling)]
        public async Task<IActionResult> Reject(int id, string reason, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var username = User.Identity?.Name ?? "System";
            var result = await billingService.RejectBillingAsync(id, reason, username, userId, cancellationToken);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Review), new { id });
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<IActionResult> Adjust(int id, CancellationToken cancellationToken)
        {
            var billing = await billingService.GetBillingByIdAsync(id, cancellationToken);
            if (billing == null)
            {
                return NotFound();
            }

            if (billing.Status != MsapConstants.BillingStatus.ForPosting || (!billing.RejectedAt.HasValue && !billing.UnpostedDate.HasValue))
            {
                TempData["error"] = "Adjust Billing is only available after the reviewer or approver requests a revision or reopens the billing.";
                return RedirectToAction(nameof(Review), new { id });
            }

            var isReopenedOrFinalized = billing.UnpostedDate.HasValue
                || billing.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
            if (!isReopenedOrFinalized
                && await unitOfWork.PostedPeriod.IsMonthClosedAsync(billing.Date.Year, billing.Date.Month, cancellationToken))
            {
                TempData["error"] = $"Cannot modify: {billing.Date:MMMM yyyy} is closed.";
                return RedirectToAction(nameof(Review), new { id });
            }

            billing = await billingService.PopulateTicketListsAsync(billing, cancellationToken);
            static bool IsSubconTicket(DispatchTicket t)
            {
                return t.Tugboat is { IsCompanyOwned: false, TugboatOwnerId: not null };
            }

            var paidTickets = (billing.PaidDispatchTickets ?? [])
                .DistinctBy(t => t.DispatchTicketId)
                .OrderBy(t => t.DateLeft)
                .ThenBy(t => t.DispatchTicketId)
                .ToList();
            var tickets = paidTickets
                .Select(t => new BillingAdjustTicketItemViewModel
                {
                    DispatchTicketId = t.DispatchTicketId,
                    DispatchNumber = t.DispatchNumber,
                    TugboatName = t.Tugboat?.TugboatName ?? "-",
                    ServiceName = MsapConstants.FormatServiceDisplay(t.Service?.ServiceNumber, t.Service?.ServiceName ?? "-", t.Tons),
                    TotalHours = t.TotalHours,
                    EffectiveHours = Math.Max(t.RawDurationHours ?? t.TotalHours, 1m),
                    DispatchChargeType = t.DispatchChargeType ?? "Per hour",
                    DispatchRate = t.DispatchRate,
                    BAFChargeType = t.BAFChargeType ?? "Per hour",
                    BAFRate = t.BAFRate,
                    IsSubcon = IsSubconTicket(t),
                    SubconCost = t.OtherTugChargeLine?.Amount
                })
                .ToList();

            var eligibleGross = paidTickets
                .Where(t => billing.IncludeSubconInDiscount || !IsSubconTicket(t))
                .Sum(t => (billing.DiscountOnTariff ? t.DispatchBillingAmount : 0m)
                    + (billing.DiscountOnBaf ? t.BAFBillingAmount : 0m));
            var totalDiscount = billing.Discount + (billing.BasicBilling?.Discount ?? billing.BafBilling?.Discount ?? 0m);

            var schedule = billing.CustomerOrderSlipId.HasValue
                ? await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == billing.CustomerOrderSlipId.Value, cancellationToken)
                : null;
            var cosDefaultDiscount = schedule?.CosDiscountPercent ?? 0m;
            var currentDiscountPercent = schedule?.CosDiscountPercent
                ?? (totalDiscount > 0m && eligibleGross > 0m
                    ? Math.Round((totalDiscount / eligibleGross) * 100m, 2)
                    : cosDefaultDiscount);
            var hasNoAppliedDiscount = totalDiscount <= 0m
                && billing.FlatDiscountTariff <= 0m
                && billing.FlatDiscountBaf <= 0m;
            var skipCosDiscount = schedule != null
                ? schedule.SkipCosDiscount || (eligibleGross > 0m && schedule.CosDiscountPercent > 0m && hasNoAppliedDiscount)
                : eligibleGross > 0m && hasNoAppliedDiscount;

            var vm = new BillingAdjustViewModel
            {
                MsapBillingId = billing.MsapBillingId,
                MsapBillingNumber = billing.MsapBillingNumber,
                COSNumber = billing.CustomerOrderSlip?.COSNumber,
                CustomerName = billing.Customer?.CustomerName,
                VesselName = billing.Vessel?.VesselName,
                RejectionReason = billing.RejectionReason,
                CosDefaultDiscountPercent = cosDefaultDiscount,
                CosDiscountPercent = currentDiscountPercent,
                SkipCosDiscount = skipCosDiscount,
                DiscountOnTariff = billing.DiscountOnTariff,
                DiscountOnBaf = billing.DiscountOnBaf,
                DiscountType = string.Equals(billing.DiscountType, "Flat", StringComparison.OrdinalIgnoreCase) ? "Flat" : "Percentage",
                FlatDiscountTariff = billing.FlatDiscountTariff,
                FlatDiscountBaf = billing.FlatDiscountBaf,
                IncludeSubconInDiscount = billing.IncludeSubconInDiscount,
                AgencyIncentiveRepresentativeId = billing.AgencyIncentiveRepresentativeId,
                AgencyIncentiveMode = billing.AgencyIncentiveMode ?? CommissionCalculator.ModePercentage,
                AgencyIncentiveBase = billing.AgencyIncentiveBase ?? CommissionCalculator.BaseNetOfDiscount,
                AgencyIncentiveRate = billing.AgencyIncentiveRate,
                AgentCommissionRepresentativeId = billing.AgentCommissionRepresentativeId,
                AgentCommissionMode = billing.AgentCommissionMode ?? CommissionCalculator.ModePercentage,
                AgentCommissionBase = billing.AgentCommissionBase ?? CommissionCalculator.BaseNetOfDiscount,
                AgentCommissionRate = billing.AgentCommissionRate,
                Tickets = tickets
            };

            var reps = (await unitOfWork.Representative.GetAllAsync(
                r => r.IsActive && (r.CustomerId == null || r.CustomerId == billing.CustomerId),
                cancellationToken)).OrderBy(r => r.RepresentativeName).ToList();
            ViewBag.AgencyIncentiveReps = reps.Where(r => r.Category == CommissionCalculator.CategoryAgencyIncentive).ToList();
            ViewBag.AgentCommissionReps = reps.Where(r => r.Category == CommissionCalculator.CategoryAgentCommission).ToList();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<IActionResult> Adjust(BillingAdjustViewModel model, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var username = User.Identity?.Name ?? "System";
            var result = await billingService.AdjustBillingAsync(model, username, userId, cancellationToken);
            if (!result.IsSuccess)
            {
                TempData["error"] = result.Message;
                return RedirectToAction(nameof(Adjust), new { id = model.MsapBillingId });
            }

            TempData["success"] = result.Message;
            return RedirectToAction(nameof(Review), new { id = model.MsapBillingId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveFinalBilling)]
        [ActionName("Review")]
        public async Task<IActionResult> ReviewConfirmed(int id, int reviewedRevision, CancellationToken cancellationToken)
        {
            var result = await billingService.PostBillingAsync(id, User.Identity?.Name ?? "System", cancellationToken, User.FindFirstValue(ClaimTypes.NameIdentifier)!, reviewedRevision);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Review), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ReverseBilling)]
        public async Task<IActionResult> ReopenForRevision(int id, string reason, IFormFile? authorizationFile, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var username = User.Identity?.Name ?? "System";
            var result = await billingService.ReopenBillingForRevisionAsync(id, reason, username, userId, cancellationToken, authorizationFile);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Review), new { id });
        }

        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.ViewBillings, ProcedureEnum.ReviewBilling, ProcedureEnum.ApproveFinalBilling)]
        public async Task<IActionResult> DownloadAdjustmentAttachment(int id, CancellationToken cancellationToken)
        {
            var billing = await billingService.GetBillingByIdAsync(id, cancellationToken);
            if (billing == null)
            {
                return NotFound();
            }

            var objectName = billing.AdjustmentAttachmentUrl;
            if (string.IsNullOrWhiteSpace(objectName))
            {
                var logs = await unitOfWork.MsapAdjustmentLog.GetAllAsync(
                    a => a.DocumentType == "Billing" && a.RecordId == id && !string.IsNullOrEmpty(a.AttachmentUrl), cancellationToken);
                objectName = logs.OrderByDescending(a => a.EditedDate).ThenByDescending(a => a.Id).FirstOrDefault()?.AttachmentUrl;
            }

            if (string.IsNullOrWhiteSpace(objectName))
            {
                return NotFound("No authorization document is attached to this billing.");
            }

            var stream = await cloudStorageService.DownloadFileAsync(objectName);
            var extension = Path.GetExtension(objectName).ToLowerInvariant();
            var contentType = extension switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };

            return File(stream, contentType, objectName);
        }

        #endregion

        #region Preview & Print

        /// <summary>
        /// Displays a preview of the Billing, including associated tickets and tugboats.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewBillings)]
        public async Task<IActionResult> Preview(int id, CancellationToken cancellationToken, bool includeTicketImages = false, bool combineMatchingBafRates = false, bool printCopy = false)
        {
            var model = await billingService.GetBillingByIdAsync(id, cancellationToken);
            if (model == null)
            {
                return NotFound();
            }
            if (printCopy && !BillingService.CanPrint(model))
            {
                return Forbid();
            }
            if (model.BasicBillingId.HasValue)
            {
                model = await billingService.GetBillingByIdAsync(model.BasicBillingId.Value, cancellationToken);
                if (model == null)
                {
                    return NotFound();
                }
            }
            model = await billingService.PopulateTicketListsAsync(model, cancellationToken);
            var statements = new List<Billing>();
            if (!printCopy || BillingService.CanPrint(model))
            {
                statements.Add(model);
            }
            if (model.BafBilling != null)
            {
                var baf = await billingService.GetBillingByIdAsync(model.BafBilling.MsapBillingId, cancellationToken);
                if (baf != null && (!printCopy || BillingService.CanPrint(baf)))
                {
                    statements.Add(await billingService.PopulateTicketListsAsync(baf, cancellationToken));
                }
            }
            if (printCopy)
            {
                model = statements[0];
            }
            await PrepareStatementImagesAsync(model, true);
            ViewData["Statements"] = statements;
            ViewData["CanPrint"] = statements.All(BillingService.CanPrint);
            ViewData["ReviewBillingId"] = id;
            ViewData["IncludeTicketImages"] = includeTicketImages;
            ViewData["CombineMatchingBafRates"] = combineMatchingBafRates;
            ViewBag.JobProgressItems = await JobProgressCalculator.LoadForBillingsAsync(unitOfWork, [model.MsapBillingId], cancellationToken);
            return View(model);
        }

        private async Task PrepareStatementImagesAsync(Billing model, bool includeImages)
        {
            if (!includeImages || model.PaidDispatchTickets == null)
            {
                return;
            }
            foreach (var ticket in model.PaidDispatchTickets.Where(t => !string.IsNullOrEmpty(t.ImageName)))
            {
                try
                {
                    ticket.ImageSignedUrl = await cloudStorageService.GetSignedUrlAsync(ticket.ImageName!);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to get signed URL for ticket {TicketId}", ticket.DispatchTicketId);
                }
            }
        }

        /// <summary>
        /// Generates an Excel file for dot-matrix printing of the Billing.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewBillings)]
        public async Task<IActionResult> Print(int id, CancellationToken cancellationToken)
        {
            try
            {
                var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                if (billing == null)
                {
                    return NotFound();
                }

                if (!BillingService.CanPrint(billing))
                {
                    return Forbid();
                }

                billing.PaidDispatchTickets = await unitOfWork.Billing.GetPaidDispatchTicketsAsync(billing.MsapBillingId, cancellationToken);
                billing.UniqueTugboats = await unitOfWork.Billing.GetUniqueTugboatsListAsync(billing.MsapBillingId, cancellationToken);

                var printDiscount = billing.PrintDiscount;

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add($"Billing #{billing.MsapBillingNumber}");
                worksheet.Cells.Style.Font.Name = "Calibri";
                worksheet.Cells["B2"].Value = billing.Principal?.PrincipalName ?? billing.Customer?.CustomerName ?? string.Empty;
                worksheet.Cells["E2"].Value = $"{billing.Date}";
                worksheet.Cells["E2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                worksheet.Cells["B3"].Value = $"{(billing.Principal?.Address ?? billing.Customer?.CustomerAddress ?? string.Empty)} TERMS: {billing.Terms}";
                worksheet.Cells["B4"].Value = billing.Principal?.TIN ?? billing.Customer?.CustomerTin ?? string.Empty;
                worksheet.Cells["E4"].Value = $"VOYAGE NO. {billing.VoyageNumber}";
                worksheet.Cells["B6"].Value = $"FOR THE SERVICE RE: {billing.Vessel.VesselName}";
                worksheet.Cells["B7"].Value = $"LOCATION PORT: {billing.Port.PortName}";

                var row = 9;
                if (billing.BasicBillingId == null && billing.UniqueTugboats != null)
                {
                    foreach (var tugboat in billing.UniqueTugboats)
                    {
                        worksheet.Cells[row, 2].Value = $"NAME OF TUGBOAT: {tugboat}";
                        row++;

                        foreach (var ticket in billing.PaidDispatchTickets!.Where(t => t.Tugboat.TugboatName == tugboat))
                        {
                            var hasTicketDiscount = ticket.DispatchDiscount > 0 && ticket.DispatchBillingAmount > ticket.DispatchNetRevenue;
                            var unitCount = MsapConstants.GetEffectiveUnitCount(ticket.Service?.ServiceNumber, ticket.Tons, ticket.TotalHours);
                            var effectiveRate = unitCount > 0 ? Math.Round(ticket.DispatchNetRevenue / unitCount, 2) : ticket.DispatchRate;

                            worksheet.Cells[row, 1].Value = "1";
                            worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            worksheet.Cells[row, 2].Value = $"{MsapConstants.FormatServiceDisplay(ticket.Service?.ServiceNumber, ticket.Service?.ServiceName, ticket.Tons)}          {ticket.DateLeft} {ticket.TimeLeft}          {ticket.DateArrived} {ticket.TimeArrived}";
                            worksheet.Cells[row, 4].Value = printDiscount ? ticket.DispatchRate : effectiveRate;
                            worksheet.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
                            worksheet.Cells[row, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            worksheet.Cells[row, 5].Value = printDiscount && hasTicketDiscount ? ticket.DispatchBillingAmount : ticket.DispatchNetRevenue;
                            worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                            worksheet.Cells[row, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            row++;

                            if (printDiscount && hasTicketDiscount)
                            {
                                worksheet.Cells[row, 2].Value = "    Less: Customer Discount";
                                worksheet.Cells[row, 5].Value = -(ticket.DispatchBillingAmount - ticket.DispatchNetRevenue);
                                worksheet.Cells[row, 5].Style.Numberformat.Format = "(#,##0.00)";
                                worksheet.Cells[row, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                                row++;
                            }
                        }
                        row++;
                    }
                }

                if (billing.BafBilling == null && billing.PaidDispatchTickets != null)
                {
                    foreach (var ticket in billing.PaidDispatchTickets.Where(t => t.BAFNetRevenue != 0))
                    {
                        var hasBafDiscount = ticket.BAFDiscount > 0 && ticket.BAFBillingAmount > ticket.BAFNetRevenue;
                        var effectiveBafRate = ticket.TotalHours > 0 ? Math.Round(ticket.BAFNetRevenue / ticket.TotalHours, 2) : ticket.BAFRate;

                        worksheet.Cells[row, 2].Value = "NAME OF TUGBOAT: BUNKER ADJUSTMENT FACTOR";
                        row++;
                        worksheet.Cells[row, 1].Value = "1";
                        worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        worksheet.Cells[row, 2].Value = $"{ticket.Service.ServiceName}          {ticket.DateLeft} {ticket.TimeLeft}          {ticket.DateArrived} {ticket.TimeArrived}";
                        worksheet.Cells[row, 4].Value = printDiscount ? ticket.BAFRate : effectiveBafRate;
                        worksheet.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
                        worksheet.Cells[row, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        worksheet.Cells[row, 5].Value = printDiscount && hasBafDiscount ? ticket.BAFBillingAmount : ticket.BAFNetRevenue;
                        worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                        worksheet.Cells[row, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        row++;

                        if (printDiscount && hasBafDiscount)
                        {
                            worksheet.Cells[row, 2].Value = "    Less: BAF Discount";
                            worksheet.Cells[row, 5].Value = -(ticket.BAFBillingAmount - ticket.BAFNetRevenue);
                            worksheet.Cells[row, 5].Style.Numberformat.Format = "(#,##0.00)";
                            worksheet.Cells[row, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            row++;
                        }
                    }
                }

                row++;

                var subTotal = billing.Amount;
                var vatAmount = 0m;
                var whtBase = billing.IsVatable ? Math.Round(subTotal / 1.12m, 2) : subTotal;

                if (billing.IsVatable)
                {
                    vatAmount = Math.Round(subTotal - whtBase, 2);
                }

                if (printDiscount)
                {
                    var totalGross = billing.BasicBillingId == null
                        ? (billing.PaidDispatchTickets?.Sum(t => t.DispatchBillingAmount + (billing.BafBilling == null ? t.BAFBillingAmount : 0)) ?? billing.Amount)
                        : (billing.PaidDispatchTickets?.Sum(t => t.BAFBillingAmount) ?? billing.Amount);
                    var totalDiscount = totalGross - billing.Amount;
                    if (totalDiscount > 0)
                    {
                        worksheet.Cells[row, 4].Value = "GROSS SALES";
                        worksheet.Cells[row, 5].Value = totalGross;
                        worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                        row++;

                        worksheet.Cells[row, 4].Value = "LESS: DISCOUNT";
                        worksheet.Cells[row, 5].Value = -totalDiscount;
                        worksheet.Cells[row, 5].Style.Numberformat.Format = "(#,##0.00)";
                        row++;
                    }
                }

                worksheet.Cells[row, 4].Value = "TOTAL SALES";
                worksheet.Cells[row, 5].Value = subTotal;
                worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                row++;

                if (billing.IsVatable)
                {
                    worksheet.Cells[row, 4].Value = "12% VAT";
                    worksheet.Cells[row, 5].Value = vatAmount;
                    worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                    row++;
                }

                if (billing.PrintWht)
                {
                    var whtAmount = Math.Round(whtBase * 0.02m, 2);
                    worksheet.Cells[row, 4].Value = "LESS 2% WHT";
                    worksheet.Cells[row, 5].Value = whtAmount;
                    worksheet.Cells[row, 5].Style.Numberformat.Format = "(#,##0.00)";
                    row++;

                    // 5% WVAT is removed from invoice statement per BIR & meeting rule (settled at collection via 2306)

                    worksheet.Cells[row, 4].Value = "NET AMOUNT DUE";
                    worksheet.Cells[row, 5].Value = Math.Round(subTotal - whtAmount, 2);
                    worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 5].Style.Font.Bold = true;
                }
                else
                {
                    worksheet.Cells[row, 4].Value = "TOTAL AMOUNT DUE";
                    worksheet.Cells[row, 5].Value = subTotal;
                    worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                    worksheet.Cells[row, 5].Style.Font.Bold = true;
                }

                worksheet.Cells[1, 1, row, 7].Style.Font.Name = "Calibri";
                worksheet.Column(1).Width = 8;
                worksheet.Column(2).Width = 53;
                worksheet.Column(3).Width = 9;
                worksheet.Column(4).Width = 8.5;
                worksheet.Column(5).Width = 16;

                var bytes = await package.GetAsByteArrayAsync(cancellationToken);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DotMatrix_{DateTimeHelper.GetCurrentPhilippineTime():yyyyddMMHHmmss}.xlsx");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to print billing");
                TempData["error"] = ExceptionHelper.GetErrorMessage(ex);
                return RedirectToAction(nameof(Index));
            }
        }

        #endregion

        #region AJAX Endpoints

        /// <summary>
        /// Retrieves a paged and filtered list of Billings for DataTables.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ViewBillings)]
        public async Task<IActionResult> GetBillingList([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            try
            {
                var (data, filtered, total) = await billingService.GetPagedBillingsAsync(parameters, cancellationToken, customerOrderSlipId);

                var closedMonths = (await unitOfWork.PostedPeriod.GetAllAsync(cancellationToken))
                    .Where(p => MsapConstants.EnforceClosedPeriods && p.IsClosed)
                    .Select(p => (p.Year, p.Month))
                    .ToHashSet();
                foreach (var item in data)
                {
                    item.IsMonthClosed = closedMonths.Contains((item.Date.Year, item.Date.Month));
                    item.CanDiscard &= item.PeriodStatus == null;
                }

                return Json(new
                {
                    draw = parameters.Draw,
                    recordsTotal = total,
                    recordsFiltered = filtered,
                    data
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get billings");
                return Json(new { draw = parameters.Draw, recordsTotal = 0, recordsFiltered = 0, data = Array.Empty<object>(), error = "An error occurred while loading the billing list." });
            }
        }

        /// <summary>
        /// Searches for customers matching a search term.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<JsonResult> SearchCustomers(string? term, CancellationToken cancellationToken)
        {
            var result = await unitOfWork.Customer.SearchCustomersDtoAsync(term ?? string.Empty, 10, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Gets full customer details by ID (used after SSR modern-select selection).
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<JsonResult> GetCustomerDetail(int customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetCustomerDetailsAsync(customerId, cancellationToken);
            return !result.IsSuccess ? Json(new { success = false, message = result.Message }) : Json(result.Data);
        }

        /// <summary>
        /// Gets all principals for a customer (used to populate principal modern-select).
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<JsonResult> GetPrincipalsByCustomer(int customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetPrincipalsByCustomerAsync(customerId, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Gets all billable COS records for a customer (used to populate JO modern-select).
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<JsonResult> GetRepresentativesByCustomer(int customerId, int? principalId, CancellationToken cancellationToken)
        {
            var reps = (await unitOfWork.Representative.GetAllAsync(
                r => r.IsActive && (r.CustomerId == null || r.CustomerId == customerId),
                cancellationToken))
                .OrderByDescending(r => r.PrincipalId == principalId && principalId.HasValue)
                .ThenByDescending(r => r.CustomerId == customerId)
                .ThenBy(r => r.RepresentativeName)
                .Select(r => new
                {
                    representativeId = r.RepresentativeId,
                    representativeName = r.RepresentativeName,
                    category = r.Category,
                    customerId = r.CustomerId,
                    principalId = r.PrincipalId,
                    rateMode = r.RateMode,
                    rateValue = r.RateValue,
                    calculationBase = r.CalculationBase
                })
                .ToList();
            return Json(reps);
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<JsonResult> GetBillableCustomerOrderSlips(int customerId, int? currentCustomerOrderSlipId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetBillableCustomerOrderSlipsAsync(customerId, cancellationToken, currentCustomerOrderSlipId);
            return Json(result);
        }

        /// <summary>
        /// Retrieves unbilled 'For Billing' tickets associated with a specific COS.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling)]
        public async Task<JsonResult> GetDispatchTicketsByCustomerOrderSlip(int customerOrderSlipId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetDispatchTicketsByCustomerOrderSlipAsync(customerOrderSlipId, cancellationToken);

            if (result is { IsSuccess: true, Data: not null })
            {
                return Json(new
                {
                    success = true,
                    header = result.Data.Header,
                    tickets = result.Data.Tickets
                });
            }

            return Json(new { success = false, message = result.Message });
        }

        #endregion

    }
}
