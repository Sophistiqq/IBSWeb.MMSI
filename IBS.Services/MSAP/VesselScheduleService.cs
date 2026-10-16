using System.Text.Json;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class VesselScheduleService(
        IUnitOfWork unitOfWork,
        JobOrderService jobOrderService,
        ILogger<VesselScheduleService> logger) : IVesselScheduleService
    {
        public async Task<ServiceResult<VesselScheduleBoardViewModel>> GetCalendarAsync(VesselScheduleBoardViewModel filters, CancellationToken ct = default)
        {
            var gridStart = filters.Date.AddDays(-(int)filters.Date.DayOfWeek);
            int? portId = filters.PortId;
            int? terminalId = filters.TerminalId;
            string? search = filters.Search;
            string? status = filters.Status;
            var schedules = (await GetSchedulesAsync(gridStart, gridStart.AddDays(42), ct)).ToList();
            var ports = (await unitOfWork.Port.GetAllAsync(null, ct)).OrderBy(p => p.PortName).ToList();
            if (portId.HasValue && ports.All(p => p.PortId != portId))
            {
                return ServiceResult<VesselScheduleBoardViewModel>.Failure("Choose a valid port.", ServiceResultStatus.ValidationError);
            }
            var terminals = (await unitOfWork.Terminal.GetAllAsync(t => !portId.HasValue || t.PortId == portId, ct))
                .OrderBy(t => t.TerminalName).ToList();
            if (terminalId.HasValue && terminals.All(t => t.TerminalId != terminalId))
            {
                terminalId = null;
            }
            var board = new VesselScheduleBoardViewModel
            {
                Date = filters.Date,
                SelectedDate = filters.SelectedDate,
                PortId = portId,
                TerminalId = terminalId,
                Search = search?.Trim(),
                Status = status,
                Schedules = schedules,
                Tugboats = (await unitOfWork.Tugboat.GetAllAsync(null, ct)).OrderBy(t => t.TugboatName).ToList(),
                Ports = ports.Select(p => new SelectListItem(p.PortName, p.PortId.ToString())).ToList(),
                Terminals = terminals.Select(t => new SelectListItem(t.TerminalName, t.TerminalId.ToString())).ToList(),
                LocationHeading = $"{ports.FirstOrDefault(p => p.PortId == portId)?.PortName ?? "All ports"} / {terminals.FirstOrDefault(t => t.TerminalId == terminalId)?.TerminalName ?? "All terminals"}"
            };
            await MarkConflictsAsync(board, gridStart, gridStart.AddDays(42), ct);
            board.TugBookings = schedules.Where(s => s.Status != MsapConstants.VesselScheduleStatus.Cancelled).ToList();
            var matchingTugIds = board.Tugboats.Where(t => t.TugboatName.Contains(board.Search ?? "", StringComparison.OrdinalIgnoreCase)
                || t.TugboatNumber.Contains(board.Search ?? "", StringComparison.OrdinalIgnoreCase)).Select(t => t.TugboatId).ToHashSet();
            board.Schedules = schedules.Where(s => (!portId.HasValue || s.PortId == portId)
                && (!terminalId.HasValue || s.TerminalId == terminalId)
                && (string.IsNullOrEmpty(status) || s.Status == status)
                && (string.IsNullOrEmpty(board.Search)
                    || s.Vessel.VesselName.Contains(board.Search, StringComparison.OrdinalIgnoreCase)
                    || (s.VoyageNumber?.Contains(board.Search, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (JsonSerializer.Deserialize<List<int>>(s.AssignedTugboatIds ?? "[]") ?? []).Any(matchingTugIds.Contains))).ToList();
            return ServiceResult<VesselScheduleBoardViewModel>.Success(board);
        }

        private async Task MarkConflictsAsync(VesselScheduleBoardViewModel board, DateTime from, DateTime to, CancellationToken ct)
        {
            if (board.Schedules.Count == 0)
            {
                return;
            }

            var tugboats = board.Tugboats.Count > 0 ? board.Tugboats : await unitOfWork.Tugboat.GetAllAsync(null, ct);
            var names = tugboats.ToDictionary(t => t.TugboatId, t => t.TugboatName);
            // shortcut: pairwise checks in the visible window; index intervals if schedule volume grows.
            foreach (var schedule in board.Schedules)
            {
                var conflicts = await CheckConflictsAsync(schedule, ct, board.Schedules, names);
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

        public async Task<ServiceResult<int>> CreateAsync(VesselSchedule model, string username, CancellationToken ct = default, bool allowConflicts = false)
        {
            try
            {
                model.Status = MsapConstants.VesselScheduleStatus.Tentative;
                model.JobOrderId = null;
                var error = await ValidateAsync(model, allowConflicts, ct);
                if (error != null) return ServiceResult<int>.Failure(error, ServiceResultStatus.ValidationError);

                model.CreatedBy = username;
                model.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    await unitOfWork.VesselSchedule.AddAsync(model, ct);
                    await unitOfWork.SaveAsync(ct);
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                        $"Created vessel schedule #{model.VesselScheduleId} ({model.Status}, {model.PlannedStart:MM/dd HH:mm} – {model.PlannedEnd:MM/dd HH:mm}). Overlap override: {allowConflicts}",
                        "Vessel Schedule", model.VesselScheduleId), ct);
                    await unitOfWork.SaveAsync(ct);
                }, ct);

                return ServiceResult<int>.Success(model.VesselScheduleId, "Schedule created successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create vessel schedule");
                return ServiceResult<int>.Failure("Failed to create schedule. Please try again.");
            }
        }

        public async Task<ServiceResult> UpdateAsync(VesselSchedule model, string username, CancellationToken ct = default, bool allowConflicts = false)
        {
            var result = ServiceResult.Failure("Failed to update schedule. Please try again.");
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var existing = await unitOfWork.VesselSchedule.GetForUpdateAsync(model.VesselScheduleId, ct);
                    if (existing == null)
                    {
                        result = ServiceResult.Failure("Schedule not found.", ServiceResultStatus.NotFound);
                        return;
                    }
                    if (existing.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        result = ServiceResult.Failure("Cannot edit a completed or cancelled schedule.", ServiceResultStatus.ValidationError);
                        return;
                    }
                    model.Status = existing.JobOrderId.HasValue ? MsapConstants.VesselScheduleStatus.Tentative : existing.Status;
                    var error = await ValidateAsync(model, allowConflicts, ct);
                    if (error != null)
                    {
                        result = ServiceResult.Failure(error, ServiceResultStatus.ValidationError);
                        return;
                    }
                    bool keepJobOrder = existing.JobOrderId.HasValue && HasOnlyAddedTugboats(existing, model);
                    if (existing.JobOrderId.HasValue)
                    {
                        var job = await unitOfWork.JobOrder.GetForUpdateAsync(existing.JobOrderId.Value, ct);
                        if (job == null)
                        {
                            result = ServiceResult.Failure("Linked Job Order not found.", ServiceResultStatus.NotFound);
                            return;
                        }
                        if (job.Status != MsapConstants.JobOrderStatus.Open
                            || await unitOfWork.DispatchTicket.GetAsync(t => t.JobOrderId == job.JobOrderId, ct) != null
                            || await unitOfWork.Billing.GetAsync(b => b.JobOrderId == job.JobOrderId, ct) != null)
                        {
                            result = ServiceResult.Failure("This booking already has service records or billing, or its Job Order is no longer open. Schedule revision is only available before Dispatch Tickets or billing exist.", ServiceResultStatus.ValidationError);
                            return;
                        }
                        var oldPeriodClosed = await unitOfWork.PostedPeriod.IsMonthClosedAsync(job.Date.Year, job.Date.Month, ct);
                        var date = DateOnly.FromDateTime(model.PlannedStart);
                        if (oldPeriodClosed || await unitOfWork.PostedPeriod.IsMonthClosedAsync(date.Year, date.Month, ct))
                        {
                            result = ServiceResult.Failure("Cannot revise a booking in a closed period.", ServiceResultStatus.ValidationError);
                            return;
                        }
                        job.EditedBy = username;
                        job.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                        if (keepJobOrder)
                        {
                            List<int> tugs = JsonSerializer.Deserialize<List<int>>(model.AssignedTugboatIds ?? "[]") ?? [];
                            job.RequiredTugCount = Math.Max(tugs.Count, 1);
                            await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                                $"Updated assigned tugboat count on Job Order #{job.JobOrderNumber} for vessel schedule #{existing.VesselScheduleId}.", "Job Order", job.JobOrderId, job.JobOrderNumber), ct);
                        }
                        else
                        {
                            job.Status = MsapConstants.JobOrderStatus.Invalidated;
                            await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                                $"Invalidated Job Order #{job.JobOrderNumber} after revision of vessel schedule #{existing.VesselScheduleId}; retained for reference.", "Job Order", job.JobOrderId, job.JobOrderNumber), ct);
                            existing.JobOrderId = null;
                            existing.JobOrder = null;
                            existing.Status = MsapConstants.VesselScheduleStatus.Tentative;
                        }
                    }
                    existing.CustomerId = model.CustomerId;
                    existing.VesselId = model.VesselId;
                    existing.PortId = model.PortId;
                    existing.TerminalId = model.TerminalId;
                    existing.PlannedStart = model.PlannedStart;
                    existing.PlannedEnd = model.PlannedEnd;
                    existing.AssignedTugboatIds = model.AssignedTugboatIds;
                    existing.VoyageNumber = model.VoyageNumber;
                    existing.VesselType = model.VesselType;
                    existing.Notes = model.Notes;
                    existing.EditedBy = username;
                    existing.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Updated vessel schedule #{model.VesselScheduleId}. Overlap override: {allowConflicts}", "Vessel Schedule", model.VesselScheduleId), ct);
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult.Success(keepJobOrder
                        ? "Assigned tugboats updated. The booking remains confirmed with the same Job Order."
                        : "Schedule updated. Review and confirm the booking before creating its Job Order.");
                }, ct);

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update vessel schedule {Id}", model.VesselScheduleId);
                return result.IsSuccess ? ServiceResult.Failure("Failed to update schedule. Please try again.") : result;
            }
        }

        private static bool HasOnlyAddedTugboats(VesselSchedule existing, VesselSchedule updated)
        {
            List<int> previousTugs = JsonSerializer.Deserialize<List<int>>(existing.AssignedTugboatIds ?? "[]") ?? [];
            List<int> updatedTugs = JsonSerializer.Deserialize<List<int>>(updated.AssignedTugboatIds ?? "[]") ?? [];
            return existing.CustomerId == updated.CustomerId && existing.VesselId == updated.VesselId
                && existing.PortId == updated.PortId && existing.TerminalId == updated.TerminalId
                && existing.PlannedStart == updated.PlannedStart && existing.PlannedEnd == updated.PlannedEnd
                && existing.VoyageNumber == updated.VoyageNumber && existing.VesselType == updated.VesselType
                && existing.Notes == updated.Notes && previousTugs.All(updatedTugs.Contains);
        }

        public async Task<ServiceResult<int>> ConfirmAsync(int id, DateTime reviewedAt, string username, CancellationToken ct = default, bool allowConflicts = false)
        {
            var result = ServiceResult<int>.Failure("Failed to confirm schedule. Please try again.");
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var schedule = await unitOfWork.VesselSchedule.GetForUpdateAsync(id, ct);
                    if (schedule == null)
                    {
                        result = ServiceResult<int>.Failure("Schedule not found.", ServiceResultStatus.NotFound);
                        return;
                    }
                    if (schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        result = ServiceResult<int>.Failure("Completed or cancelled schedules cannot be confirmed.", ServiceResultStatus.ValidationError);
                        return;
                    }
                    if (schedule.JobOrderId.HasValue)
                    {
                        result = ServiceResult<int>.Success(schedule.JobOrderId.Value, "This schedule already has a Job Order.");
                        return;
                    }
                    if (reviewedAt != (schedule.EditedDate ?? schedule.CreatedDate))
                    {
                        result = ServiceResult<int>.Failure("The booking changed. Review its current details before confirming.", ServiceResultStatus.ValidationError);
                        return;
                    }
                    var error = await ValidateAsync(schedule, allowConflicts, ct, confirming: true);
                    if (error != null)
                    {
                        result = ServiceResult<int>.Failure(error, ServiceResultStatus.ValidationError);
                        return;
                    }
                    var created = await jobOrderService.CreateJobOrderAsync(BuildJobOrder(schedule), username, ct);
                    if (!created.IsSuccess)
                    {
                        result = ServiceResult<int>.Failure(created.Message ?? "Job Order creation failed.", created.Status);
                        throw new InvalidOperationException(result.Message);
                    }
                    schedule.JobOrderId = created.Data;
                    if (schedule.Status != MsapConstants.VesselScheduleStatus.InProgress)
                    {
                        schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
                    }
                    schedule.EditedBy = username;
                    schedule.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                        $"Confirmed vessel schedule #{id}; linked Job Order #{created.Data}. Overlap override: {allowConflicts}", "Vessel Schedule", id), ct);
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult<int>.Success(created.Data, "Schedule confirmed and Job Order created successfully.");
                }, ct);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to confirm vessel schedule {Id}", id);
                return result.IsSuccess ? ServiceResult<int>.Failure("Failed to confirm schedule. Please try again.") : result;
            }
        }

        public async Task<ServiceResult> ChangeStatusAsync(int id, string status, string username, CancellationToken ct = default)
        {
            if (status is not (MsapConstants.VesselScheduleStatus.Cancelled or MsapConstants.VesselScheduleStatus.Completed))
            {
                return ServiceResult.Failure("Choose Cancel Schedule or Complete Schedule.", ServiceResultStatus.ValidationError);
            }
            var result = ServiceResult.Failure("Failed to change schedule status. Please try again.");
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var schedule = await unitOfWork.VesselSchedule.GetForUpdateAsync(id, ct);
                    if (schedule == null)
                    {
                        result = ServiceResult.Failure("Schedule not found.", ServiceResultStatus.NotFound);
                        return;
                    }
                    if (schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled
                        || (status == MsapConstants.VesselScheduleStatus.Completed && schedule.JobOrderId == null))
                    {
                        result = ServiceResult.Failure("This schedule cannot make that transition.", ServiceResultStatus.ValidationError);
                        return;
                    }
                    if (schedule.JobOrderId.HasValue)
                    {
                        result = status == MsapConstants.VesselScheduleStatus.Cancelled
                            ? await jobOrderService.CancelJobOrderAsync(schedule.JobOrderId.Value, username, ct)
                            : await jobOrderService.CompleteBookingAsync(schedule.JobOrderId.Value, username, ct);
                        if (!result.IsSuccess)
                        {
                            throw new InvalidOperationException(result.Message);
                        }
                        return;
                    }
                    schedule.Status = status;
                    schedule.EditedBy = username;
                    schedule.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Vessel schedule #{id} marked {status}; booking records retained.", "Vessel Schedule", id), ct);
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult.Success($"Schedule marked {status.ToLowerInvariant()}. Linked operational records were retained.");
                }, ct);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to change vessel schedule {Id}", id);
                return result.IsSuccess ? ServiceResult.Failure("Failed to change schedule status. Please try again.") : result;
            }
        }

        private static JobOrder BuildJobOrder(VesselSchedule schedule)
        {
            var tugs = JsonSerializer.Deserialize<List<int>>(schedule.AssignedTugboatIds ?? "[]") ?? [];
            return new JobOrder
            {
                Date = DateOnly.FromDateTime(schedule.PlannedStart),
                CustomerId = schedule.CustomerId!.Value,
                VesselId = schedule.VesselId,
                PortId = schedule.PortId,
                TerminalId = schedule.TerminalId,
                VoyageNumber = schedule.VoyageNumber,
                PlannedStartTime = schedule.PlannedStart,
                PlannedEndTime = schedule.PlannedEnd,
                PreferredTugboatId = tugs.Count > 0 ? tugs[0] : null,
                RequiredTugCount = Math.Max(tugs.Count, 1),
                Remarks = schedule.Notes
            };
        }

        public async Task<ServiceResult> DeleteAsync(int id, string username, CancellationToken ct = default)
        {
            try
            {
                var result = ServiceResult.Failure("Schedule not found.", ServiceResultStatus.NotFound);
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var existing = await unitOfWork.VesselSchedule.GetForUpdateAsync(id, ct);
                    if (existing == null)
                    {
                        return;
                    }
                    if (existing.Status != MsapConstants.VesselScheduleStatus.Tentative || existing.JobOrderId != null)
                    {
                        result = ServiceResult.Failure("Only tentative schedules without a Job Order can be deleted. Cancel an active schedule to retain its history.", ServiceResultStatus.ValidationError);
                        return;
                    }
                    await unitOfWork.VesselSchedule.RemoveAsync(existing, ct);
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Deleted vessel schedule #{id}", "Vessel Schedule", id), ct);
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult.Success("Schedule deleted successfully.");
                }, ct);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete vessel schedule {Id}", id);
                return ServiceResult.Failure("Failed to delete schedule. Please try again.");
            }
        }

        public async Task<VesselSchedule?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await unitOfWork.VesselSchedule.GetAsync(s => s.VesselScheduleId == id, ct);
        }

        public async Task<IEnumerable<VesselSchedule>> GetSchedulesAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            return await unitOfWork.VesselSchedule.GetSchedulesWithDetailsAsync(from, to, ct);
        }

        private async Task<string?> ValidateAsync(VesselSchedule model, bool allowConflicts, CancellationToken ct, bool confirming = false)
        {
            if (model.CustomerId is not > 0 || await unitOfWork.Customer.GetAsync(c => c.CustomerId == model.CustomerId, ct) == null)
            {
                return "Choose a valid customer before saving or confirming the schedule.";
            }
            if (model.PlannedStart.Year < 1900 || model.PlannedEnd.Year > 9998 || model.PlannedEnd <= model.PlannedStart)
                return "Use dates between 1900 and 9998, with planned end after planned start.";
            if (model.Status is not (MsapConstants.VesselScheduleStatus.Tentative or MsapConstants.VesselScheduleStatus.Confirmed
                or MsapConstants.VesselScheduleStatus.InProgress or MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled))
                return "Choose a valid schedule status.";
            var vessel = await unitOfWork.Vessel.GetAsync(v => v.VesselId == model.VesselId, ct);
            if (vessel == null) return "Choose a valid vessel.";
            var terminal = await unitOfWork.Terminal.GetAsync(t => t.TerminalId == model.TerminalId && t.PortId == model.PortId, ct);
            if (terminal == null) return "Choose a terminal belonging to the selected port.";
            model.VesselType = vessel.VesselType == "FOREIGN" ? "Foreign" : "Local";
            List<int> tugIds;
            try
            {
                tugIds = string.IsNullOrEmpty(model.AssignedTugboatIds)
                    ? [] : JsonSerializer.Deserialize<List<int>>(model.AssignedTugboatIds) ?? [];
            }
            catch (JsonException) { return "Invalid tugboat assignment."; }
            tugIds = tugIds.Distinct().ToList();
            var tugs = await unitOfWork.Tugboat.GetAllAsync(t => tugIds.Contains(t.TugboatId), ct);
            if (tugs.Count() != tugIds.Count) return "Choose valid tugboats.";
            model.AssignedTugboatIds = tugIds.Count == 0 ? null : JsonSerializer.Serialize(tugIds);
            if (model.Status == MsapConstants.VesselScheduleStatus.Cancelled) return null;
            if ((confirming || model.Status is MsapConstants.VesselScheduleStatus.Confirmed or MsapConstants.VesselScheduleStatus.InProgress)
                && tugIds.Count == 0)
            {
                return "Assign at least one tugboat before confirming the schedule. Use Tentative while planning.";
            }
            var conflicts = await CheckConflictsAsync(model, ct);
            if (conflicts.Count > 0 && !allowConflicts)
                return "Overlapping plans: " + string.Join(" ", conflicts.Select(c => c.Message))
                    + " Adjust the booking or review both confirmation alerts to acknowledge and save it.";
            return null;
        }

        public async Task<List<ScheduleConflict>> CheckConflictsAsync(VesselSchedule schedule, CancellationToken ct = default,
            IEnumerable<VesselSchedule>? candidates = null, IReadOnlyDictionary<int, string>? tugboatNames = null)
        {
            var conflicts = new List<ScheduleConflict>();
            if (schedule.Status == MsapConstants.VesselScheduleStatus.Cancelled) return conflicts;
            var from = schedule.PlannedStart;
            var to = schedule.PlannedEnd;
            var allSchedules = candidates ?? await GetSchedulesAsync(from, to, ct);
            var others = allSchedules.Where(s => s.VesselScheduleId != schedule.VesselScheduleId
                && s.Status != MsapConstants.VesselScheduleStatus.Cancelled
                && s.PlannedStart < schedule.PlannedEnd && s.PlannedEnd > schedule.PlannedStart).ToList();
            foreach (var s in others.Where(s => s.VesselId == schedule.VesselId))
            {
                conflicts.Add(new ScheduleConflict
                {
                    Type = "Vessel",
                    Message = $"Vessel '{s.Vessel.VesselName}' already has schedule #{s.VesselScheduleId} ({s.PlannedStart:MMM d HH:mm} – {s.PlannedEnd:MMM d HH:mm}).",
                    ConflictingScheduleId = s.VesselScheduleId,
                    ConflictingVessel = s.Vessel.VesselName,
                    ConflictStart = s.PlannedStart,
                    ConflictEnd = s.PlannedEnd
                });
            }

            // Terminal overlap
            foreach (var s in others.Where(s =>
                s.TerminalId == schedule.TerminalId &&
                s.PlannedStart < schedule.PlannedEnd &&
                s.PlannedEnd > schedule.PlannedStart))
            {
                conflicts.Add(new ScheduleConflict
                {
                    Type = "Terminal",
                    Message = $"Terminal '{s.Terminal.TerminalName}' has an overlapping plan for '{s.Vessel.VesselName}'.",
                    ConflictingScheduleId = s.VesselScheduleId,
                    ConflictingVessel = s.Vessel.VesselName,
                    ConflictStart = s.PlannedStart,
                    ConflictEnd = s.PlannedEnd
                });
            }

            // Tugboat overlap
            var tugboatIds = string.IsNullOrEmpty(schedule.AssignedTugboatIds)
                ? new List<int>()
                : JsonSerializer.Deserialize<List<int>>(schedule.AssignedTugboatIds) ?? new();

            if (tugboatIds.Count > 0)
            {
                tugboatNames ??= (await unitOfWork.Tugboat.GetAllAsync(t => tugboatIds.Contains(t.TugboatId), ct))
                    .ToDictionary(t => t.TugboatId, t => t.TugboatName);

                foreach (var s in others)
                {
                    var otherTugIds = string.IsNullOrEmpty(s.AssignedTugboatIds)
                        ? new List<int>()
                        : JsonSerializer.Deserialize<List<int>>(s.AssignedTugboatIds) ?? new();
                    if (otherTugIds.Count == 0)
                    {
                        continue;
                    }

                    var shared = tugboatIds.Intersect(otherTugIds).ToList();
                    if (shared.Count > 0 &&
                        s.PlannedStart < schedule.PlannedEnd &&
                        s.PlannedEnd > schedule.PlannedStart)
                    {
                        conflicts.Add(new ScheduleConflict
                        {
                            Type = "Tugboat",
                            Message = $"Tugboat(s) '{string.Join(", ", shared.Select(id => tugboatNames.GetValueOrDefault(id, $"#{id}")))}' assigned to '{s.Vessel.VesselName}'.",
                            ConflictingScheduleId = s.VesselScheduleId,
                            ConflictingVessel = s.Vessel.VesselName,
                            ConflictStart = s.PlannedStart,
                            ConflictEnd = s.PlannedEnd
                        });
                    }
                }
            }

            return conflicts;
        }
    }
}
