using System.Security.Claims;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DTOs.MSAP;
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
    /// Controller for managing Dispatch Tickets in the MMSI system.
    /// </summary>
    [Area("MSAP")]
    public class DispatchTicketController(
        IUnitOfWork unitOfWork,
        DispatchTicketService dispatchTicketService,
        ICloudStorageService cloudStorageService,
        IAccessControlService accessControl,
        IRoleService roleService,
        ILogger<DispatchTicketController> logger)
        : Controller
    {
        #region Index

        /// <summary>
        /// Displays the list of Dispatch Tickets.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Index(string filterType)
        {
            ViewBag.FilterType = filterType;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanEditTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditDispatchTicket);
            return View(Enumerable.Empty<DispatchTicket>());
        }

        #endregion

        #region Create

        /// <summary>
        /// Displays the form to create a new Dispatch Ticket.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateDispatchTicket, "Access denied. You don't have permission to create Dispatch Tickets.")]
        public async Task<IActionResult> Create(int customerOrderSlipId, CancellationToken cancellationToken = default, int? expectedServiceEntryId = null)
        {
            var viewModel = await dispatchTicketService.PopulateDispatchTicketViewModelAsync(
                new DispatchTicketViewModel { VesselScheduleServiceEntryId = expectedServiceEntryId }, customerOrderSlipId, cancellationToken);
            if (expectedServiceEntryId.HasValue && (viewModel.ServiceId == 0 || viewModel.TugBoatId == 0))
            {
                return NotFound();
            }
            return View(viewModel);
        }

        /// <summary>
        /// Processes the creation of a new Dispatch Ticket, including file uploads.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateDispatchTicket, "Access denied. You don't have permission to create Dispatch Tickets.")]
        public async Task<IActionResult> Create(
            DispatchTicketViewModel viewModel,
            IFormFile? imageFile,
            IFormFile? videoFile,
            CancellationToken cancellationToken = default, bool inlineEntry = false)
        {
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            string? role = userId is not null ? await roleService.GetUserRoleAsync(userId) : null;
            if ((imageFile == null || imageFile.Length == 0) && role is not (MsapRoles.Admin or MsapRoles.SuperAdmin))
            {
                if (inlineEntry)
                {
                    return Json(new { success = false, message = "An image of the Dispatch Ticket is required." });
                }
                TempData["warning"] = "An image of the Dispatch Ticket is strictly required!";
                viewModel = await dispatchTicketService.PopulateDispatchTicketViewModelAsync(viewModel, null, cancellationToken);
                return View(viewModel);
            }

            if (!ModelState.IsValid)
            {
                if (inlineEntry)
                {
                    return Json(new { success = false, message = "Review the ticket number, dates and required fields before saving." });
                }
                await dispatchTicketService.PopulateDispatchTicketViewModelAsync(viewModel, viewModel.CustomerOrderSlipId, cancellationToken);
                return View(viewModel);
            }
            var result = await dispatchTicketService.CreateDispatchTicketAsync(viewModel, imageFile, videoFile, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                if (inlineEntry)
                {
                    return Json(new { success = true, message = result.Message, redirectUrl = Url.Action("Details", "CustomerOrderSlip", new { id = viewModel.CustomerOrderSlipId, dispatchTicketId = result.Data }) });
                }
                TempData["success"] = result.Message;

                if (viewModel.CustomerOrderSlipId.HasValue)
                {
                    TempData["PromptAddTicket"] = !viewModel.VesselScheduleServiceEntryId.HasValue;
                    if (!viewModel.VesselScheduleServiceEntryId.HasValue)
                    {
                        TempData.Remove("success");
                    }
                    return RedirectToAction("Details", "CustomerOrderSlip", new { id = viewModel.CustomerOrderSlipId });
                }

                return RedirectToAction(nameof(Index));
            }

            if (inlineEntry)
            {
                return Json(new { success = false, message = result.Message });
            }
            TempData["error"] = result.Message;
            viewModel = await dispatchTicketService.PopulateDispatchTicketViewModelAsync(viewModel, null, cancellationToken);
            return View(viewModel);
        }

        #endregion

        #region Preview

        /// <summary>
        /// Displays a preview of a specific Dispatch Ticket, including signed URLs for media.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Preview(int id, CancellationToken cancellationToken = default)
        {
            var model = await dispatchTicketService.GetDispatchTicketByIdAsync(id, cancellationToken);
            if (model == null)
            {
                return NotFound();
            }

            if (model.CustomerOrderSlipId.HasValue)
            {
                return RedirectToAction("Details", "CustomerOrderSlip", new { id = model.CustomerOrderSlipId, dispatchTicketId = id });
            }

            if (!string.IsNullOrEmpty(model.ImageName))
            {
                model.ImageSignedUrl = await cloudStorageService.GetSignedUrlAsync(model.ImageName);
            }

            if (!string.IsNullOrEmpty(model.VideoName))
            {
                model.VideoSignedUrl = await cloudStorageService.GetSignedUrlAsync(model.VideoName);
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanEditTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditDispatchTicket);

            return View(model);
        }

        #endregion

        #region Edit Ticket

        /// <summary>
        /// Displays the form to edit basic information of a Dispatch Ticket.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.EditDispatchTicket, "Access denied. You don't have permission to edit Dispatch Tickets.")]
        public async Task<IActionResult> EditTicket(
            int id, int? customerOrderSlipId, string? filterType, CancellationToken cancellationToken = default)
        {
            ViewBag.FilterType = filterType;

            var model = await dispatchTicketService.GetDispatchTicketByIdAsync(id, cancellationToken);
            if (model == null)
            {
                return NotFound();
            }

            if (model.CustomerOrderSlipId.HasValue)
            {
                return RedirectToAction("Details", "CustomerOrderSlip", new { id = model.CustomerOrderSlipId, dispatchTicketId = id, editTicket = true });
            }

            if (!await dispatchTicketService.IsCustomerOrderSlipEditableAsync(model.CustomerOrderSlipId, cancellationToken))
            {
                TempData["error"] = "Cannot edit ticket — parent COS is cancelled or closed.";
                return RedirectToAction(nameof(Index), new { filterType });
            }

            var viewModel = new DispatchTicketViewModel();
            viewModel.FromEntity(model);
            viewModel.CustomerOrderSlipId = model.CustomerOrderSlipId;

            viewModel.PortId = model.Terminal.Port.PortId;

            if (!string.IsNullOrEmpty(model.ImageName))
            {
                viewModel.ImageSignedUrl = await cloudStorageService.GetSignedUrlAsync(model.ImageName);
            }

            if (!string.IsNullOrEmpty(model.VideoName))
            {
                viewModel.VideoSignedUrl = await cloudStorageService.GetSignedUrlAsync(model.VideoName);
            }

            viewModel = await dispatchTicketService.PopulateSelectListsAsync(viewModel, cancellationToken);

            ViewData["PortId"] = model.Terminal.Port.PortId;
            return View(viewModel);
        }

        /// <summary>
        /// Processes the update of basic information for a Dispatch Ticket.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditDispatchTicket, "Access denied. You don't have permission to edit Dispatch Tickets.")]
        public async Task<IActionResult> EditTicket(
            DispatchTicketViewModel viewModel,
            IFormFile? imageFile,
            IFormFile? videoFile,
            string? filterType,
            CancellationToken cancellationToken = default, bool inlineEntry = false)
        {
            if (inlineEntry && (!ModelState.IsValid || !viewModel.DispatchTicketId.HasValue))
            {
                return Json(new { success = false, message = "Review the ticket number, dates and required fields before saving." });
            }
            var result = await dispatchTicketService.UpdateDispatchTicketAsync(viewModel, imageFile, videoFile, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                if (inlineEntry)
                {
                    return Json(new { success = true, message = result.Message, redirectUrl = Url.Action("Details", "CustomerOrderSlip", new { id = viewModel.CustomerOrderSlipId, dispatchTicketId = viewModel.DispatchTicketId }) });
                }
                TempData["success"] = result.Message;

                return viewModel.CustomerOrderSlipId.HasValue
                    ? RedirectToAction("Details", "CustomerOrderSlip", new { id = viewModel.CustomerOrderSlipId.Value })
                    : RedirectToAction(nameof(Index), new { filterType });
            }

            if (inlineEntry)
            {
                return Json(new { success = false, message = result.Message });
            }
            if (result.Status == ServiceResultStatus.NotFound)
            {
                return NotFound();
            }

            TempData["error"] = result.Message;
            return RedirectToAction("EditTicket", new { id = viewModel.DispatchTicketId, customerOrderSlipId = viewModel.CustomerOrderSlipId });
        }

        #endregion

        [HttpGet]
        [RequireAccess(ProcedureEnum.EditDispatchTicket)]
        public async Task<IActionResult> PreviewCharges(int id, int customerOrderSlipId, CancellationToken ct)
        {
            var result = await dispatchTicketService.PreviewChargesAsync(id, customerOrderSlipId, ct);
            return Json(new { success = result.IsSuccess, message = result.Message, data = result.Data });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditDispatchTicket, "Access denied. You don't have permission to recalculate ticket charges.")]
        public async Task<IActionResult> RecalculateCharges(int id, int customerOrderSlipId, CancellationToken ct, string? reviewedFingerprint)
        {
            var result = await dispatchTicketService.RecalculateChargesAsync(id, customerOrderSlipId, User.Identity?.Name ?? "System", ct, reviewedFingerprint);
            return Json(new
            {
                success = result.IsSuccess,
                message = result.Message,
                redirectUrl = result.IsSuccess ? Url.Action("Details", "CustomerOrderSlip", new { id = customerOrderSlipId, dispatchTicketId = id }) : null
            });
        }

        #region AJAX Endpoints

        /// <summary>
        /// Returns the voyage type (Foreign/Local) for a given vessel.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.CreateDispatchTicket, ProcedureEnum.EditDispatchTicket)]
        public async Task<IActionResult> GetVesselVoyageType(int vesselId, CancellationToken cancellationToken)
        {
            var vessel = await unitOfWork.Vessel.GetAsync(v => v.VesselId == vesselId, cancellationToken);
            var voyageType = vessel?.VesselType == "FOREIGN" ? "Foreign" : "Local";
            return Json(voyageType);
        }

        /// <summary>
        /// Retrieves terminals for a specific port for cascading dropdowns.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.CreateDispatchTicket, ProcedureEnum.EditDispatchTicket)]
        public async Task<IActionResult> ChangeTerminal(int portId, CancellationToken cancellationToken)
        {
            var list = await dispatchTicketService.GetTerminalsByPortAsync(portId, cancellationToken);
            return Json(list);
        }

        /// <summary>
        /// Retrieves a list of Dispatch Tickets filtered by status.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetDispatchTicketList(string status, CancellationToken cancellationToken)
        {
            var items = await dispatchTicketService.GetDispatchTicketsByFilterAsync(status, cancellationToken);
            return Json(items);
        }

        /// <summary>
        /// Retrieves a paged and filtered list of Dispatch Tickets for DataTables.
        /// Includes signed URLs for media attachments.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetDispatchTicketLists(
            [FromForm] DataTablesParameters parameters,
            string filterType,
            CancellationToken cancellationToken)
        {
            try
            {
                var (data, recordsFiltered, totalRecords) = await dispatchTicketService.GetPagedDispatchTicketsAsync(parameters, filterType, cancellationToken);

                foreach (var dt in data.Where(dt => !string.IsNullOrEmpty(dt.ImageName)))
                {
                    dt.ImageSignedUrl = await cloudStorageService.GetSignedUrlAsync(dt.ImageName!);
                }

                foreach (var dt in data.Where(dt => !string.IsNullOrEmpty(dt.VideoName)))
                {
                    dt.VideoSignedUrl = await cloudStorageService.GetSignedUrlAsync(dt.VideoName!);
                }

                var closedMonths = (await unitOfWork.PostedPeriod.GetAllAsync(cancellationToken))
                    .Where(p => MsapConstants.EnforceClosedPeriods && p.IsClosed)
                    .Select(p => (p.Year, p.Month))
                    .ToHashSet();
                foreach (var dt in data)
                {
                    dt.IsMonthClosed = closedMonths.Contains((dt.Date.Year, dt.Date.Month));
                }

                return Json(new
                {
                    draw = parameters.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered,
                    data
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get dispatch tickets");
                return Json(new { error = ex.Message, draw = parameters.Draw, recordsTotal = 0, recordsFiltered = 0, data = Array.Empty<DispatchTicket>() });
            }
        }

        #endregion

        #region Customer Lookup

        /// <summary>
        /// Searches for customers matching a search term.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.CreateDispatchTicket, ProcedureEnum.EditDispatchTicket)]
        public async Task<JsonResult> SearchCustomers(string? term, CancellationToken cancellationToken)
        {
            var result = await unitOfWork.Customer.SearchCustomersDtoAsync(term ?? string.Empty, 10, cancellationToken);
            return Json(result);
        }

        #endregion
    }

}
