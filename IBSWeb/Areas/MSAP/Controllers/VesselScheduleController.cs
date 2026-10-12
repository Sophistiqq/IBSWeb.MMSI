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
        public async Task<IActionResult> Index(DateTime? month, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Choose a valid month.");
            }

            var selected = month ?? DateTimeHelper.GetCurrentPhilippineTime();
            var start = new DateTime(selected.Year, selected.Month, 1);
            if (start.Year < 1900 || start.Year > 9998)
            {
                return BadRequest("Choose a month between 1900 and 9998.");
            }

            var gridStart = start.AddDays(-(int)start.DayOfWeek);
            var schedules = await scheduleService.GetSchedulesAsync(gridStart, gridStart.AddDays(42), ct);
            var board = new VesselScheduleBoardViewModel { Date = start, Schedules = schedules.ToList() };
            await MarkConflictsAsync(board, gridStart, gridStart.AddDays(42), ct);
            return View(board);
        }

        [HttpGet]
        public async Task<IActionResult> Day(DateTime? date, CancellationToken ct)
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

            var schedules = await scheduleService.GetSchedulesAsync(day, day.AddDays(1), ct);
            var tugboats = await unitOfWork.Tugboat.GetAllAsync(null, ct);
            var board = new VesselScheduleBoardViewModel
            {
                Date = day,
                Schedules = schedules.ToList(),
                Tugboats = tugboats.OrderBy(t => t.TugboatName).ToList()
            };
            await MarkConflictsAsync(board, day, day.AddDays(1), ct);
            await SetActionPermissionsAsync();
            return View(board);
        }

        [HttpGet]
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
        [RequireAccess(ProcedureEnum.EditJobOrder)]
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
            var vm = MapToViewModel(entity);
            await PopulateDropdownsAsync(vm, ct, entity.PortId);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditJobOrder)]
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

            vm.Status = (await scheduleService.GetByIdAsync(vm.VesselScheduleId, ct))?.Status ?? MsapConstants.VesselScheduleStatus.Tentative;
            ModelState.Remove(nameof(vm.Status));
            await PopulateDropdownsAsync(vm, ct);
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id, CancellationToken ct)
        {
            var schedule = await LoadDetailsAsync(id, ct);
            if (schedule == null)
            {
                return NotFound();
            }
            await SetActionPermissionsAsync();
            var job = schedule.JobOrderId.HasValue
                ? await unitOfWork.JobOrder.GetAsync(j => j.JobOrderId == schedule.JobOrderId.Value, ct)
                : null;
            ViewBag.JobProgress = await JobProgressCalculator.LoadAsync(unitOfWork, job, schedule, ct);
            ViewBag.HasDispatchTickets = schedule.JobOrderId.HasValue &&
                await unitOfWork.DispatchTicket.GetAsync(t => t.JobOrderId == schedule.JobOrderId.Value, ct) != null;
            bool hasBilling = schedule.JobOrderId.HasValue && await unitOfWork.Billing.GetAsync(b => b.JobOrderId == schedule.JobOrderId, ct) != null;
            bool hasActiveTickets = schedule.JobOrderId.HasValue && await unitOfWork.DispatchTicket.GetAsync(t => t.JobOrderId == schedule.JobOrderId
                && (t.Status != MsapConstants.DispatchTicketStatus.Deleted || t.BillingId != null), ct) != null;
            ViewBag.CanRevise = schedule.JobOrderId == null || (job?.Status == MsapConstants.JobOrderStatus.Open && ViewBag.HasDispatchTickets != true && !hasBilling);
            ViewBag.CanComplete = job != null && schedule.Status is not (MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                && JobOrderService.CanCompleteService(await unitOfWork.DispatchTicket.GetAllAsync(t => t.JobOrderId == job.JobOrderId, ct));
            ViewBag.CanCancel = schedule.JobOrderId == null || (job?.Status == MsapConstants.JobOrderStatus.Open && !hasActiveTickets && !hasBilling);
            return View(schedule);
        }

        private async Task SetActionPermissionsAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanConfirm = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateJobOrder);
            ViewBag.CanEdit = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.EditJobOrder);
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateJobOrder)]
        public async Task<IActionResult> Confirm(int id, CancellationToken ct)
        {
            var schedule = await LoadDetailsAsync(id, ct);
            if (schedule == null)
            {
                return NotFound();
            }
            if (schedule.JobOrderId.HasValue || schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
            {
                return RedirectToAction(nameof(Details), new { id });
            }
            if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            {
                return RedirectToAction(nameof(Details), new { id, review = true });
            }
            return PartialView("_ConfirmSchedule", schedule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateJobOrder)]
        public async Task<IActionResult> Confirm(int id, DateTime reviewedAt, bool allowConflicts, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Review the schedule again before confirming." });
            }
            var result = await scheduleService.ConfirmAsync(id, reviewedAt, User.Identity?.Name ?? "system", ct, allowConflicts);
            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
            }
            return Json(new
            {
                success = result.IsSuccess,
                message = result.Message,
                requiresConflictAcknowledgement = !allowConflicts && result.Message?.StartsWith("Overlapping plans:", StringComparison.Ordinal) == true,
                redirectUrl = result.IsSuccess ? Url.Action("Details", "JobOrder", new { area = "MSAP", id = result.Data }) : null
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditJobOrder)]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            return await ChangeStatusAsync(id, MsapConstants.VesselScheduleStatus.Cancelled, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditJobOrder)]
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
            var entity = await unitOfWork.VesselSchedule.GetAsync(
                s => s.VesselScheduleId == id, ct);
            if (entity == null)
            {
                return null;
            }

            var schedule = (await scheduleService.GetSchedulesAsync(entity.PlannedStart, entity.PlannedEnd, ct))
                .FirstOrDefault(s => s.VesselScheduleId == id);
            var tugIds = string.IsNullOrEmpty(entity.AssignedTugboatIds)
                ? new List<int>() : JsonSerializer.Deserialize<List<int>>(entity.AssignedTugboatIds) ?? [];
            ViewBag.AssignedTugboats = (await unitOfWork.Tugboat.GetAllAsync(t => tugIds.Contains(t.TugboatId), ct))
                .OrderBy(t => t.TugboatName).Select(t => t.TugboatName).ToList();

            return schedule;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var schedule = await scheduleService.GetByIdAsync(id, ct);
            var result = await scheduleService.DeleteAsync(id, User.Identity?.Name ?? "system", ct);

            if (result.IsSuccess)
            {
                TempData["success"] = "Schedule deleted successfully.";
            }
            else
            {
                TempData["error"] = result.Message;
            }

            return RedirectToAction(nameof(Day), new { date = (schedule?.PlannedStart ?? DateTimeHelper.GetCurrentPhilippineTime()).ToString("yyyy-MM-dd") });
        }

        [HttpGet]
        public async Task<IActionResult> GetTerminalsByPort(int portId, CancellationToken ct)
        {
            var terminals = await unitOfWork.Terminal.GetAllAsync(t => t.PortId == portId, ct);
            return Json(terminals.OrderBy(t => t.TerminalName).Select(t => new { value = t.TerminalId.ToString(), text = t.TerminalName }));
        }

        [HttpGet]
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

        private async Task MarkConflictsAsync(VesselScheduleBoardViewModel board, DateTime from, DateTime to, CancellationToken ct)
        {
            if (board.Schedules.Count == 0)
            {
                return;
            }

            var tugboats = board.Tugboats.Count > 0 ? board.Tugboats : await unitOfWork.Tugboat.GetAllAsync(null, ct);
            var names = tugboats.ToDictionary(t => t.TugboatId, t => t.TugboatName);
            // ponytail: pairwise checks in the displayed window; index resource intervals if schedule volume grows.
            foreach (var schedule in board.Schedules)
            {
                var conflicts = await scheduleService.CheckConflictsAsync(schedule, ct, board.Schedules, names);
                foreach (var conflict in conflicts)
                {
                    var start = new[] { schedule.PlannedStart, conflict.ConflictStart, from }.Max();
                    var end = new[] { schedule.PlannedEnd, conflict.ConflictEnd, to }.Min();
                    for (var day = start.Date; day < end; day = day.AddDays(1))
                    {
                        if (!board.ConflictsByDay.TryGetValue(day, out var ids))
                        {
                            board.ConflictsByDay[day] = ids = [];
                        }

                        ids.Add(schedule.VesselScheduleId);
                        ids.Add(conflict.ConflictingScheduleId);
                    }
                }
            }
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
            vm.Tugboats = (await unitOfWork.Tugboat.GetAllAsync(null, ct))
                .OrderBy(t => t.TugboatName)
                .Select(t => new SelectListItem { Value = t.TugboatId.ToString(), Text = $"{t.TugboatName} (#{t.TugboatNumber})" })
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
            return new VesselSchedule
            {
                CustomerId = vm.CustomerId,
                VesselId = vm.VesselId,
                PortId = vm.PortId,
                TerminalId = vm.TerminalId,
                PlannedStart = vm.PlannedStart,
                PlannedEnd = vm.PlannedEnd,
                AssignedTugboatIds = vm.SelectedTugboatIds?.Any() == true
                    ? JsonSerializer.Serialize(vm.SelectedTugboatIds)
                    : null,
                VoyageNumber = vm.VoyageNumber,
                Notes = vm.Notes
            };
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
                Notes = entity.Notes
            };
        }
    }

}
