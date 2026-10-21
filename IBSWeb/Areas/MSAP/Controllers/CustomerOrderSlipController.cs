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

namespace IBSWeb.Areas.MSAP.Controllers
{
    /// <summary>
    /// Controller for managing COS records in the MMSI system.
    /// </summary>
    [Area("MSAP")]
    [Route("MSAP/COS/[action]/{id?}", Order = 0)]
    public class CustomerOrderSlipController(
        IUnitOfWork unitOfWork,
        CustomerOrderSlipService customerOrderSlipService,
        DispatchTicketService dispatchTicketService,
        OtherTugChargeService otherTugCharges,
        ITerminalService terminalService,
        ICloudStorageService cloudStorageService,
        IAccessControlService accessControl,
        ILogger<CustomerOrderSlipController> logger) : Controller
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> CancelService(int id, int entryId, string? reason, CancellationToken ct)
        {
            var result = await customerOrderSlipService.CancelServiceAsync(id, entryId, reason,
                User.Identity?.Name ?? "System", User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        #region Index

        /// <summary>
        /// Displays the list of all COS records.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanEdit = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditCustomerOrderSlip);
            ViewBag.CanCreate = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateCustomerOrderSlip);
            return View();
        }

        /// <summary>
        /// Returns a paged, filtered, sorted list of COS records for DataTables server-side processing.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetCustomerOrderSlipList([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var (data, filtered, total) = await customerOrderSlipService.GetPagedCustomerOrderSlipsAsync(parameters, cancellationToken);

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
                logger.LogError(ex, "Failed to get COS records");
                return Json(new { draw = parameters.Draw, error = ex.Message });
            }
        }

        #endregion

        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateCustomerOrderSlip)]
        public IActionResult Create()
        {
            return RedirectToAction("Create", "VesselSchedule");
        }


        #region Details

        /// <summary>
        /// Displays the details of a specific COS.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken, int? expectedServiceEntryId = null, int? dispatchTicketId = null, bool editTicket = false)
        {
            var customerOrderSlip = await customerOrderSlipService.GetCustomerOrderSlipByIdAsync(id, cancellationToken);
            if (customerOrderSlip == null)
            {
                return NotFound();
            }

            var billing = await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == id, cancellationToken);
            ViewData["HasBilling"] = billing != null;
            ViewBag.HasDispatchTickets = await unitOfWork.DispatchTicket.GetAsync(t => t.CustomerOrderSlipId == id, cancellationToken) != null;
            ViewData["BillingNumber"] = billing?.MsapBillingNumber;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanEditTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditDispatchTicket);
            ViewBag.CanEditCustomerOrderSlip = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditCustomerOrderSlip);
            ViewBag.CanCreateDispatchTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateDispatchTicket);
            ViewData["CanManageOtherTugCharges"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ManageOtherTugCharges);
            ViewData["CanViewOtherTugCharges"] = ViewData["CanManageOtherTugCharges"] is true || userId != null
                && await accessControl.HasAccessAsync(userId, ProcedureEnum.ConfirmOtherTugCharges);
            ViewData["OtherTugCharges"] = await otherTugCharges.GetForCosAsync(id, cancellationToken);
            ViewData["CanViewActualTicket"] = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ViewOperations);
            var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == id, cancellationToken);
            if (schedule != null)
            {
                schedule = await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(schedule.VesselScheduleId, cancellationToken);
            }
            if (expectedServiceEntryId.HasValue)
            {
                if (ViewBag.CanCreateDispatchTicket != true)
                {
                    return Forbid();
                }
                if (schedule?.ServiceEntries.Any(entry => entry.VesselScheduleServiceEntryId == expectedServiceEntryId.Value && !entry.CancelledAt.HasValue) != true
                    || customerOrderSlip.DispatchTickets.Any(ticket => ticket.VesselScheduleServiceEntryId == expectedServiceEntryId.Value))
                {
                    return NotFound();
                }
                if (schedule != null && billing == null && customerOrderSlip.Status == MsapConstants.CustomerOrderSlipStatus.Open
                    && schedule.Status is MsapConstants.VesselScheduleStatus.Tentative or MsapConstants.VesselScheduleStatus.Confirmed or MsapConstants.VesselScheduleStatus.InProgress)
                {
                    ViewBag.ActualTicketEntry = await dispatchTicketService.PopulateDispatchTicketViewModelAsync(
                        new DispatchTicketViewModel { VesselScheduleServiceEntryId = expectedServiceEntryId.Value }, id, cancellationToken);
                }
            }
            ViewBag.VesselSchedule = schedule;
            ViewBag.JobProgress = await JobProgressCalculator.LoadAsync(unitOfWork, customerOrderSlip, schedule, cancellationToken);

            foreach (var ticket in customerOrderSlip.DispatchTickets)
            {
                if (!string.IsNullOrEmpty(ticket.ImageName))
                {
                    try { ticket.ImageSignedUrl = await cloudStorageService.GetSignedUrlAsync(ticket.ImageName); }
                    catch (Exception ex) { logger.LogWarning(ex, "Image file not found: {Name}", ticket.ImageName); }
                }
                if (!string.IsNullOrEmpty(ticket.VideoName))
                {
                    try { ticket.VideoSignedUrl = await cloudStorageService.GetSignedUrlAsync(ticket.VideoName); }
                    catch (Exception ex) { logger.LogWarning(ex, "Video file not found: {Name}", ticket.VideoName); }
                }
            }

            if (dispatchTicketId.HasValue)
            {
                if (ViewData["CanViewActualTicket"] is not true)
                {
                    return Forbid();
                }
                var ticket = customerOrderSlip.DispatchTickets.SingleOrDefault(t => t.DispatchTicketId == dispatchTicketId.Value);
                if (ticket == null || expectedServiceEntryId.HasValue)
                {
                    return NotFound();
                }
                if (editTicket)
                {
                    if (ViewBag.CanEditTicket != true)
                    {
                        return Forbid();
                    }
                    if (billing != null || customerOrderSlip.Status != MsapConstants.CustomerOrderSlipStatus.Open
                        || customerOrderSlip.DispatchTickets.Any(t => t.Status == MsapConstants.DispatchTicketStatus.Billed || t.BillingId.HasValue)
                        || schedule?.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        TempData["error"] = "This ticket cannot be edited in the current booking or billing state.";
                        return RedirectToAction(nameof(Details), new { id, dispatchTicketId });
                    }
                    ViewData["EditActualTicket"] = true;
                }
                ViewData["ViewedTicket"] = ticket;
                var ticketDetails = new DispatchTicketViewModel();
                ticketDetails.FromEntity(ticket);
                ViewBag.ActualTicketEntry = await dispatchTicketService.PopulateDispatchTicketViewModelAsync(ticketDetails, id, cancellationToken);
            }

            return View(customerOrderSlip);
        }

        #endregion

        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Progress(int? customerOrderSlipId, List<int>? billingIds, List<int>? dispatchTicketIds, int? actionStage, CancellationToken cancellationToken)
        {
            ViewData["JobProgressActionStage"] = actionStage;
            if (customerOrderSlipId.HasValue)
            {
                var job = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == customerOrderSlipId.Value, cancellationToken);
                if (job == null)
                {
                    return NotFound();
                }
                var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == job.CustomerOrderSlipId, cancellationToken);
                var progress = await JobProgressCalculator.LoadAsync(unitOfWork, job, schedule, cancellationToken);
                return PartialView("_JobProgressList", new[] { progress });
            }
            if (dispatchTicketIds is { Count: > 0 })
            {
                IEnumerable<DispatchTicket> tickets = await unitOfWork.DispatchTicket.GetAllAsync(t => dispatchTicketIds.Contains(t.DispatchTicketId), cancellationToken);
                IEnumerable<int> jobIds = tickets.Where(t => t.CustomerOrderSlipId.HasValue).Select(t => t.CustomerOrderSlipId!.Value);
                return PartialView("_JobProgressList", await JobProgressCalculator.LoadForJobsAsync(unitOfWork, jobIds, cancellationToken));
            }
            return PartialView("_JobProgressList", await JobProgressCalculator.LoadForBillingsAsync(unitOfWork, billingIds ?? [], cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
        {
            var result = await customerOrderSlipService.CancelCustomerOrderSlipAsync(id, User.Identity?.Name ?? "Unknown", cancellationToken);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> CompleteBooking(int id, CancellationToken cancellationToken)
        {
            var result = await customerOrderSlipService.CompleteBookingAsync(id, User.Identity?.Name ?? "Unknown", cancellationToken);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        #region Edit

        /// <summary>
        /// Displays the form to edit an existing COS.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip, "Access denied. You don't have permission to edit COS records.")]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var customerOrderSlip = await customerOrderSlipService.GetCustomerOrderSlipByIdAsync(id, cancellationToken);
            if (customerOrderSlip == null)
            {
                return NotFound();
            }

            ViewData["CustomerOrderSlipNumber"] = customerOrderSlip.CustomerOrderSlipNumber;

            string? editError = await customerOrderSlipService.GetEditErrorAsync(customerOrderSlip, cancellationToken);
            if (editError != null)
            {
                TempData["error"] = editError;
                return RedirectToAction(nameof(Details), new { id });
            }

            var viewModel = new CustomerOrderSlipViewModel
            {
                CustomerOrderSlipId = customerOrderSlip.CustomerOrderSlipId,
                Date = customerOrderSlip.Date,
                CustomerId = customerOrderSlip.CustomerId,
                VesselId = customerOrderSlip.VesselId,
                PortId = customerOrderSlip.PortId,
                TerminalId = customerOrderSlip.TerminalId,
                COSNumber = customerOrderSlip.COSNumber,
                VoyageNumber = customerOrderSlip.VoyageNumber,
                PlannedStartTime = customerOrderSlip.PlannedStartTime,
                PlannedEndTime = customerOrderSlip.PlannedEndTime,
                Remarks = customerOrderSlip.Remarks
            };

            await customerOrderSlipService.PopulateCustomerOrderSlipViewModelAsync(viewModel, cancellationToken);
            ViewData["HasTickets"] = customerOrderSlip.DispatchTickets.Any();

            return View(viewModel);
        }

        /// <summary>
        /// Processes the update of an existing COS.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip, "Access denied. You don't have permission to edit COS records.")]
        public async Task<IActionResult> Edit(CustomerOrderSlipViewModel viewModel, CancellationToken cancellationToken)
        {
            var customerOrderSlip = MapToEntity(viewModel);

            var result = await customerOrderSlipService.UpdateCustomerOrderSlipAsync(customerOrderSlip, User.Identity?.Name ?? "Unknown", cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = customerOrderSlip.CustomerOrderSlipId });
            }

            if (result.Status == ServiceResultStatus.NotFound)
            {
                return NotFound();
            }

            var jo = await customerOrderSlipService.GetCustomerOrderSlipByIdAsync(viewModel.CustomerOrderSlipId, cancellationToken);
            ViewData["CustomerOrderSlipNumber"] = jo?.CustomerOrderSlipNumber ?? "";
            ViewData["HasTickets"] = jo?.DispatchTickets.Any() ?? false;
            await customerOrderSlipService.PopulateCustomerOrderSlipViewModelAsync(viewModel, cancellationToken);
            TempData["error"] = result.Message ?? "An error occurred.";
            return View(viewModel);
        }

        #endregion

        #region AJAX Endpoints

        /// <summary>
        /// Retrieves terminals associated with a specific port. Used for dynamic dropdowns.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateCustomerOrderSlip, ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> ChangeTerminal(int portId, CancellationToken cancellationToken)
        {
            var list = await terminalService.GetTerminalsByPortAsync(portId, cancellationToken);
            return Json(list);
        }

        /// <summary>
        /// Retrieves detailed information for a specific Dispatch Ticket.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetTicketDetails(int id, CancellationToken cancellationToken)
        {
            var details = await dispatchTicketService.GetTicketDetailsAsync(id, cancellationToken);
            if (details == null)
            {
                return NotFound();
            }

            return Json(details);
        }

        /// <summary>
        /// Searches for customers matching a search term.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess("Access denied.", ProcedureEnum.CreateCustomerOrderSlip, ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<JsonResult> SearchCustomers(string? term, CancellationToken cancellationToken)
        {
            var result = await unitOfWork.Customer.SearchCustomersDtoAsync(term ?? string.Empty, 10, cancellationToken);
            return Json(result);
        }

        /// <summary>
        /// Assigns a preferred tugboat to a COS.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAnyAccess("Access denied.", ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> AssignTugboat(int customerOrderSlipId, int tugboatId, CancellationToken cancellationToken)
        {
            return await HandleTugboatAssignmentAsync(customerOrderSlipId, tugboatId, true, cancellationToken);
        }

        /// <summary>
        /// Unassigns a tugboat from a COS.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAnyAccess("Access denied.", ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> UnassignTugboat(int customerOrderSlipId, int tugboatId, CancellationToken cancellationToken)
        {
            return await HandleTugboatAssignmentAsync(customerOrderSlipId, tugboatId, false, cancellationToken);
        }

        #endregion

        private static CustomerOrderSlip MapToEntity(CustomerOrderSlipViewModel viewModel)
        {
            return new CustomerOrderSlip
            {
                CustomerOrderSlipId = viewModel.CustomerOrderSlipId,
                Date = viewModel.Date,
                CustomerId = viewModel.CustomerId,
                VesselId = viewModel.VesselId,
                PortId = viewModel.PortId,
                TerminalId = viewModel.TerminalId,
                COSNumber = viewModel.COSNumber,
                VoyageNumber = viewModel.VoyageNumber,
                PlannedStartTime = viewModel.PlannedStartTime,
                PlannedEndTime = viewModel.PlannedEndTime,
                Remarks = viewModel.Remarks
            };
        }

        private async Task<IActionResult> HandleTugboatAssignmentAsync(int customerOrderSlipId, int tugboatId, bool isAssign, CancellationToken cancellationToken)
        {
            var result = isAssign
                ? await customerOrderSlipService.AssignTugboatAsync(customerOrderSlipId, tugboatId, User.Identity?.Name ?? "Unknown", cancellationToken)
                : await customerOrderSlipService.UnassignTugboatAsync(customerOrderSlipId, tugboatId, User.Identity?.Name ?? "Unknown", cancellationToken);

            if (result.IsSuccess)
            {
                return Json(new { success = true });
            }

            return Json(new { success = false, message = result.Message });
        }
    }
}
