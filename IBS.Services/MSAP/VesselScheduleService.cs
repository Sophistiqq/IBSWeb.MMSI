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
                model.Revision = 1;
                model.ApprovedRevision = null;
                model.ApprovedByUserId = null;
                model.ApprovedAt = null;

                if (model.CosDiscountPercent == null && !model.SkipCosDiscount && model.CustomerId.HasValue)
                {
                    var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == model.CustomerId.Value, ct);
                    if (customer != null && customer.DefaultCosDiscountPercent > 0)
                    {
                        model.CosDiscountPercent = customer.DefaultCosDiscountPercent;
                    }
                }

                var error = await ValidateAsync(model, allowConflicts, ct);
                if (error != null) return ServiceResult<int>.Failure(error, ServiceResultStatus.ValidationError);

                if (model.ServiceEntries.Any(e => e.VesselScheduleServiceEntryId != 0))
                {
                    return ServiceResult<int>.Failure("New expected services must not contain saved row identifiers.", ServiceResultStatus.ValidationError);
                }

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

                    JobOrder? job = null;
                    if (existing.JobOrderId.HasValue)
                    {
                        job = await unitOfWork.JobOrder.GetForUpdateAsync(existing.JobOrderId.Value, ct);
                        if (job == null)
                        {
                            result = ServiceResult.Failure("Linked Job Order not found.", ServiceResultStatus.NotFound);
                            return;
                        }
                        if (job.Status != MsapConstants.JobOrderStatus.Open)
                        {
                            result = ServiceResult.Failure("This booking already has service records or billing, or its Job Order is no longer open. Schedule revision is only available before Dispatch Tickets or billing exist.", ServiceResultStatus.ValidationError);
                            return;
                        }
                        var oldPeriodClosed = await unitOfWork.PostedPeriod.IsMonthClosedAsync(job.Date.Year, job.Date.Month, ct);
                        var newDate = DateOnly.FromDateTime(model.PlannedStart);
                        if (oldPeriodClosed || await unitOfWork.PostedPeriod.IsMonthClosedAsync(newDate.Year, newDate.Month, ct))
                        {
                            result = ServiceResult.Failure("Cannot revise a booking in a closed period.", ServiceResultStatus.ValidationError);
                            return;
                        }
                    }

                    var existingTickets = existing.JobOrderId.HasValue
                        ? (await unitOfWork.DispatchTicket.GetAllAsync(t => t.JobOrderId == existing.JobOrderId.Value, ct)).ToList()
                        : [];
                    var entryIds = existing.ServiceEntries.Select(e => e.VesselScheduleServiceEntryId).ToList();
                    var entryTickets = entryIds.Count > 0
                        ? (await unitOfWork.DispatchTicket.GetAllAsync(t => t.VesselScheduleServiceEntryId.HasValue && entryIds.Contains(t.VesselScheduleServiceEntryId.Value), ct)).ToList()
                        : [];
                    bool hasTickets = existingTickets.Count > 0 || entryTickets.Count > 0;
                    bool hasBilling = existing.JobOrderId.HasValue && await unitOfWork.Billing.GetAsync(b => b.JobOrderId == existing.JobOrderId.Value, ct) != null;

                    if (hasTickets || hasBilling)
                    {
                        result = ServiceResult.Failure("This booking already has service records or billing, or its Job Order is no longer open. Schedule revision is only available before Dispatch Tickets or billing exist.", ServiceResultStatus.ValidationError);
                        return;
                    }

                    var savedEntryIds = existing.ServiceEntries.Select(e => e.VesselScheduleServiceEntryId).ToHashSet();
                    var submittedEntryIds = model.ServiceEntries.Where(e => e.VesselScheduleServiceEntryId != 0).Select(e => e.VesselScheduleServiceEntryId).ToList();
                    if (submittedEntryIds.Any(id => !savedEntryIds.Contains(id)) || submittedEntryIds.Distinct().Count() != submittedEntryIds.Count)
                    {
                        result = ServiceResult.Failure("Expected service rows changed. Reload this booking before editing.", ServiceResultStatus.ValidationError);
                        return;
                    }

                    model.Status = MsapConstants.VesselScheduleStatus.Tentative;
                    var error = await ValidateAsync(model, allowConflicts, ct);
                    if (error != null)
                    {
                        result = ServiceResult.Failure(error, ServiceResultStatus.ValidationError);
                        return;
                    }

                    if (existing.JobOrderId.HasValue && job != null)
                    {
                        // Revision-aware reapproval: preserve the existing COS/JobOrder identity while incrementing revision and requiring reapproval
                        existing.Revision += 1;
                        existing.Status = MsapConstants.VesselScheduleStatus.Tentative;

                        job.CustomerId = model.CustomerId!.Value;
                        job.VesselId = model.VesselId;
                        job.PortId = model.PortId;
                        job.TerminalId = model.TerminalId;
                        job.Date = DateOnly.FromDateTime(model.PlannedStart);
                        job.PlannedStartTime = model.PlannedStart;
                        job.PlannedEndTime = model.PlannedEnd;
                        job.VoyageNumber = model.VoyageNumber;
                        job.Remarks = model.Notes;
                        List<int> tugs = JsonSerializer.Deserialize<List<int>>(model.AssignedTugboatIds ?? "[]") ?? [];
                        job.RequiredTugCount = Math.Max(tugs.Count, 1);
                        job.PreferredTugboatId = tugs.Count > 0 ? tugs[0] : null;
                        job.EditedBy = username;
                        job.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                            $"Updated Job Order #{job.JobOrderNumber} for revised vessel schedule #{existing.VesselScheduleId} (Revision {existing.Revision}).", "Job Order", job.JobOrderId, job.JobOrderNumber), ct);
                    }
                    else
                    {
                        existing.Status = MsapConstants.VesselScheduleStatus.Tentative;
                        existing.JobOrderId = null;
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
                    existing.CosDiscountPercent = model.CosDiscountPercent;
                    existing.SkipCosDiscount = model.SkipCosDiscount;
                    existing.SbmaEnabled = model.SbmaEnabled;
                    existing.EditedBy = username;
                    existing.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    // Sync expected service entries
                    var incomingEntries = model.ServiceEntries ?? [];
                    var incomingIds = incomingEntries.Where(e => e.VesselScheduleServiceEntryId > 0).Select(e => e.VesselScheduleServiceEntryId).ToHashSet();
                    var toRemove = existing.ServiceEntries.Where(e => !incomingIds.Contains(e.VesselScheduleServiceEntryId)).ToList();
                    if (toRemove.Count > 0)
                    {
                        foreach (var rem in toRemove)
                        {
                            existing.ServiceEntries.Remove(rem);
                        }
                        unitOfWork.VesselSchedule.RemoveServiceEntries(toRemove);
                    }
                    foreach (var existingEntry in existing.ServiceEntries)
                    {
                        var matching = incomingEntries.FirstOrDefault(e => e.VesselScheduleServiceEntryId == existingEntry.VesselScheduleServiceEntryId);
                        if (matching != null)
                        {
                            existingEntry.TugboatId = matching.TugboatId;
                            existingEntry.ServiceId = matching.ServiceId;
                            existingEntry.TariffBasis = matching.TariffBasis;
                            existingEntry.BafMethod = matching.BafMethod;
                            existingEntry.ManualTariffRate = matching.ManualTariffRate;
                            existingEntry.ManualBafRate = matching.ManualBafRate;
                        }
                    }
                    foreach (var newEntry in incomingEntries.Where(e => e.VesselScheduleServiceEntryId == 0))
                    {
                        existing.ServiceEntries.Add(new VesselScheduleServiceEntry
                        {
                            VesselScheduleId = existing.VesselScheduleId,
                            TugboatId = newEntry.TugboatId,
                            ServiceId = newEntry.ServiceId,
                            TariffBasis = newEntry.TariffBasis,
                            BafMethod = newEntry.BafMethod,
                            ManualTariffRate = newEntry.ManualTariffRate,
                            ManualBafRate = newEntry.ManualBafRate
                        });
                    }

                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Updated vessel schedule #{model.VesselScheduleId} (Revision {existing.Revision}). Overlap override: {allowConflicts}", "Vessel Schedule", model.VesselScheduleId), ct);
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult.Success(existing.JobOrderId.HasValue
                        ? $"Schedule updated (Revision {existing.Revision}). Reapproval is required before ticket entry."
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

        public async Task<ServiceResult<int>> ConfirmAsync(int id, DateTime reviewedAt, string username, CancellationToken ct = default, bool allowConflicts = false, string? userId = null)
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

                    // Idempotent repeated confirmation check
                    if (schedule.JobOrderId.HasValue && schedule.ApprovedRevision == schedule.Revision)
                    {
                        result = ServiceResult<int>.Success(schedule.JobOrderId.Value, "This schedule is already confirmed.");
                        return;
                    }

                    if (reviewedAt != (schedule.EditedDate ?? schedule.CreatedDate))
                    {
                        result = ServiceResult<int>.Failure("The booking changed. Review its current details before confirming.", ServiceResultStatus.ValidationError);
                        return;
                    }

                    var approverUserId = userId ?? (await unitOfWork.UserAccess.GetAsync(u => u.UserName == username, ct))?.UserId ?? username;

                    var error = await ValidateAsync(schedule, allowConflicts, ct, confirming: true);
                    if (error != null)
                    {
                        result = ServiceResult<int>.Failure(error, ServiceResultStatus.ValidationError);
                        return;
                    }

                    if (!schedule.JobOrderId.HasValue)
                    {
                        var created = await jobOrderService.CreateJobOrderAsync(BuildJobOrder(schedule), username, ct);
                        if (!created.IsSuccess)
                        {
                            result = ServiceResult<int>.Failure(created.Message ?? "Job Order creation failed.", created.Status);
                            throw new InvalidOperationException(result.Message);
                        }
                        schedule.JobOrderId = created.Data;
                    }
                    else
                    {
                        // Preserve existing JobOrder identity and sync details
                        var job = await unitOfWork.JobOrder.GetForUpdateAsync(schedule.JobOrderId.Value, ct);
                        if (job?.Status != MsapConstants.JobOrderStatus.Open
                            || await unitOfWork.DispatchTicket.GetAsync(t => t.JobOrderId == schedule.JobOrderId, ct) != null
                            || await unitOfWork.Billing.GetAsync(b => b.JobOrderId == schedule.JobOrderId, ct) != null)
                        {
                            result = ServiceResult<int>.Failure("Cannot reapprove a booking once tickets or billing exist, or its Job Order is closed.", ServiceResultStatus.ValidationError);
                            return;
                        }
                        if (await unitOfWork.PostedPeriod.IsMonthClosedAsync(job.Date.Year, job.Date.Month, ct))
                        {
                            result = ServiceResult<int>.Failure("Cannot reapprove a booking in a closed period.", ServiceResultStatus.ValidationError);
                            return;
                        }
                        var updatedJob = BuildJobOrder(schedule);
                        job.CustomerId = updatedJob.CustomerId;
                        job.VesselId = updatedJob.VesselId;
                        job.PortId = updatedJob.PortId;
                        job.TerminalId = updatedJob.TerminalId;
                        job.Date = updatedJob.Date;
                        job.PlannedStartTime = updatedJob.PlannedStartTime;
                        job.PlannedEndTime = updatedJob.PlannedEndTime;
                        job.VoyageNumber = updatedJob.VoyageNumber;
                        job.Remarks = updatedJob.Remarks;
                        job.RequiredTugCount = updatedJob.RequiredTugCount;
                        job.PreferredTugboatId = updatedJob.PreferredTugboatId;
                        job.EditedBy = username;
                        job.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    }

                    // Revision-aware approval evidence
                    schedule.ApprovedRevision = schedule.Revision;
                    schedule.ApprovedByUserId = approverUserId;
                    schedule.ApprovedAt = DateTimeHelper.GetCurrentPhilippineTime();

                    if (schedule.Status != MsapConstants.VesselScheduleStatus.InProgress)
                    {
                        schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
                    }
                    schedule.EditedBy = username;
                    schedule.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                        $"Confirmed vessel schedule #{id} revision #{schedule.Revision}; linked Job Order #{schedule.JobOrderId}. Overlap override: {allowConflicts}", "Vessel Schedule", id), ct);
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult<int>.Success(schedule.JobOrderId.Value, "Schedule confirmed and Job Order created successfully.");
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
                    if (existing.ServiceEntries.Count > 0)
                    {
                        unitOfWork.VesselSchedule.RemoveServiceEntries(existing.ServiceEntries.ToList());
                        existing.ServiceEntries.Clear();
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
            return await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(id, ct);
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

            if (model.CosDiscountPercent.HasValue && (model.CosDiscountPercent.Value < 0 || model.CosDiscountPercent.Value > 100))
            {
                return "COS discount percentage must be between 0% and 100%.";
            }

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

            if (model.ServiceEntries != null)
            {
                var serviceIds = model.ServiceEntries.Select(e => e.ServiceId).Distinct().ToList();
                var validServices = (await unitOfWork.Service.GetAllAsync(service => serviceIds.Contains(service.ServiceId), ct)).Select(service => service.ServiceId).ToHashSet();
                foreach (var entry in model.ServiceEntries)
                {
                    if (!tugIds.Contains(entry.TugboatId))
                    {
                        return "Each expected service must belong to an assigned tugboat.";
                    }
                    if (entry.TariffBasis.HasValue && !Enum.IsDefined(entry.TariffBasis.Value)
                        || entry.BafMethod.HasValue && !Enum.IsDefined(entry.BafMethod.Value))
                    {
                        return "Choose a valid tariff basis and BAF method.";
                    }
                    if (!validServices.Contains(entry.ServiceId))
                    {
                        return "Choose a valid service for each service entry.";
                    }
                    if (entry.ManualTariffRate.HasValue && (entry.ManualTariffRate.Value < 0 || entry.ManualTariffRate.Value > 99999999999999.9999m))
                    {
                        return "Manual tariff rate must be between 0 and 99,999,999,999,999.9999.";
                    }
                    if (entry.ManualBafRate.HasValue && (entry.ManualBafRate.Value < 0 || entry.ManualBafRate.Value > 99999999999999.9999m))
                    {
                        return "Manual BAF rate must be between 0 and 99,999,999,999,999.9999.";
                    }
                }
            }

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
