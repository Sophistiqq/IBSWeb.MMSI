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
        [RequireAnyAccess(
            "Access denied. You don't have permission to access Dispatch Tickets.",
            ProcedureEnum.CreateDispatchTicket,
            ProcedureEnum.EditDispatchTicket,
            ProcedureEnum.DeleteDispatchTicket)]
        public async Task<IActionResult> Index(string filterType)
        {
            ViewBag.FilterType = filterType;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanSetTariff = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.SetTariff);
            ViewBag.CanEditTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditDispatchTicket);
            ViewBag.CanApproveTariff = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveTariff);
            ViewBag.CanDeleteTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.DeleteDispatchTicket);
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
            CancellationToken cancellationToken = default)
        {
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            string? role = userId is not null ? await roleService.GetUserRoleAsync(userId) : null;
            if ((imageFile == null || imageFile.Length == 0) && role is not (MsapRoles.Admin or MsapRoles.SuperAdmin))
            {
                TempData["warning"] = "An image of the Dispatch Ticket is strictly required!";
                viewModel = await dispatchTicketService.PopulateDispatchTicketViewModelAsync(viewModel, null, cancellationToken);
                return View(viewModel);
            }

            if (!ModelState.IsValid)
            {
                await dispatchTicketService.PopulateDispatchTicketViewModelAsync(viewModel, viewModel.CustomerOrderSlipId, cancellationToken);
                return View(viewModel);
            }
            var result = await dispatchTicketService.CreateDispatchTicketAsync(viewModel, imageFile, videoFile, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;

                if (viewModel.CustomerOrderSlipId.HasValue)
                {
                    TempData["PromptAddTicket"] = !viewModel.VesselScheduleServiceEntryId.HasValue;
                    TempData.Remove("success");
                    return RedirectToAction("Details", "CustomerOrderSlip", new { id = viewModel.CustomerOrderSlipId });
                }

                return RedirectToAction(nameof(Index));
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
        [RequireAnyAccess(
            "Access denied. You don't have permission to view Dispatch Tickets.",
            ProcedureEnum.SetTariff,
            ProcedureEnum.ApproveTariff,
            ProcedureEnum.CreateBilling,
            ProcedureEnum.CreateCollection,
            ProcedureEnum.CreateDispatchTicket,
            ProcedureEnum.EditDispatchTicket,
            ProcedureEnum.DeleteDispatchTicket)]
        public async Task<IActionResult> Preview(int id, CancellationToken cancellationToken = default)
        {
            var model = await dispatchTicketService.GetDispatchTicketByIdAsync(id, cancellationToken);
            if (model == null)
            {
                return NotFound();
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
            ViewBag.CanApproveTariff = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveTariff);
            ViewBag.CanEditTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditDispatchTicket);

            return View(model);
        }

        #endregion

        #region Set Tariff

        /// <summary>
        /// Displays the form to set the tariff for a Dispatch Ticket.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.SetTariff, "Access denied. You don't have permission to set Tariff.")]
        public async Task<IActionResult> SetTariff(int id, string? filterType, CancellationToken cancellationToken)
        {
            ViewBag.FilterType = filterType;
            var ticket = await dispatchTicketService.GetDispatchTicketByIdAsync(id, cancellationToken);
            if (ticket == null)
            {
                return NotFound();
            }

            if (ticket.Status != MsapConstants.DispatchTicketStatus.ForTariff)
            {
                TempData["error"] = "Only tickets in 'For Tariff' status can have a tariff set.";
                return RedirectToAction(nameof(Index), new { filterType });
            }

            var viewModel = new TariffViewModel
            {
                DispatchTicketId = ticket.DispatchTicketId,
                CustomerOrderSlipId = ticket.CustomerOrderSlipId,
                CustomerId = ticket.CustomerId,
                DispatchNumber = ticket.DispatchNumber,
                COSNumber = ticket.COSNumber,
                VoyageNumber = ticket.VoyageNumber,
                Date = ticket.Date,
                TugMasterName = ticket.TugMaster?.TugMasterName,
                DateLeft = ticket.DateLeft,
                TimeLeft = ticket.TimeLeft,
                DateArrived = ticket.DateArrived,
                TimeArrived = ticket.TimeArrived,
                TugboatName = ticket.Tugboat.TugboatName,
                VesselName = ticket.Vessel.VesselName,
                VesselType = ticket.Vessel.VesselType,
                TerminalName = ticket.Terminal.TerminalName,
                PortName = ticket.Terminal.Port.PortName,
                IsTugboatCompanyOwned = ticket.Tugboat.IsCompanyOwned,
                TugboatOwnerName = ticket.Tugboat.TugboatOwner?.TugboatOwnerName,
                FixedRate = ticket.Tugboat.TugboatOwner?.FixedRate,
                Remarks = ticket.Remarks,
                TotalHours = ticket.TotalHours,
                CustomerName = ticket.Customer.CustomerName,
                DispatchRate = ticket.DispatchRate,
                DispatchDiscount = ticket.DispatchDiscount,
                BAFRate = ticket.BAFRate,
                BAFDiscount = ticket.BAFDiscount,
                ApOtherTugs = ticket.ApOtherTugs,
                ApOtherTugsIncludesVat = ticket.ApOtherTugsIncludesVat,
                DispatchChargeType = ticket.DispatchChargeType,
                BAFChargeType = ticket.BAFChargeType,
                Customers = await dispatchTicketService.GetCustomerSelectListAsync(cancellationToken)
            };

            if (!string.IsNullOrEmpty(ticket.ImageName))
            {
                viewModel.ImageSignedUrl = await cloudStorageService.GetSignedUrlAsync(ticket.ImageName);
            }

            if (!string.IsNullOrEmpty(ticket.VideoName))
            {
                viewModel.VideoSignedUrl = await cloudStorageService.GetSignedUrlAsync(ticket.VideoName);
            }

            return View(viewModel);
        }

        /// <summary>
        /// Processes the saving of the tariff for a Dispatch Ticket.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.SetTariff, "Access denied. You don't have permission to set Tariff.")]
        public async Task<IActionResult> SetTariff(
            TariffViewModel viewModel,
            string chargeType, string chargeType2, string? filterType, CancellationToken cancellationToken)
        {
            if (viewModel.CustomerId is null or 0)
            {
                TempData["error"] = "Customer is required before setting tariff.";
                return RedirectToAction(nameof(SetTariff), new { id = viewModel.DispatchTicketId, filterType });
            }

            var model = new DispatchTicket
            {
                DispatchTicketId = viewModel.DispatchTicketId,
                CustomerOrderSlipId = viewModel.CustomerOrderSlipId,
                CustomerId = viewModel.CustomerId.Value,
                DispatchRate = viewModel.DispatchRate ?? 0,
                DispatchDiscount = viewModel.DispatchDiscount ?? 0,
                DispatchBillingAmount = viewModel.DispatchBillingAmount,
                DispatchNetRevenue = viewModel.DispatchNetRevenue,
                BAFRate = viewModel.BAFRate ?? 0,
                BAFDiscount = viewModel.BAFDiscount ?? 0,
                BAFBillingAmount = viewModel.BAFBillingAmount,
                BAFNetRevenue = viewModel.BAFNetRevenue,
                TotalBilling = viewModel.TotalBilling,
                TotalNetRevenue = viewModel.TotalNetRevenue,
                ApOtherTugs = viewModel.ApOtherTugs ?? 0,
                ApOtherTugsIncludesVat = viewModel.ApOtherTugsIncludesVat
            };

            var result = await dispatchTicketService.SaveTariffAsync(model, chargeType, chargeType2, User.Identity?.Name ?? "System", isEdit: false, cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
                return model.CustomerOrderSlipId.HasValue
                    ? RedirectToAction("Details", "CustomerOrderSlip", new { id = model.CustomerOrderSlipId.Value })
                    : RedirectToAction(nameof(Index), new { filterType });
            }

            TempData["error"] = result.Message;
            return RedirectToAction(nameof(SetTariff), new { id = model.DispatchTicketId, filterType });
        }

        #endregion

        #region Edit Tariff

        /// <summary>
        /// Displays the form to edit the tariff for a Dispatch Ticket.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.SetTariff, "Access denied. You don't have permission to edit Tariff.")]
        public async Task<IActionResult> EditTariff(int id, string? filterType, CancellationToken cancellationToken)
        {
            ViewBag.FilterType = filterType;
            var ticket = await dispatchTicketService.GetDispatchTicketByIdAsync(id, cancellationToken);
            if (ticket == null)
            {
                return NotFound();
            }

            if (ticket.Status is not (MsapConstants.DispatchTicketStatus.ForApproval or MsapConstants.DispatchTicketStatus.Disapproved))
            {
                TempData["error"] = "Only tickets awaiting approval or disapproved can have their tariff edited.";
                return RedirectToAction(nameof(Index), new { filterType });
            }

            var viewModel = new TariffViewModel
            {
                DispatchTicketId = ticket.DispatchTicketId,
                CustomerOrderSlipId = ticket.CustomerOrderSlipId,
                CustomerId = ticket.CustomerId,
                DispatchNumber = ticket.DispatchNumber,
                COSNumber = ticket.COSNumber,
                VoyageNumber = ticket.VoyageNumber,
                Date = ticket.Date,
                TugMasterName = ticket.TugMaster?.TugMasterName,
                DateLeft = ticket.DateLeft,
                TimeLeft = ticket.TimeLeft,
                DateArrived = ticket.DateArrived,
                TimeArrived = ticket.TimeArrived,
                TugboatName = ticket.Tugboat.TugboatName,
                VesselName = ticket.Vessel.VesselName,
                VesselType = ticket.Vessel.VesselType,
                TerminalName = ticket.Terminal.TerminalName,
                PortName = ticket.Terminal.Port.PortName,
                IsTugboatCompanyOwned = ticket.Tugboat.IsCompanyOwned,
                TugboatOwnerName = ticket.Tugboat.TugboatOwner?.TugboatOwnerName,
                FixedRate = ticket.Tugboat.TugboatOwner?.FixedRate,
                Remarks = ticket.Remarks,
                TotalHours = ticket.TotalHours,
                CustomerName = ticket.Customer.CustomerName,
                DispatchRate = ticket.DispatchRate,
                DispatchDiscount = ticket.DispatchDiscount,
                DispatchBillingAmount = ticket.DispatchBillingAmount,
                DispatchNetRevenue = ticket.DispatchNetRevenue,
                BAFRate = ticket.BAFRate,
                BAFDiscount = ticket.BAFDiscount,
                BAFBillingAmount = ticket.BAFBillingAmount,
                BAFNetRevenue = ticket.BAFNetRevenue,
                TotalBilling = ticket.TotalBilling,
                TotalNetRevenue = ticket.TotalNetRevenue,
                ApOtherTugs = ticket.ApOtherTugs,
                ApOtherTugsIncludesVat = ticket.ApOtherTugsIncludesVat,
                DispatchChargeType = ticket.DispatchChargeType,
                BAFChargeType = ticket.BAFChargeType,
                Customers = await dispatchTicketService.GetCustomerSelectListAsync(cancellationToken)
            };

            if (!string.IsNullOrEmpty(ticket.ImageName))
            {
                viewModel.ImageSignedUrl = await cloudStorageService.GetSignedUrlAsync(ticket.ImageName);
            }

            if (!string.IsNullOrEmpty(ticket.VideoName))
            {
                viewModel.VideoSignedUrl = await cloudStorageService.GetSignedUrlAsync(ticket.VideoName);
            }

            return View(viewModel);
        }

        /// <summary>
        /// Processes the saving of the tariff for a Dispatch Ticket.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.SetTariff, "Access denied. You don't have permission to edit Tariff.")]
        public async Task<IActionResult> EditTariff(
            TariffViewModel viewModel,
            string chargeType, string chargeType2, string? filterType, CancellationToken cancellationToken)
        {
            if (viewModel.CustomerId is null or 0)
            {
                TempData["error"] = "Customer is required before editing tariff.";
                return RedirectToAction(nameof(EditTariff), new { id = viewModel.DispatchTicketId, filterType });
            }

            var model = new DispatchTicket
            {
                DispatchTicketId = viewModel.DispatchTicketId,
                CustomerOrderSlipId = viewModel.CustomerOrderSlipId,
                CustomerId = viewModel.CustomerId.Value,
                DispatchRate = viewModel.DispatchRate ?? 0,
                DispatchDiscount = viewModel.DispatchDiscount ?? 0,
                DispatchBillingAmount = viewModel.DispatchBillingAmount,
                DispatchNetRevenue = viewModel.DispatchNetRevenue,
                BAFRate = viewModel.BAFRate ?? 0,
                BAFDiscount = viewModel.BAFDiscount ?? 0,
                BAFBillingAmount = viewModel.BAFBillingAmount,
                BAFNetRevenue = viewModel.BAFNetRevenue,
                TotalBilling = viewModel.TotalBilling,
                TotalNetRevenue = viewModel.TotalNetRevenue,
                ApOtherTugs = viewModel.ApOtherTugs ?? 0,
                ApOtherTugsIncludesVat = viewModel.ApOtherTugsIncludesVat
            };

            var result = await dispatchTicketService.SaveTariffAsync(model, chargeType, chargeType2, User.Identity?.Name ?? "System", isEdit: true, cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
                return model.CustomerOrderSlipId.HasValue
                    ? RedirectToAction("Details", "CustomerOrderSlip", new { id = model.CustomerOrderSlipId.Value })
                    : RedirectToAction(nameof(Index), new { filterType });
            }

            TempData["error"] = result.Message;
            return RedirectToAction(nameof(EditTariff), new { id = model.DispatchTicketId, filterType });
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
            CancellationToken cancellationToken = default)
        {
            var result = await dispatchTicketService.UpdateDispatchTicketAsync(viewModel, imageFile, videoFile, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;

                return viewModel.CustomerOrderSlipId.HasValue
                    ? RedirectToAction("Details", "CustomerOrderSlip", new { id = viewModel.CustomerOrderSlipId.Value })
                    : RedirectToAction(nameof(Index), new { filterType });
            }

            if (result.Status == ServiceResultStatus.NotFound)
            {
                return NotFound();
            }

            TempData["error"] = result.Message;
            return RedirectToAction("EditTicket", new { id = viewModel.DispatchTicketId, customerOrderSlipId = viewModel.CustomerOrderSlipId });
        }

        #endregion

        #region Status Changes

        /// <summary>
        /// Approves the tariff for a Dispatch Ticket, moving it to 'For Billing'.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveTariff, "Access denied. You don't have permission to approve Tariff.")]
        public async Task<IActionResult> ApproveTariff(int id, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.ApproveTariffAsync(id, User.Identity?.Name ?? "System", cancellationToken);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        /// <summary>
        /// Disapproves the tariff for a Dispatch Ticket, requiring a reason.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveTariff, "Access denied. You don't have permission to approve Tariff.")]
        public async Task<IActionResult> DisapproveTariff(int id, string reason, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.DisapproveTariffAsync(id, reason, User.Identity?.Name ?? "System", cancellationToken);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        /// <summary>
        /// Deletes a Dispatch Ticket.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.DeleteDispatchTicket, "Access denied. You don't have permission to delete Dispatch Tickets.")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.DeleteTicketAsync(id, User.Identity?.Name ?? "System", cancellationToken);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        /// <summary>
        /// Restores a deleted Dispatch Ticket.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.DeleteDispatchTicket, "Access denied. You don't have permission to restore Dispatch Tickets.")]
        public async Task<IActionResult> Restore(int id, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.RestoreTicketAsync(id, User.Identity?.Name ?? "System", cancellationToken);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        /// <summary>
        /// Revokes tariff approval for an unbilled Dispatch Ticket.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditDispatchTicket, "Access denied. You don't have permission to change Dispatch Ticket status.")]
        public async Task<IActionResult> ChangeStatus(int id, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.RevokeTariffApprovalAsync(
                id, User.Identity?.Name ?? "System", cancellationToken);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        /// <summary>
        /// Batch approves multiple tariffs at once.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveTariff, "Access denied. You don't have permission to approve Tariff.")]
        public async Task<IActionResult> BatchApproveTariff([FromBody] List<int>? ids, CancellationToken cancellationToken)
        {
            if (ids == null || ids.Count == 0)
            {
                return Json(new { success = false, message = "No tickets selected." });
            }

            var result = await dispatchTicketService.BatchApproveTariffAsync(ids, User.Identity?.Name ?? "System", cancellationToken);
            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        /// <summary>
        /// Batch sets tariff rates for multiple tickets at once.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.SetTariff, "Access denied. You don't have permission to set Tariff.")]
        public async Task<IActionResult> BatchSetTariff(
            [FromBody] BatchTariffRequest? request,
            CancellationToken cancellationToken)
        {
            if (request?.Ids == null || request.Ids.Count == 0)
            {
                return Json(new { success = false, message = "No tickets selected." });
            }

            var result = await dispatchTicketService.BatchSetTariffAsync(
                request.Ids, request.DispatchRate, request.BafRate,
                request.ChargeType, request.ChargeType2,
                User.Identity?.Name ?? "System", cancellationToken);

            return Json(new { success = result.IsSuccess, message = result.Message });
        }

        #endregion

        #region AJAX Endpoints

        /// <summary>
        /// Returns the voyage type (Foreign/Local) for a given vessel.
        /// </summary>
        [HttpGet]
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
        public async Task<IActionResult> ChangeTerminal(int portId, CancellationToken cancellationToken)
        {
            var list = await dispatchTicketService.GetTerminalsByPortAsync(portId, cancellationToken);
            return Json(list);
        }

        /// <summary>
        /// Retrieves a list of Dispatch Tickets filtered by status.
        /// </summary>
        [HttpGet]
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
                    .Where(p => p.IsClosed)
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

        /// <summary>
        /// Checks if a tariff rate exists for a given customer and Dispatch Ticket criteria.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckForTariffRate(
            int customerId, int dispatchTicketId, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.CheckForTariffRateAsync(customerId, dispatchTicketId, cancellationToken);

            if (result.IsSuccess)
            {
                return Json(result.Data);
            }

            if (result.Status == ServiceResultStatus.NotFound)
            {
                return NotFound();
            }

            return BadRequest(result.Message);
        }

        #endregion

        #region Media Management

        /// <summary>
        /// Deletes the associated image from a Dispatch Ticket and cloud storage.
        /// </summary>
        public async Task<IActionResult> DeleteImage(int id, string filterType, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.DeleteImageAsync(id, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
            }
            else
            {
                TempData["error"] = result.Message;
            }

            return RedirectToAction(nameof(Index), new { filterType });
        }

        /// <summary>
        /// Deletes the associated video from a Dispatch Ticket and cloud storage.
        /// </summary>
        public async Task<IActionResult> DeleteVideo(int id, string filterType, CancellationToken cancellationToken)
        {
            var result = await dispatchTicketService.DeleteVideoAsync(id, User.Identity?.Name ?? "System", cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
            }
            else
            {
                TempData["error"] = result.Message;
            }

            return RedirectToAction(nameof(Index), new { filterType });
        }

        /// <summary>
        /// Searches for customers matching a search term.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> SearchCustomers(string? term, CancellationToken cancellationToken)
        {
            var result = await unitOfWork.Customer.SearchCustomersDtoAsync(term ?? string.Empty, 10, cancellationToken);
            return Json(result);
        }

        #endregion
    }

}
