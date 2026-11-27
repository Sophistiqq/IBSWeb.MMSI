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
using Microsoft.EntityFrameworkCore;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [Authorize]
    public class VesselScheduleController(
        IUnitOfWork unitOfWork,
        IVesselScheduleService scheduleService,
        IAccessControlService accessControl,
        RateUploadService rates) : Controller
    {
        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Index(DateTime? month, CancellationToken ct, DateTime? date = null,
            DateTime? endDate = null, int? portId = null, int? terminalId = null, string? search = null, string? status = null, string? view = null)
        {
            if (!ModelState.IsValid || search?.Length > 120)
            {
                return BadRequest("Choose a valid month and a search of at most 120 characters.");
            }

            var today = DateTimeHelper.GetCurrentPhilippineTime().Date;
            var selected = month ?? date ?? today;
            var start = new DateTime(selected.Year, selected.Month, 1);
            if (start.Year < 1900 || start.Year > 9998
                || (date.HasValue && (date.Value.Year < 1900 || date.Value.Year > 9998))
                || (endDate.HasValue && (endDate.Value.Year < 1900 || endDate.Value.Year > 9998)))
            {
                return BadRequest("Choose a month between 1900 and 9998.");
            }

            var gridStart = start.AddDays(-(int)start.DayOfWeek);
            var selectedDate = date?.Date;
            var selectedEndDate = endDate?.Date;

            if (!selectedDate.HasValue)
            {
                selectedDate = start.Year == today.Year && start.Month == today.Month ? today : start;
            }
            else if (!selectedEndDate.HasValue && (selectedDate.Value < gridStart || selectedDate.Value >= gridStart.AddDays(42)))
            {
                selectedDate = new DateTime(start.Year, start.Month, Math.Min(selectedDate.Value.Day, DateTime.DaysInMonth(start.Year, start.Month)));
            }

            if (selectedEndDate.HasValue)
            {
                if (selectedEndDate.Value < selectedDate.Value)
                {
                    (selectedDate, selectedEndDate) = (selectedEndDate, selectedDate);
                }

                if ((selectedEndDate.Value - selectedDate.Value).TotalDays > 62)
                {
                    selectedEndDate = selectedDate.Value.AddDays(62);
                }

                if (selectedEndDate.Value == selectedDate.Value)
                {
                    selectedEndDate = null;
                }
            }

            var result = await scheduleService.GetCalendarAsync(new VesselScheduleBoardViewModel
            {
                Date = start,
                SelectedDate = selectedDate,
                SelectedEndDate = selectedEndDate,
                PortId = portId,
                TerminalId = terminalId,
                Search = search,
                Status = status,
                ViewMode = view ?? "calendar"
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.CanPrepareRates = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ManageRates);
            ViewBag.CanApproveRates = userId != null && await accessControl.HasAccessAsync(userId, ProcedureEnum.ApproveRateUpload);
            ViewBag.CanManageRates = ViewBag.CanPrepareRates == true || ViewBag.CanApproveRates == true;
            ViewBag.CanCaptureRates = ViewBag.CanManageRates;
            var canViewRates = ViewBag.CanConfirm == true || ViewBag.CanManageRates == true;
            var rateCaptures = canViewRates
                ? await rates.GetScheduleCapturesAsync(schedule, ct)
                : new List<ScheduleRateCaptureViewModel>();
            ViewBag.RateCaptures = rateCaptures;
            ViewBag.RateSetup = canViewRates
                ? await rates.GetScheduleRateSetupAsync(schedule, rateCaptures, ct)
                : null;
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
            ViewBag.CanCancel = schedule.CustomerOrderSlipId == null || (job?.Status == MsapConstants.CustomerOrderSlipStatus.Open && !hasActiveTickets && !hasBilling);
            var isPendingReview = !closed
                && (!schedule.CustomerOrderSlipId.HasValue || schedule.ApprovedRevision != schedule.Revision || schedule.Status == MsapConstants.VesselScheduleStatus.Tentative);
            ViewBag.CanAdoptTableRate = ViewBag.CanConfirm == true
                && isPendingReview
                && !hasTickets
                && !hasBilling
                && (job == null || job.Status == MsapConstants.CustomerOrderSlipStatus.Open);
            var isPendingReapproval = isPendingReview && schedule.ApprovedRevision.HasValue;
            if (isPendingReapproval)
            {
                var (changes, addedTugboatIds, addedEntryIds) = await LoadRevisionChangesAsync(id, ct);
                ViewBag.RevisionChanges = changes;
                ViewBag.AddedTugboatIds = addedTugboatIds;
                ViewBag.AddedServiceEntryIds = addedEntryIds;
            }
            else
            {
                ViewBag.RevisionChanges = new List<MsapFieldChange>();
                ViewBag.AddedTugboatIds = new HashSet<int>();
                ViewBag.AddedServiceEntryIds = new HashSet<int>();
            }
            return View(schedule);
        }

        [HttpGet]
        [RequireAccess(ProcedureEnum.ViewOperations)]
        public async Task<IActionResult> Revisions(int id, string? field, CancellationToken ct)
        {
            var schedule = await LoadDetailsAsync(id, ct);
            if (schedule == null)
            {
                return NotFound();
            }

            var adjustmentLogs = (await unitOfWork.MsapAdjustmentLog.GetAllAsync(
                a => a.DocumentType == "VesselSchedule" && a.RecordId == id, ct))
                .OrderByDescending(a => a.EditedDate)
                .ThenByDescending(a => a.Id)
                .ToList();

            var revisions = new List<ScheduleRevisionEntryViewModel>();
            foreach (var log in adjustmentLogs)
            {
                List<MsapFieldChange> parsed = [];
                if (!string.IsNullOrWhiteSpace(log.ChangesJson))
                {
                    try
                    {
                        parsed = JsonSerializer.Deserialize<List<MsapFieldChange>>(log.ChangesJson) ?? [];
                    }
                    catch (JsonException)
                    {
                        parsed = [];
                    }
                }

                var visible = parsed
                    .Where(c => c.Field is not "_AddedTugboatIds" and not "_AddedServiceEntryIds")
                    .ToList();
                revisions.Add(new ScheduleRevisionEntryViewModel
                {
                    EditedDate = log.EditedDate,
                    EditedBy = log.EditedBy,
                    Reason = log.Reason,
                    Changes = visible
                });
            }

            var auditLogs = (await unitOfWork.AuditTrail.GetAllAsync(
                a => a.DocumentType == "Vessel Schedule" && a.RecordId == id, ct))
                .OrderByDescending(a => a.Date)
                .ThenByDescending(a => a.Id)
                .ToList();

            foreach (var audit in auditLogs.Where(a => a.Activity.StartsWith("Re-approval revision:", StringComparison.Ordinal)))
            {
                if (revisions.Any(r => Math.Abs((r.EditedDate - audit.Date).TotalSeconds) <= 5
                    && string.Equals(r.EditedBy, audit.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var parsed = ParseRevisionChangesFromActivity(audit.Activity);
                await ResolveFallbackTugboatNamesAsync(parsed, ct);
                revisions.Add(new ScheduleRevisionEntryViewModel
                {
                    EditedDate = audit.Date,
                    EditedBy = audit.Username,
                    Reason = "Re-approval revision",
                    Changes = parsed
                });
            }

            var chronologicalRevisions = revisions
                .OrderBy(r => r.EditedDate)
                .ToList();

            var netChanges = chronologicalRevisions
                .SelectMany(r => r.Changes)
                .GroupBy(c => c.Field, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.First();
                    var last = g.Last();
                    return new ScheduleNetChangeViewModel
                    {
                        Field = first.Field,
                        Label = last.Label,
                        OriginalValue = first.Before ?? string.Empty,
                        CurrentValue = last.After ?? string.Empty,
                        RevisionCount = g.Count(),
                        DeltaHint = FormatScheduleTimeDelta(first.Field, first.Before, last.After)
                    };
                })
                .ToList();

            foreach (var audit in auditLogs.Where(a =>
                a.Activity.Contains("Confirmed", StringComparison.OrdinalIgnoreCase)
                || a.Activity.Contains("Approved", StringComparison.OrdinalIgnoreCase)
                || a.Activity.Contains("Reapproved", StringComparison.OrdinalIgnoreCase)))
            {
                revisions.Add(new ScheduleRevisionEntryViewModel
                {
                    EditedDate = audit.Date,
                    EditedBy = audit.Username,
                    Reason = audit.Activity,
                    IsApprovalCheckpoint = true
                });
            }

            revisions = revisions
                .OrderByDescending(r => r.EditedDate)
                .ThenByDescending(r => r.IsApprovalCheckpoint)
                .ToList();

            return View(new ScheduleRevisionViewModel
            {
                Schedule = schedule,
                FocusedField = field,
                NetChanges = netChanges,
                Revisions = revisions
            });
        }

        private static string? FormatScheduleTimeDelta(string field, string? before, string? after)
        {
            if (field is not (nameof(VesselSchedule.PlannedStart) or nameof(VesselSchedule.PlannedEnd)))
            {
                return null;
            }
            if (!DateTime.TryParse(before, out var b) || !DateTime.TryParse(after, out var a))
            {
                return null;
            }
            var diff = a - b;
            if (diff == TimeSpan.Zero)
            {
                return null;
            }
            var sign = diff < TimeSpan.Zero ? "-" : "+";
            var abs = diff.Duration();
            if (abs.Days > 0)
            {
                return $"{sign}{abs.Days}d {abs.Hours}h {abs.Minutes}m";
            }
            return abs.Hours > 0 ? $"{sign}{abs.Hours}h {abs.Minutes}m" : $"{sign}{abs.Minutes}m";
        }

        private async Task<(List<MsapFieldChange> Changes, HashSet<int> AddedTugboatIds, HashSet<int> AddedEntryIds)> LoadRevisionChangesAsync(int scheduleId, CancellationToken ct)
        {
            List<MsapFieldChange> rawChanges = [];
            var adjustmentLogs = await unitOfWork.MsapAdjustmentLog.GetAllAsync(
                a => a.DocumentType == "VesselSchedule" && a.RecordId == scheduleId, ct);
            var latestAdjustment = adjustmentLogs
                .OrderByDescending(a => a.EditedDate)
                .ThenByDescending(a => a.Id)
                .FirstOrDefault();
            if (latestAdjustment != null && !string.IsNullOrWhiteSpace(latestAdjustment.ChangesJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<MsapFieldChange>>(latestAdjustment.ChangesJson);
                    if (parsed is { Count: > 0 })
                    {
                        rawChanges = parsed;
                    }
                }
                catch (JsonException)
                {
                    // Fall back to AuditTrail activity text if ChangesJson is malformed.
                }
            }

            if (rawChanges.Count == 0)
            {
                var logs = await unitOfWork.AuditTrail.GetAllAsync(
                    a => a.DocumentType == "Vessel Schedule" && a.RecordId == scheduleId, ct);
                var latestRevisionLog = logs
                    .Where(a => a.Activity.StartsWith("Re-approval revision:", StringComparison.Ordinal))
                    .OrderByDescending(a => a.Date)
                    .ThenByDescending(a => a.Id)
                    .FirstOrDefault();
                if (latestRevisionLog != null)
                {
                    rawChanges = ParseRevisionChangesFromActivity(latestRevisionLog.Activity);
                    await ResolveFallbackTugboatNamesAsync(rawChanges, ct);
                }
            }

            HashSet<int> addedTugboatIds = [];
            HashSet<int> addedEntryIds = [];
            List<MsapFieldChange> visibleChanges = [];
            foreach (var change in rawChanges)
            {
                if (change.Field == "_AddedTugboatIds")
                {
                    foreach (var part in (change.After ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (int.TryParse(part, out var tugId))
                        {
                            addedTugboatIds.Add(tugId);
                        }
                    }
                    continue;
                }
                if (change.Field == "_AddedServiceEntryIds")
                {
                    foreach (var part in (change.After ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (int.TryParse(part, out var entryId))
                        {
                            addedEntryIds.Add(entryId);
                        }
                    }
                    continue;
                }
                visibleChanges.Add(change);
            }

            return (visibleChanges, addedTugboatIds, addedEntryIds);
        }

        private async Task ResolveFallbackTugboatNamesAsync(List<MsapFieldChange> changes, CancellationToken ct)
        {
            var tugChange = changes.FirstOrDefault(c => c.Field == nameof(VesselSchedule.AssignedTugboatIds));
            if (tugChange == null || string.IsNullOrWhiteSpace(tugChange.Before) || !tugChange.Before.StartsWith("[", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var oldIds = JsonSerializer.Deserialize<List<int>>(tugChange.Before) ?? [];
                var newIds = string.IsNullOrWhiteSpace(tugChange.After)
                    ? []
                    : JsonSerializer.Deserialize<List<int>>(tugChange.After) ?? [];
                var allIds = oldIds.Concat(newIds).Distinct().ToList();
                var tugNames = allIds.Count > 0
                    ? (await unitOfWork.Tugboat.GetAllAsync(t => allIds.Contains(t.TugboatId), ct))
                        .ToDictionary(t => t.TugboatId, t => t.TugboatName)
                    : new Dictionary<int, string>();
                tugChange.Before = oldIds.Count == 0 ? "None" : string.Join(", ", oldIds.Select(i => tugNames.TryGetValue(i, out var n) ? n : $"Tug #{i}"));
                tugChange.After = newIds.Count == 0 ? "None" : string.Join(", ", newIds.Select(i => tugNames.TryGetValue(i, out var n) ? n : $"Tug #{i}"));
                var addedIds = newIds.Except(oldIds).ToList();
                if (addedIds.Count > 0)
                {
                    changes.Add(new MsapFieldChange { Field = "_AddedTugboatIds", Label = "_AddedTugboatIds", Before = "", After = string.Join(",", addedIds) });
                }
            }
            catch (JsonException)
            {
                // Leave original text if not JSON IDs.
            }
        }

        private static List<MsapFieldChange> ParseRevisionChangesFromActivity(string activity)
        {
            const string marker = "Changes: ";
            var start = activity.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return [];
            }

            start += marker.Length;
            var end = activity.IndexOf(". Overlap override:", start, StringComparison.Ordinal);
            var summary = (end >= 0 ? activity[start..end] : activity[start..]).Trim();
            if (string.IsNullOrWhiteSpace(summary) || summary.Equals("No field values changed", StringComparison.Ordinal))
            {
                return [];
            }

            List<MsapFieldChange> changes = [];
            foreach (var rawPart in summary.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseArrowChange(rawPart, "Planned start ", nameof(VesselSchedule.PlannedStart), "Planned start", out var plannedStart))
                {
                    changes.Add(plannedStart);
                }
                else if (TryParseArrowChange(rawPart, "Planned end ", nameof(VesselSchedule.PlannedEnd), "Planned end", out var plannedEnd))
                {
                    changes.Add(plannedEnd);
                }
                else if (TryParseArrowChange(rawPart, "Voyage ", nameof(VesselSchedule.VoyageNumber), "Voyage number", out var voyage))
                {
                    changes.Add(voyage);
                }
                else if (TryParseArrowChange(rawPart, "Customer ID ", nameof(VesselSchedule.CustomerId), "Customer", out var customer))
                {
                    changes.Add(customer);
                }
                else if (TryParseArrowChange(rawPart, "Vessel ID ", nameof(VesselSchedule.VesselId), "Vessel", out var vessel))
                {
                    changes.Add(vessel);
                }
                else if (TryParseArrowChange(rawPart, "Port ID ", nameof(VesselSchedule.PortId), "Port", out var port))
                {
                    changes.Add(port);
                }
                else if (TryParseArrowChange(rawPart, "Terminal ID ", nameof(VesselSchedule.TerminalId), "Terminal", out var terminal))
                {
                    changes.Add(terminal);
                }
                else if (TryParseArrowChange(rawPart, "Assigned tugboats ", nameof(VesselSchedule.AssignedTugboatIds), "Assigned tugboats", out var tugs))
                {
                    changes.Add(tugs);
                }
                else if (TryParseArrowChange(rawPart, "SBMA share ", nameof(VesselSchedule.SbmaEnabled), "SBMA share", out var sbma))
                {
                    changes.Add(sbma);
                }
                else if (rawPart.StartsWith("Notes updated", StringComparison.Ordinal))
                {
                    changes.Add(new MsapFieldChange { Field = nameof(VesselSchedule.Notes), Label = "Notes", Before = "", After = "Updated" });
                }
                else if (rawPart.StartsWith("Services updated", StringComparison.Ordinal))
                {
                    changes.Add(new MsapFieldChange { Field = nameof(VesselSchedule.ServiceEntries), Label = "Tugboats & services", Before = "", After = rawPart });
                }
            }

            return changes;
        }

        private static bool TryParseArrowChange(string part, string prefix, string field, string label, out MsapFieldChange change)
        {
            change = default!;
            if (!part.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var rest = part[prefix.Length..];
            var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow < 0)
            {
                return false;
            }

            var before = rest[..arrow].Trim().Trim('\'');
            var after = rest[(arrow + 4)..].Trim().Trim('\'');
            change = new MsapFieldChange { Field = field, Label = label, Before = before, After = after };
            return true;
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ApproveSchedule)]
        public async Task<IActionResult> AdoptTableRate(int scheduleId, int entryId, RateTableKind kind, CancellationToken ct)
        {
            var schedule = await LoadDetailsAsync(scheduleId, ct);
            if (schedule == null)
            {
                return NotFound(new { message = "Schedule not found." });
            }
            if (schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
            {
                return BadRequest(new { message = "Completed or cancelled schedules cannot be modified." });
            }
            if (schedule.CustomerOrderSlipId.HasValue
                && schedule.ApprovedRevision == schedule.Revision
                && schedule.Status != MsapConstants.VesselScheduleStatus.Tentative)
            {
                return BadRequest(new { message = "Only schedules pending approval or reapproval can adopt table rates." });
            }
            var job = schedule.CustomerOrderSlipId.HasValue
                ? await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == schedule.CustomerOrderSlipId.Value, ct)
                : null;
            var hasTickets = (schedule.CustomerOrderSlipId.HasValue && await unitOfWork.DispatchTicket.GetAsync(t => t.CustomerOrderSlipId == schedule.CustomerOrderSlipId.Value, ct) != null)
                || (schedule.ServiceEntries.Count > 0 && await unitOfWork.DispatchTicket.GetAsync(t => schedule.ServiceEntries.Select(e => (int?)e.VesselScheduleServiceEntryId).Contains(t.VesselScheduleServiceEntryId), ct) != null);
            var hasBilling = schedule.CustomerOrderSlipId.HasValue && await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == schedule.CustomerOrderSlipId, ct) != null;
            if (hasTickets || hasBilling || (job != null && job.Status != MsapConstants.CustomerOrderSlipStatus.Open))
            {
                return BadRequest(new { message = "Cannot adopt table rates once Dispatch Tickets, billing, or a closed COS exist." });
            }
            var entry = schedule.ServiceEntries.SingleOrDefault(e => e.VesselScheduleServiceEntryId == entryId);
            if (entry == null || entry.CancelledAt.HasValue)
            {
                return BadRequest(new { message = "Service entry not found." });
            }
            var rateDate = DateOnly.FromDateTime(schedule.PlannedStart);
            decimal? tableRate;
            if (kind == RateTableKind.Tariff)
            {
                var rate = await unitOfWork.TariffTable.GetApplicableRateAsync(schedule.CustomerId.GetValueOrDefault(), schedule.TerminalId, entry.ServiceId, rateDate, ct);
                if (rate == null || rate.Dispatch <= 0)
                {
                    return BadRequest(new { message = "No valid rate found in rate table to adopt." });
                }
                tableRate = rate.Dispatch;
            }
            else
            {
                var rate = await unitOfWork.BafTable.GetApplicableRateAsync(schedule.CustomerId.GetValueOrDefault(), schedule.TerminalId, entry.ServiceId, rateDate, ct);
                if (rate == null || rate.AsOfDate < ScheduledTicketCharges.BafCycleStart(rateDate))
                {
                    return BadRequest(new { message = "No valid current-cycle BAF rate found in rate table to adopt." });
                }
                tableRate = rate.Amount;
            }
            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (kind == RateTableKind.Tariff)
                {
                    entry.ManualTariffRate = null;
                    entry.IsManualTariffOverride = false;
                }
                else
                {
                    entry.ManualBafRate = null;
                }
                var actorName = User.Identity?.Name ?? "system";
                schedule.EditedBy = actorName;
                schedule.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(actorName,
                    $"Adopted {kind} table rate ({tableRate:N2}) for booking #{scheduleId} entry #{entryId}",
                    "Vessel Schedule", scheduleId), ct);
                await unitOfWork.SaveAsync(ct);
            }, ct);
            return Json(new { success = true, newRate = tableRate?.ToString("N2"), rate = tableRate });
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
        public async Task<IActionResult> Confirm(int id, DateTime reviewedAt, bool allowConflicts, CancellationToken ct,
            string? rateAcknowledgement = null)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Review the schedule again before confirming." });
            }
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return StatusCode(503, new { success = false, message = "Rate review is unavailable. Try again." });
            }

            var (result, acknowledgement) = await rates.ConfirmScheduleAsync(scheduleService, id, reviewedAt,
                allowConflicts,
                rateAcknowledgement, User.Identity?.Name ?? "system", userId, ct);
            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
            }
            return Json(new
            {
                success = result.IsSuccess,
                message = result.Message,
                rateAcknowledgement = acknowledgement,
                requiresConflictAcknowledgement = !allowConflicts && result.Message?.StartsWith("Overlapping plans:", StringComparison.Ordinal) == true,
                redirectUrl = result.IsSuccess ? Url.Action("Details", "CustomerOrderSlip", new { area = "MSAP", id = result.Data }) : null
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> Cancel(int id, string? reason, CancellationToken ct)
        {
            return await ChangeStatusAsync(id, MsapConstants.VesselScheduleStatus.Cancelled, reason, ct);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.EditCustomerOrderSlip)]
        public async Task<IActionResult> Complete(int id, CancellationToken ct)
        {
            return await ChangeStatusAsync(id, MsapConstants.VesselScheduleStatus.Completed, null, ct);
        }

        private async Task<IActionResult> ChangeStatusAsync(int id, string status, string? reason, CancellationToken ct)
        {
            var result = await scheduleService.ChangeStatusAsync(id, status, User.Identity?.Name ?? "system", reason, ct);
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
        [RequireAnyAccess(ProcedureEnum.ApproveSchedule, ProcedureEnum.ManageRates, ProcedureEnum.ApproveRateUpload)]
        public async Task<IActionResult> GetCustomerDiscount(int customerId, CancellationToken ct)
        {
            var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == customerId, ct);
            return Json(new { defaultDiscount = customer?.DefaultCosDiscountPercent ?? 0m });
        }

        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.ApproveSchedule, ProcedureEnum.ManageRates, ProcedureEnum.ApproveRateUpload)]
        public async Task<IActionResult> GetTariffRate(int customerId, int terminalId, int serviceId, DateTime? date, CancellationToken ct)
        {
            if (customerId <= 0 || serviceId <= 0)
            {
                return Json(new { exists = false });
            }

            var asOfDate = DateOnly.FromDateTime(date ?? DateTimeHelper.GetCurrentPhilippineTime());

            var rate = await unitOfWork.TariffTable.GetApplicableRateAsync(customerId, terminalId, serviceId, asOfDate, ct);

            var bafRate = await unitOfWork.BafTable.GetApplicableRateAsync(customerId, terminalId, serviceId, asOfDate, ct);
            if (bafRate?.AsOfDate < ScheduledTicketCharges.BafCycleStart(asOfDate))
            {
                bafRate = null;
            }

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
            var terminals = await unitOfWork.Terminal.GetAllQuery(t => t.PortId == portId)
                .OrderBy(t => t.TerminalName)
                .Select(t => new { value = t.TerminalId.ToString(), text = t.TerminalName, isIoc = t.IsIoc })
                .ToListAsync(ct);
            return Json(terminals);
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
            vm.Vessels = await unitOfWork.Vessel.GetAllQuery()
                .OrderBy(v => v.VesselName)
                .Select(v => new SelectListItem { Value = v.VesselId.ToString(), Text = v.VesselName + " (" + v.VesselNumber + ")" })
                .ToListAsync(ct);
            vm.Ports = await unitOfWork.Port.GetMsapPortsSelectList(ct);
            var allTugboats = await unitOfWork.Tugboat.GetAllQuery()
                .OrderBy(t => t.TugboatName)
                .Select(t => new { t.TugboatId, t.TugboatName, t.TugboatNumber, t.IsCompanyOwned })
                .ToListAsync(ct);
            vm.Tugboats = allTugboats
                .Select(t => new SelectListItem { Value = t.TugboatId.ToString(), Text = $"{t.TugboatName} (#{t.TugboatNumber})" })
                .ToList();
            ViewBag.TugboatOwnerMap = allTugboats.ToDictionary(t => t.TugboatId, t => t.IsCompanyOwned);
            vm.Services = await unitOfWork.Service.GetAllQuery()
                .OrderBy(s => s.ServiceName)
                .Select(s => new SelectListItem { Value = s.ServiceId.ToString(), Text = s.ServiceName })
                .ToListAsync(ct);

            var portId = selectedPortId ?? vm.PortId;
            if (portId > 0)
            {
                var terminals = await unitOfWork.Terminal.GetAllQuery(t => t.PortId == portId)
                    .OrderBy(t => t.TerminalName)
                    .Select(t => new { t.TerminalId, t.TerminalName, t.IsIoc })
                    .ToListAsync(ct);
                ViewBag.TerminalIocMap = terminals.ToDictionary(t => t.TerminalId, t => t.IsIoc);
                vm.Terminals = terminals
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
                IsIoc = vm.IsIoc,
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
                        TariffBasis = TariffChargeBasis.PerHour,
                        BafMethod = BafCalculationMethod.PerHour,
                        ManualTariffRate = null,
                        ManualBafRate = null
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
                IsIoc = entity.IsIoc,
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
