using System.Security.Claims;
using System.Text.Json;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [Authorize]
    public class VesselScheduleController(
        IUnitOfWork unitOfWork,
        IVesselScheduleService scheduleService,
        IAccessControlService accessControl) : Controller
    {
        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Index(DateTime? month, CancellationToken ct, DateTime? date = null,
            int? portId = null, int? terminalId = null, string? search = null, string? status = null)
        {
            if (!ModelState.IsValid || search?.Length > 120)
            {
                return BadRequest("Choose a valid month and a search of at most 120 characters.");
            }

            var selected = month ?? date ?? DateTimeHelper.GetCurrentPhilippineTime();
            var start = new DateTime(selected.Year, selected.Month, 1);
            if (start.Year < 1900 || start.Year > 9998 || date.HasValue && (date.Value.Year < 1900 || date.Value.Year > 9998))
            {
                return BadRequest("Choose a month between 1900 and 9998.");
            }

            var gridStart = start.AddDays(-(int)start.DayOfWeek);
            if (date.HasValue && (date.Value < gridStart || date.Value >= gridStart.AddDays(42)))
            {
                date = new DateTime(start.Year, start.Month, Math.Min(date.Value.Day, DateTime.DaysInMonth(start.Year, start.Month)));
            }

            var result = await scheduleService.GetCalendarAsync(new VesselScheduleBoardViewModel
            {
                Date = start,
                SelectedDate = date?.Date,
                PortId = portId,
                TerminalId = terminalId,
                Search = search,
                Status = status
            }, ct);
            return result.IsSuccess ? View(result.Data) : BadRequest(result.Message);
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public IActionResult Day(DateTime? date, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Choose a valid date.");
            }

            var day = (date ?? DateTimeHelper.GetCurrentPhilippineTime()).Date;
            if (day.Year < 1900 || day.Year > 9998)
            {
                return BadRequest("Choose a date between 1900 and 9998.");
            }

            return RedirectToAction(nameof(Index), new { date = day.ToString("yyyy-MM-dd") });
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateCustomerOrderSlip)]
        public async Task<IActionResult> Create(DateTime? date, CancellationToken ct)
        {
            if (!ModelState.IsValid || date.HasValue && (date.Value.Year < 1900 || date.Value.Year > 9998))
            {
                return BadRequest("Choose a date between 1900 and 9998.");
            }

            var start = date?.Date.AddHours(8) ?? DateTimeHelper.GetCurrentPhilippineTime();
            var vm = new VesselScheduleViewModel { PlannedStart = start, PlannedEnd = start.AddHours(2) };

            await PopulateDropdownsAsync(vm, ct);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateCustomerOrderSlip)]
        public async Task<IActionResult> Create(VesselScheduleViewModel vm, CancellationToken ct)
        {
            if (ModelState.IsValid)
            {
                var entity = MapToEntity(vm);
                var result = await scheduleService.CreateAsync(entity, User.Identity?.Name ?? "system", ct, vm.AllowConflicts);

                if (result.IsSuccess)
                {
                    TempData["success"] = "Schedule created successfully.";
                    return RedirectToAction(nameof(Details), new { id = result.Data });
                }

                ModelState.AddModelError("", result.Message ?? "Failed to create schedule.");
            }

            await PopulateDropdownsAsync(vm, ct);
            return View(vm);
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> Edit(int id, CancellationToken ct)
        {
            var entity = await scheduleService.GetByIdAsync(id, ct);
            if (entity == null)
            {
                return NotFound();
            }

            if (entity.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
            {
                TempData["error"] = "Completed and cancelled schedules are read-only.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var hasTickets = (entity.CustomerOrderSlipId.HasValue && await unitOfWork.DispatchTicket.GetAsync(t => t.CustomerOrderSlipId == entity.CustomerOrderSlipId.Value, ct) != null)
                || (entity.ServiceEntries.Count > 0 && await unitOfWork.DispatchTicket.GetAsync(t => entity.ServiceEntries.Select(e => (int?)e.VesselScheduleServiceEntryId).Contains(t.VesselScheduleServiceEntryId), ct) != null);
            var hasBilling = entity.CustomerOrderSlipId.HasValue && await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == entity.CustomerOrderSlipId.Value, ct) != null;
            if (hasTickets || hasBilling)
            {
                TempData["error"] = "Cannot edit schedule once actual tickets or billing exist.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var vm = MapToViewModel(entity);
            await PopulateDropdownsAsync(vm, ct, entity.PortId);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> Edit(VesselScheduleViewModel vm, CancellationToken ct)
        {
            if (ModelState.IsValid)
            {
                var entity = MapToEntity(vm);
                entity.VesselScheduleId = vm.VesselScheduleId;

                var result = await scheduleService.UpdateAsync(entity, User.Identity?.Name ?? "system", ct, vm.AllowConflicts);

                if (result.IsSuccess)
                {
                    TempData["success"] = result.Message;
                    return RedirectToAction(nameof(Details), new { id = vm.VesselScheduleId });
                }

                ModelState.AddModelError("", result.Message ?? "Failed to update schedule.");
            }

            var existing = await scheduleService.GetByIdAsync(vm.VesselScheduleId, ct);
            vm.Status = existing?.Status ?? MsapConstants.VesselScheduleStatus.Tentative;
            vm.Revision = existing?.Revision ?? 1;
            vm.ApprovedRevision = existing?.ApprovedRevision;
            ModelState.Remove(nameof(vm.Status));
            await PopulateDropdownsAsync(vm, ct);
            return View(vm);
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Details(int id, CancellationToken ct)
        {
            var schedule = await LoadDetailsAsync(id, ct);
            if (schedule == null)
            {
                return NotFound();
            }
            await SetActionPermissionsAsync();
            ViewData["JobProgressActionStage"] = 1;
            ViewData["JobProgressActionTargetId"] = id;
            var job = schedule.CustomerOrderSlipId.HasValue
                ? await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == schedule.CustomerOrderSlipId.Value, ct)
                : null;
            ViewBag.JobProgress = await JobProgressCalculator.LoadAsync(unitOfWork, job, schedule, ct);

            var hasTickets = (schedule.CustomerOrderSlipId.HasValue && await unitOfWork.DispatchTicket.GetAsync(t => t.CustomerOrderSlipId == schedule.CustomerOrderSlipId.Value, ct) != null)
                || (schedule.ServiceEntries.Count > 0 && await unitOfWork.DispatchTicket.GetAsync(t => schedule.ServiceEntries.Select(e => (int?)e.VesselScheduleServiceEntryId).Contains(t.VesselScheduleServiceEntryId), ct) != null);
            bool hasBilling = schedule.CustomerOrderSlipId.HasValue && await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == schedule.CustomerOrderSlipId, ct) != null;
            bool hasActiveTickets = schedule.CustomerOrderSlipId.HasValue && await unitOfWork.DispatchTicket.GetAsync(t => t.CustomerOrderSlipId == schedule.CustomerOrderSlipId
                && (t.Status != MsapConstants.DispatchTicketStatus.Deleted || t.BillingId != null), ct) != null;

            ViewBag.HasDispatchTickets = hasTickets;
            var closed = schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled;
            ViewBag.CanRevise = !closed && ViewBag.CanEdit == true && !hasTickets && !hasBilling && !schedule.ServiceEntries.Any(e => e.CancelledAt.HasValue)
                && (job == null || job.Status == MsapConstants.CustomerOrderSlipStatus.Open);
            ViewBag.CanComplete = job != null && !closed
                && ((JobProgressViewModel)ViewBag.JobProgress).Stage >= JobProgressCalculator.TariffStage
                && CustomerOrderSlipService.CanCompleteService(await unitOfWork.DispatchTicket.GetAllAsync(t => t.CustomerOrderSlipId == job.CustomerOrderSlipId, ct));
            ViewBag.CanCancel = schedule.CustomerOrderSlipId == null || (job?.Status == MsapConstants.CustomerOrderSlipStatus.Open && !hasActiveTickets && !hasBilling);
            return View(schedule);
        }

        private async Task SetActionPermissionsAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanConfirm = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveSchedule);
            ViewBag.CanEdit = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditCustomerOrderSlip);
            ViewBag.CanRecordTicket = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateDispatchTicket);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateDispatchTicket)]
        public async Task<IActionResult> UrgentEntry(int id, CancellationToken ct)
        {
            var result = await scheduleService.StartUrgentEntryAsync(id, User.Identity?.Name ?? "system", ct);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return result.IsSuccess
                ? RedirectToAction("Details", "CustomerOrderSlip", new { id = result.Data })
                : RedirectToAction(nameof(Details), new { id });
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ApproveSchedule)]
        public async Task<IActionResult> Confirm(int id, CancellationToken ct)
        {
            var schedule = await LoadDetailsAsync(id, ct);
            if (schedule == null)
            {
                return NotFound();
            }
            if ((schedule.CustomerOrderSlipId.HasValue && schedule.ApprovedRevision == schedule.Revision)
                || schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
            {
                return RedirectToAction(nameof(Details), new { id });
            }
            return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                ? PartialView("_ConfirmSchedule", schedule)
                : RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveSchedule)]
        public async Task<IActionResult> Confirm(int id, DateTime reviewedAt, bool allowConflicts, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Review the schedule again before confirming." });
            }
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await scheduleService.ConfirmAsync(id, reviewedAt, User.Identity?.Name ?? "system", ct, allowConflicts, userId);
            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
            }
            return Json(new
            {
                success = result.IsSuccess,
                message = result.Message,
                requiresConflictAcknowledgement = !allowConflicts && result.Message?.StartsWith("Overlapping plans:", StringComparison.Ordinal) == true,
                redirectUrl = result.IsSuccess ? Url.Action("Details", "CustomerOrderSlip", new { area = "MSAP", id = result.Data }) : null
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            return await ChangeStatusAsync(id, MsapConstants.VesselScheduleStatus.Cancelled, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> Complete(int id, CancellationToken ct)
        {
            return await ChangeStatusAsync(id, MsapConstants.VesselScheduleStatus.Completed, ct);
        }

        private async Task<IActionResult> ChangeStatusAsync(int id, string status, CancellationToken ct)
        {
            var result = await scheduleService.ChangeStatusAsync(id, status, User.Identity?.Name ?? "system", ct);
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<VesselSchedule?> LoadDetailsAsync(int id, CancellationToken ct)
        {
            var entity = await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(id, ct);
            if (entity == null)
            {
                return null;
            }

            var schedule = (await scheduleService.GetSchedulesAsync(entity.PlannedStart, entity.PlannedEnd, ct))
                .FirstOrDefault(s => s.VesselScheduleId == id) ?? entity;
            var tugIds = string.IsNullOrEmpty(schedule.AssignedTugboatIds)
                ? new List<int>() : JsonSerializer.Deserialize<List<int>>(schedule.AssignedTugboatIds) ?? [];
            var assignedTugboats = (await unitOfWork.Tugboat.GetAllAsync(t => tugIds.Contains(t.TugboatId), ct))
                .OrderBy(t => t.TugboatName).ToList();
            ViewBag.AssignedTugboatModels = assignedTugboats;
            ViewBag.AssignedTugboats = assignedTugboats.Select(t => t.TugboatName).ToList();

            return schedule;
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetCustomerDiscount(int customerId, CancellationToken ct)
        {
            var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == customerId, ct);
            return Json(new { defaultDiscount = customer?.DefaultCosDiscountPercent ?? 0m });
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetTariffRate(int customerId, int terminalId, int serviceId, DateTime? date, CancellationToken ct)
        {
            if (customerId <= 0 || serviceId <= 0)
            {
                return Json(new { exists = false });
            }

            var asOfDate = DateOnly.FromDateTime(date ?? DateTimeHelper.GetCurrentPhilippineTime());

            var rate = await unitOfWork.TariffTable.GetApplicableRateAsync(customerId, terminalId, serviceId, asOfDate, ct);

            var bafRate = await unitOfWork.BafTable.GetApplicableRateAsync(customerId, terminalId, serviceId, asOfDate, ct);
            return Json(new
            {
                exists = rate != null || bafRate != null,
                tariffRate = rate?.Dispatch,
                bafRate = bafRate?.Amount
            });
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetTerminalsByPort(int portId, CancellationToken ct)
        {
            var terminals = await unitOfWork.Terminal.GetAllAsync(t => t.PortId == portId, ct);
            return Json(terminals.OrderBy(t => t.TerminalName).Select(t => new { value = t.TerminalId.ToString(), text = t.TerminalName }));
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> GetTugboatBookings(DateTime? start, DateTime? end, int scheduleId, CancellationToken ct)
        {
            if (!ModelState.IsValid || !start.HasValue || !end.HasValue
                || start.Value.Year < 1900 || end.Value.Year > 9998 || end <= start)
            {
                return BadRequest(new { message = "Choose a valid planned start and end." });
            }

            var schedules = await scheduleService.GetSchedulesAsync(start, end, ct);
            var bookings = schedules
                .Where(s => s.VesselScheduleId != scheduleId && s.Status != MsapConstants.VesselScheduleStatus.Cancelled)
                .SelectMany(s => (string.IsNullOrEmpty(s.AssignedTugboatIds)
                    ? new List<int>() : JsonSerializer.Deserialize<List<int>>(s.AssignedTugboatIds) ?? [])
                    .Distinct().Select(id => new
                    {
                        tugboatId = id,
                        scheduleId = s.VesselScheduleId,
                        vessel = s.Vessel.VesselName,
                        start = s.PlannedStart.ToString("MMM d, yyyy HH:mm"),
                        end = s.PlannedEnd.ToString("MMM d, yyyy HH:mm"),
                        status = s.Status
                    }))
                .ToList();
            return Json(new { bookings });
        }

        private async Task PopulateDropdownsAsync(VesselScheduleViewModel vm, CancellationToken ct, int? selectedPortId = null)
        {
            vm.Customers = await unitOfWork.GetCustomerListAsyncById(ct);
            vm.Vessels = (await unitOfWork.Vessel.GetAllAsync(null, ct))
                .OrderBy(v => v.VesselName)
                .Select(v => new SelectListItem { Value = v.VesselId.ToString(), Text = $"{v.VesselName} ({v.VesselNumber})" })
                .ToList();
            vm.Ports = (await unitOfWork.Port.GetAllAsync(null, ct))
                .OrderBy(p => p.PortName)
                .Select(p => new SelectListItem { Value = p.PortId.ToString(), Text = p.PortName })
                .ToList();
            var allTugboats = (await unitOfWork.Tugboat.GetAllAsync(null, ct))
                .OrderBy(t => t.TugboatName)
                .ToList();
            vm.Tugboats = allTugboats
                .Select(t => new SelectListItem { Value = t.TugboatId.ToString(), Text = $"{t.TugboatName} (#{t.TugboatNumber})" })
                .ToList();
            ViewBag.TugboatOwnerMap = allTugboats.ToDictionary(t => t.TugboatId, t => t.IsCompanyOwned);
            vm.Services = (await unitOfWork.Service.GetAllAsync(null, ct))
                .OrderBy(s => s.ServiceName)
                .Select(s => new SelectListItem { Value = s.ServiceId.ToString(), Text = s.ServiceName })
                .ToList();

            var portId = selectedPortId ?? vm.PortId;
            if (portId > 0)
            {
                vm.Terminals = (await unitOfWork.Terminal.GetAllAsync(t => t.PortId == portId, ct))
                    .OrderBy(t => t.TerminalName)
                    .Select(t => new SelectListItem { Value = t.TerminalId.ToString(), Text = t.TerminalName })
                    .ToList();
            }
        }

        private static VesselSchedule MapToEntity(VesselScheduleViewModel vm)
        {
            var entity = new VesselSchedule
            {
                CustomerId = vm.CustomerId,
                VesselId = vm.VesselId,
                PortId = vm.PortId,
                TerminalId = vm.TerminalId,
                PlannedStart = vm.PlannedStart,
                PlannedEnd = vm.PlannedEnd,
                AssignedTugboatIds = vm.SelectedTugboatIds?.Any() == true
                    ? JsonSerializer.Serialize(vm.SelectedTugboatIds.Distinct())
                    : null,
                VoyageNumber = vm.VoyageNumber,
                Notes = vm.Notes,
                CosDiscountPercent = vm.CosDiscountPercent,
                SkipCosDiscount = vm.SkipCosDiscount,
                SbmaEnabled = vm.SbmaEnabled,
                ServiceEntries = vm.ServiceEntries?
                    .Select(e => new VesselScheduleServiceEntry
                    {
                        VesselScheduleServiceEntryId = e.VesselScheduleServiceEntryId,
                        VesselScheduleId = vm.VesselScheduleId,
                        TugboatId = e.TugboatId,
                        ServiceId = e.ServiceId,
                        TariffBasis = e.TariffBasis,
                        BafMethod = e.BafMethod,
                        ManualTariffRate = e.ManualTariffRate,
                        ManualBafRate = e.ManualBafRate
                    }).ToList() ?? []
            };
            return entity;
        }

        private static VesselScheduleViewModel MapToViewModel(VesselSchedule entity)
        {
            var tugIds = string.IsNullOrEmpty(entity.AssignedTugboatIds)
                ? new List<int>()
                : JsonSerializer.Deserialize<List<int>>(entity.AssignedTugboatIds) ?? new List<int>();

            return new VesselScheduleViewModel
            {
                VesselScheduleId = entity.VesselScheduleId,
                CustomerId = entity.CustomerId ?? 0,
                VesselId = entity.VesselId,
                PortId = entity.PortId,
                TerminalId = entity.TerminalId,
                PlannedStart = entity.PlannedStart,
                PlannedEnd = entity.PlannedEnd,
                SelectedTugboatIds = tugIds,
                VoyageNumber = entity.VoyageNumber,
                Status = entity.Status,
                Notes = entity.Notes,
                CosDiscountPercent = entity.CosDiscountPercent,
                SkipCosDiscount = entity.SkipCosDiscount,
                SbmaEnabled = entity.SbmaEnabled,
                Revision = entity.Revision,
                ApprovedRevision = entity.ApprovedRevision,
                ServiceEntries = entity.ServiceEntries?.Select(e => new VesselScheduleServiceEntryViewModel
                {
                    VesselScheduleServiceEntryId = e.VesselScheduleServiceEntryId,
                    TugboatId = e.TugboatId,
                    TugboatName = e.Tugboat?.TugboatName,
                    ServiceId = e.ServiceId,
                    ServiceName = e.Service?.ServiceName,
                    TariffBasis = e.TariffBasis,
                    BafMethod = e.BafMethod,
                    ManualTariffRate = e.ManualTariffRate,
                    ManualBafRate = e.ManualBafRate
                }).ToList() ?? []
            };
        }
    }
}
