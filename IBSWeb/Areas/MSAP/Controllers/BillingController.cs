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
            ViewBag.CanEdit = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditBilling);
            ViewBag.CanDelete = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.DeleteBilling);
            return View(Enumerable.Empty<Billing>());
        }

        #endregion

        #region Create

        /// <summary>
        /// Displays the form to create a new Billing.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateBilling, "Access denied. You don't have permission to create Billings.")]
        public async Task<IActionResult> Create(CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            var model = new Billing();
            if (customerOrderSlipId.HasValue)
            {
                var job = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == customerOrderSlipId.Value, cancellationToken);
                if (job == null)
                {
                    return NotFound();
                }
                model.CustomerId = job.CustomerId;
                model.CustomerOrderSlipId = job.CustomerOrderSlipId;
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
        public async Task<IActionResult> Create([Bind("CustomerId,PrincipalId,BilledTo,MsapBillingNumber,Date,CustomerOrderSlipId,SeparateBaf,BafBillingNumber,IsUndocumented")] Billing model, CancellationToken cancellationToken)
        {
            try
            {
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
            [Bind("CustomerId,CustomerOrderSlipId,PrincipalId,BilledTo,MsapBillingNumber,Date,SeparateBaf,BafBillingNumber,IsUndocumented")] Billing model,
            CancellationToken cancellationToken)
        {
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

        #region Edit

        /// <summary>
        /// Displays the form to edit an existing Billing.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.EditBilling, "Access denied. You don't have permission to edit Billings.")]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var model = await billingService.GetBillingByIdAsync(id, cancellationToken);
            if (model == null)
            {
                return NotFound();
            }

            if (model.BasicBillingId.HasValue || model.BafBilling != null)
            {
                TempData["error"] = "Split billings must be corrected together. Delete the unposted preparation and prepare it again.";
                return RedirectToAction(nameof(Preview), new { id });
            }
            if (model.Status != MsapConstants.BillingStatus.ForPosting)
            {
                TempData["error"] = "Only billings with 'For Posting' status can be edited.";
                return RedirectToAction(nameof(Index));
            }

            BillingService.ApplyCustomerTaxSettings(model);
            model = await billingService.PopulateBillingSelectListsAsync(model, cancellationToken);
            model.UnbilledDispatchTickets = await billingService.GetEditTicketsSelectListAsync(model.CustomerId, model.MsapBillingId, cancellationToken);

            if (model.CustomerId != 0)
            {
                model.CustomerPrincipal = await billingService.GetPrincipalsSelectListAsync(model.CustomerId, cancellationToken);
            }

            model = await billingService.PopulateTicketListsAsync(model, cancellationToken);

            if (model.CustomerOrderSlipId.HasValue)
            {
                var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetAsync(jo => jo.CustomerOrderSlipId == model.CustomerOrderSlipId.Value, cancellationToken);
                ViewData["CurrentCustomerOrderSlipNumber"] = customerOrderSlip?.CustomerOrderSlipNumber;
            }

            ViewData["HasPrincipal"] = model.CustomerPrincipal is { Count: > 0 };

            ViewData["CustomerAddress"] = model.Customer.CustomerAddress;
            ViewData["CustomerTIN"] = model.Customer.CustomerTin;
            ViewData["CustomerTerms"] = model.Customer.CustomerTerms;
            ViewData["CustomerBusinessStyle"] = model.Customer.BusinessStyle ?? "-";
            ViewData["CustomerVatType"] = model.Customer.VatType;
            ViewData["CustomerType"] = model.Customer.Type;
            ViewData["CustomerWht"] = model.Customer.WithHoldingTax;

            ViewBag.JobProgressItems = await JobProgressCalculator.LoadForBillingsAsync(unitOfWork, [model.MsapBillingId], cancellationToken);
            return View(model);
        }

        /// <summary>
        /// Processes the update of an existing Billing, including ticket reallocation.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditBilling, "Access denied. You don't have permission to edit Billings.")]
        public async Task<IActionResult> Edit([Bind("MsapBillingId,Date,IsUndocumented,BilledTo,VoyageNumber,COSNumber,CustomerId,PrincipalId,VesselId,PortId,TerminalId,ToBillDispatchTickets,CustomerOrderSlipId,IsVatable,IsVatInclusive,PrintWht,BafRates")] Billing model, IFormFile? file, CancellationToken cancellationToken)
        {
            try
            {
                var result = await billingService.UpdateBillingAsync(model, User.Identity?.Name ?? "System", cancellationToken);

                if (result.IsSuccess)
                {
                    return Json(new { success = true, message = result.Message ?? "Entry edited successfully!", redirectUrl = Url.Action(nameof(Review), new { id = model.MsapBillingId }) });
                }

                if (result.Status == ServiceResultStatus.NotFound)
                {
                    return Json(new { success = false, message = "Billing not found." });
                }

                return Json(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to edit billing");
                return Json(new { success = false, message = ExceptionHelper.GetErrorMessage(ex) });
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// Deletes a specific Billing.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.DeleteBilling, "Access denied. You don't have permission to delete Billings.")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            List<JobProgressViewModel> progress = await JobProgressCalculator.LoadForBillingsAsync(unitOfWork, [id], cancellationToken);
            var result = await billingService.DeleteBillingAsync(id, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
            }
            else
            {
                TempData["error"] = result.Message;
            }

            return RedirectToAction(nameof(Index), new { customerOrderSlipId = progress.Count == 1 ? progress[0].CustomerOrderSlipId : null });
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
            ViewData["CanApproveFinalBilling"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveFinalBilling);
            ViewData["PostingBlocker"] = await JobProgressCalculator.GetBillingBlockerAsync(unitOfWork, model, JobProgressCalculator.PostingStage, cancellationToken);
            ViewData["IsPeriodClosed"] = await unitOfWork.PostedPeriod.IsMonthClosedAsync(model.Date.Year, model.Date.Month, cancellationToken);
            return View(model);
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

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add($"Billing #{billing.MsapBillingNumber}");
                worksheet.Cells.Style.Font.Name = "Calibri";
                worksheet.Cells["B2"].Value = billing.Principal?.PrincipalName ?? billing.Customer.CustomerName;
                worksheet.Cells["E2"].Value = $"{billing.Date}";
                worksheet.Cells["E2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                worksheet.Cells["B3"].Value = $"{billing.Principal?.Address1 ?? billing.Customer.CustomerAddress} TERMS: {billing.Terms}";
                worksheet.Cells["B4"].Value = billing.Principal?.TIN ?? billing.Customer.CustomerTin;
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
                            worksheet.Cells[row, 1].Value = "1";
                            worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            worksheet.Cells[row, 2].Value = $"{ticket.Service.ServiceName}          {ticket.DateLeft} {ticket.TimeLeft}          {ticket.DateArrived} {ticket.TimeArrived}";
                            worksheet.Cells[row, 4].Value = $"{ticket.DispatchRate}";
                            worksheet.Cells[row, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            worksheet.Cells[row, 5].Value = $"{ticket.DispatchBillingAmount}";
                            worksheet.Cells[row, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            row++;
                        }
                        row++;
                    }
                }

                if (billing.BafBilling == null && billing.PaidDispatchTickets != null)
                {
                    foreach (var ticket in billing.PaidDispatchTickets.Where(t => t.BAFNetRevenue != 0))
                    {
                        worksheet.Cells[row, 2].Value = "NAME OF TUGBOAT: BUNKER ADJUSTMENT FACTOR";
                        row++;
                        worksheet.Cells[row, 1].Value = "1";
                        worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        worksheet.Cells[row, 2].Value = $"{ticket.Service.ServiceName}          {ticket.DateLeft} {ticket.TimeLeft}          {ticket.DateArrived} {ticket.TimeArrived}";
                        worksheet.Cells[row, 4].Value = $"{ticket.BAFRate}";
                        worksheet.Cells[row, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        worksheet.Cells[row, 5].Value = $"{ticket.BAFNetRevenue}";
                        worksheet.Cells[row, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        row++;
                    }
                }

                row++;

                var subTotal = billing.Amount;
                var vatAmount = 0m;
                var vatableSales = 0m;

                if (billing.IsVatable)
                {
                    vatableSales = subTotal / 1.12m;
                    vatAmount = subTotal - vatableSales;
                }

                worksheet.Cells[row, 4].Value = "SUBTOTAL";
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
                    var whtAmount = vatableSales * 0.02m;
                    worksheet.Cells[row, 4].Value = "LESS 2% WHT";
                    worksheet.Cells[row, 5].Value = whtAmount;
                    worksheet.Cells[row, 5].Style.Numberformat.Format = "(#,##0.00)";
                    row++;

                    decimal wvatAmount = 0;
                    if (billing.Customer.WithHoldingVat)
                    {
                        wvatAmount = vatableSales * 0.05m;
                        worksheet.Cells[row, 4].Value = "LESS 5% WVAT";
                        worksheet.Cells[row, 5].Value = wvatAmount;
                        worksheet.Cells[row, 5].Style.Numberformat.Format = "(#,##0.00)";
                        row++;
                    }

                    worksheet.Cells[row, 4].Value = "NET AMOUNT DUE";
                    worksheet.Cells[row, 5].Value = subTotal - whtAmount - wvatAmount;
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
        /// Retrieves detailed information for a list of Dispatch Tickets.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<IActionResult> GetDispatchTickets(List<string> dispatchTicketIds, CancellationToken cancellationToken)
        {
            try
            {
                var intDispatchTicketIds = dispatchTicketIds.Select(int.Parse).ToList();
                var dispatchTickets = await billingService.GetDispatchTicketsByIdsAsync(intDispatchTicketIds, cancellationToken);

                return Json(new { success = true, data = dispatchTickets });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get dispatch tickets");
                return Json(new { success = false, message = ex.Message });
            }
        }

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
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<JsonResult> SearchCustomers(string? term, CancellationToken cancellationToken)
        {
            var result = await unitOfWork.Customer.SearchCustomersDtoAsync(term ?? string.Empty, 10, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Gets full customer details by ID (used after SSR modern-select selection).
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<JsonResult> GetCustomerDetail(int customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetCustomerDetailsAsync(customerId, cancellationToken);
            return !result.IsSuccess ? Json(new { success = false, message = result.Message }) : Json(result.Data);
        }

        /// <summary>
        /// Gets all principals for a customer (used to populate principal modern-select).
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<JsonResult> GetPrincipalsByCustomer(int customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetPrincipalsByCustomerAsync(customerId, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Searches for principals associated with a specific customer.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<JsonResult> SearchPrincipals(string? term, int customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.SearchPrincipalsAsync(term, customerId, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Gets all billable COS records for a customer (used to populate JO modern-select).
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<JsonResult> GetBillableCustomerOrderSlips(int customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetBillableCustomerOrderSlipsAsync(customerId, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Retrieves unbilled 'For Billing' tickets associated with a specific COS.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
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

        /// <summary>
        /// Retrieves principals for a customer as a JSON list.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<IActionResult> GetPrincipalsJson(string customerId, CancellationToken cancellationToken)
        {
            var principalsList = await billingService.GetPrincipalsSelectListAsync(int.Parse(customerId), cancellationToken);
            return Json(principalsList);
        }

        /// <summary>
        /// Retrieves unbilled tickets for a customer that have no COS.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<IActionResult> GetDispatchTicketsByCustomer(string customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetDispatchTicketsByCustomerAsync(int.Parse(customerId), cancellationToken);

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

        /// <summary>
        /// Retrieves detailed information for a specific customer.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<IActionResult> GetCustomerDetails(int customerId, CancellationToken cancellationToken)
        {
            var result = await billingService.GetCustomerDetailsAsync(customerId, cancellationToken);
            if (result.IsSuccess)
            {
                return Json(result.Data);
            }

            return result.Status == ServiceResultStatus.NotFound ? NotFound() : BadRequest(result.Message);
        }

        /// <summary>
        /// Checks whether a billing number already exists (used for client-side duplicate validation).
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling)]
        public async Task<IActionResult> CheckBillingNumber(string number, CancellationToken cancellationToken)
        {
            var exists = await unitOfWork.Billing.GetAsync(b => b.MsapBillingNumber == number, cancellationToken) != null;
            return Json(new { exists });
        }

        #endregion


    }
}
