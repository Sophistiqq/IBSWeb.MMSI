using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class DispatchTicketService(
        IUnitOfWork unitOfWork,
        ICloudStorageService cloudStorageService,
        ILogger<DispatchTicketService> logger)
    {
        public async Task<DispatchTicket?> GetDispatchTicketByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await unitOfWork.DispatchTicket.GetDispatchTicketWithDetailsAsync(id, cancellationToken);
        }

        public async Task<DispatchTicketViewModel> PopulateDispatchTicketViewModelAsync(DispatchTicketViewModel? viewModel, int? customerOrderSlipId, CancellationToken cancellationToken)
        {
            viewModel ??= new DispatchTicketViewModel();

            if (customerOrderSlipId.HasValue)
            {
                var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == customerOrderSlipId.Value, cancellationToken);
                if (customerOrderSlip != null)
                {
                    viewModel.CustomerOrderSlipId = customerOrderSlipId;
                    viewModel.CustomerId = customerOrderSlip.CustomerId;
                    viewModel.VesselId = customerOrderSlip.VesselId;
                    viewModel.PortId = customerOrderSlip.PortId;
                    viewModel.TerminalId = customerOrderSlip.TerminalId;
                    viewModel.VoyageNumber = customerOrderSlip.VoyageNumber;
                    viewModel.COSNumber = customerOrderSlip.CustomerOrderSlipNumber;
                    viewModel.Date = viewModel.VesselScheduleServiceEntryId.HasValue
                        ? viewModel.Date == default ? DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()) : viewModel.Date
                        : customerOrderSlip.Date;
                }
            }

            viewModel = await PopulateSelectListsAsync(viewModel, cancellationToken);
            if (viewModel.CustomerOrderSlipId.HasValue)
            {
                List<int>? assigned = await GetAssignedTugboatsAsync(viewModel.CustomerOrderSlipId.Value, cancellationToken);
                if (assigned != null)
                {
                    viewModel.Tugboats = viewModel.Tugboats?.Where(t => int.TryParse(t.Value, out int id) && assigned.Contains(id)).ToList();
                }
            }

            return viewModel;
        }

        private async Task<List<int>?> GetAssignedTugboatsAsync(int customerOrderSlipId, CancellationToken ct)
        {
            var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, ct);
            return schedule == null ? null : JsonSerializer.Deserialize<List<int>>(schedule.AssignedTugboatIds ?? "[]") ?? [];
        }

        public async Task<ServiceResult<int>> CreateDispatchTicketAsync(DispatchTicketViewModel viewModel, IFormFile? imageFile, IFormFile? videoFile, string username, CancellationToken cancellationToken)
        {
            DispatchTicket? created = null;
            try
            {
                if (!viewModel.CustomerOrderSlipId.HasValue)
                {
                    return ServiceResult<int>.Failure("Dispatch tickets must be created under a COS.");
                }

                string? readinessError = await ValidateReadyAsync(viewModel, cancellationToken);
                if (readinessError != null)
                {
                    return ServiceResult<int>.Failure(readinessError);
                }

                if (!await IsCustomerOrderSlipEditableAsync(viewModel.CustomerOrderSlipId, cancellationToken))
                {
                    return ServiceResult<int>.Failure("Cannot add ticket — parent COS is cancelled or closed.");
                }

                if (viewModel.CustomerOrderSlipId.HasValue && await unitOfWork.DispatchTicket.GetAsync(dt => dt.CustomerOrderSlipId == viewModel.CustomerOrderSlipId && dt.Status == MsapConstants.DispatchTicketStatus.Billed, cancellationToken) != null)
                {
                    return ServiceResult<int>.Failure("Cannot add ticket — COS already has billed tickets.");
                }

                List<int>? assignedTugs = await GetAssignedTugboatsAsync(viewModel.CustomerOrderSlipId.Value, cancellationToken);
                if (assignedTugs != null && !assignedTugs.Contains(viewModel.TugBoatId))
                {
                    return ServiceResult<int>.Failure("Choose a tugboat assigned to this vessel schedule.");
                }

                var scheduled = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == viewModel.CustomerOrderSlipId, cancellationToken);
                if (scheduled != null)
                {
                    scheduled = await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(scheduled.VesselScheduleId, cancellationToken);
                }
                if (scheduled?.ServiceEntries.Count > 0 || viewModel.VesselScheduleServiceEntryId.HasValue)
                {
                    var expected = scheduled?.ServiceEntries.SingleOrDefault(e => e.VesselScheduleServiceEntryId == viewModel.VesselScheduleServiceEntryId);
                    if (expected == null || expected.CancelledAt.HasValue || expected.TugboatId != viewModel.TugBoatId || expected.ServiceId != viewModel.ServiceId)
                    {
                        return ServiceResult<int>.Failure("Choose an expected service from this COS before recording its ticket.");
                    }
                    var actualError = await ValidateExpectedActualAsync(viewModel, cancellationToken);
                    if (actualError != null)
                    {
                        return ServiceResult<int>.Failure(actualError);
                    }
                }

                var model = viewModel.ToEntity();
                created = model;

                model.CreatedBy = username;
                model.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();

                // Logic from Repository.AddAsync
                if (model.CustomerOrderSlipId.HasValue)
                {
                    var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetCustomerOrderSlipWithDetailsAsync(model.CustomerOrderSlipId.Value, cancellationToken);
                    if (customerOrderSlip != null)
                    {
                        model.CustomerId = customerOrderSlip.CustomerId;
                        model.VesselId = customerOrderSlip.VesselId;
                        model.PortId = customerOrderSlip.PortId;
                        model.TerminalId = customerOrderSlip.TerminalId;
                        model.VoyageNumber = customerOrderSlip.VoyageNumber;
                        model.COSNumber = customerOrderSlip.CustomerOrderSlipNumber;
                        model.Date = model.VesselScheduleServiceEntryId.HasValue ? viewModel.Date : customerOrderSlip.Date;
                    }
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return ServiceResult<int>.Failure(guard.Message!);
                }

                if (model is { DateLeft: not null, DateArrived: not null, TimeLeft: not null, TimeArrived: not null })
                {
                    var start = model.DateLeft.Value.ToDateTime(model.TimeLeft.Value);
                    var end = model.DateArrived.Value.ToDateTime(model.TimeArrived.Value);

                    if (end <= start)
                    {
                        return ServiceResult<int>.Failure("Arrival Date/Time must be strictly after Departure Date/Time.");
                    }

                    model.Status = MsapConstants.DispatchTicketStatus.ForTariff;
                    var duration = (decimal)(end - start).TotalHours;
                    model.TotalHours = Math.Round(Math.Max(duration, 1m), 2);
                }
                else
                {
                    model.Status = MsapConstants.DispatchTicketStatus.ForTariff;
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    if (scheduled != null)
                    {
                        await unitOfWork.VesselSchedule.GetForUpdateAsync(scheduled.VesselScheduleId, cancellationToken);
                    }
                    var parent = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(model.CustomerOrderSlipId!.Value, cancellationToken);
                    var booking = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == model.CustomerOrderSlipId, cancellationToken);
                    if (parent?.Status != MsapConstants.CustomerOrderSlipStatus.Open ||
                        booking?.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        throw new InvalidOperationException("Cannot add a ticket to a closed, cancelled or operationally completed booking.");
                    }
                    if (booking != null)
                    {
                        booking = await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(booking.VesselScheduleId, cancellationToken);
                    }
                    if (booking?.ServiceEntries.Count > 0 || model.VesselScheduleServiceEntryId.HasValue)
                    {
                        var expected = booking?.ServiceEntries.SingleOrDefault(e => e.VesselScheduleServiceEntryId == model.VesselScheduleServiceEntryId);
                        if (expected == null || expected.CancelledAt.HasValue || expected.TugboatId != model.TugBoatId || expected.ServiceId != model.ServiceId)
                        {
                            throw new InvalidOperationException("The expected service changed. Reload the COS before recording its ticket.");
                        }
                        if (await unitOfWork.DispatchTicket.GetAsync(t => t.VesselScheduleServiceEntryId == model.VesselScheduleServiceEntryId, cancellationToken) != null)
                        {
                            throw new InvalidOperationException("A ticket has already been recorded for this expected service.");
                        }
                    }
                    List<int>? currentTugs = await GetAssignedTugboatsAsync(model.CustomerOrderSlipId.Value, cancellationToken);
                    if (currentTugs != null && !currentTugs.Contains(model.TugBoatId))
                    {
                        throw new InvalidOperationException("Choose a tugboat assigned to this vessel schedule.");
                    }
                    readinessError = await ValidateReadyAsync(viewModel, cancellationToken);
                    if (readinessError != null)
                    {
                        throw new InvalidOperationException(readinessError);
                    }
                    model.CustomerId = parent.CustomerId;
                    model.VesselId = parent.VesselId;
                    model.PortId = parent.PortId;
                    model.TerminalId = parent.TerminalId;
                    model.VoyageNumber = parent.VoyageNumber;
                    model.COSNumber = parent.CustomerOrderSlipNumber;
                    model.Date = model.VesselScheduleServiceEntryId.HasValue ? viewModel.Date : parent.Date;
                    if (model.VesselScheduleServiceEntryId.HasValue)
                    {
                        var duration = model.DateArrived!.Value.ToDateTime(model.TimeArrived!.Value)
                            - model.DateLeft!.Value.ToDateTime(model.TimeLeft!.Value);
                        model.RawDurationMinutes = (int)duration.TotalMinutes;
                        model.RawDurationHours = (decimal)duration.TotalHours;
                    }
                    string? chargeBlocker = null;
                    if (booking != null && model.VesselScheduleServiceEntryId.HasValue)
                    {
                        chargeBlocker = await ScheduledTicketCharges.ApplyAsync(unitOfWork, model, booking, username, cancellationToken);
                    }
                    if (imageFile is { Length: > 0 })
                    {
                        var ext = Path.GetExtension(imageFile.FileName);
                        var name = Path.GetFileNameWithoutExtension(imageFile.FileName);
                        model.ImageName = $"{name}-img-{Guid.NewGuid():N}{ext}";
                        model.ImageSavedUrl = await cloudStorageService.UploadFileAsync(imageFile, model.ImageName);
                    }

                    if (videoFile is { Length: > 0 })
                    {
                        var ext = Path.GetExtension(videoFile.FileName);
                        var name = Path.GetFileNameWithoutExtension(videoFile.FileName);
                        model.VideoName = $"{name}-vid-{Guid.NewGuid():N}{ext}";
                        model.VideoSavedUrl = await cloudStorageService.UploadFileAsync(videoFile, model.VideoName);
                    }


                    await unitOfWork.DispatchTicket.AddAsync(model, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                    await RefreshSbmaAsync(model, cancellationToken);
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Create dispatch ticket #{model.DispatchNumber}{(chargeBlocker == null ? "" : $"; {chargeBlocker}")}", "Dispatch Ticket", model.DispatchTicketId, model.DispatchNumber), cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult<int>.Success(model.DispatchTicketId, $"Dispatch Ticket #{model.DispatchNumber} was successfully created.");
            }
            catch (Exception ex)
            {
                await DeleteFilesAsync([created?.ImageName, created?.VideoName]);
                logger.LogError(ex, "Failed to create dispatch ticket");
                return ServiceResult<int>.Failure($"Failed to create dispatch ticket: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult<TicketChargeReviewViewModel>> PreviewChargesAsync(int id, int customerOrderSlipId, CancellationToken ct)
        {
            var ticket = await unitOfWork.DispatchTicket.GetAsync(t => t.DispatchTicketId == id && t.CustomerOrderSlipId == customerOrderSlipId, ct);
            var booking = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, ct);
            var schedule = booking == null ? null : await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(booking.VesselScheduleId, ct);
            if (ticket?.VesselScheduleServiceEntryId == null || schedule == null
                || !schedule.ServiceEntries.Any(e => e.VesselScheduleServiceEntryId == ticket.VesselScheduleServiceEntryId))
            {
                return ServiceResult<TicketChargeReviewViewModel>.Failure("The scheduled ticket is unavailable.");
            }
            var preview = await ScheduledTicketCharges.PreviewAsync(unitOfWork, ticket, schedule, ct);
            var entry = schedule.ServiceEntries.Single(e => e.VesselScheduleServiceEntryId == ticket.VesselScheduleServiceEntryId);
            string? blocker = ScheduledTicketCharges.GetBlocker(preview.Ticket, schedule, entry, preview.Tariff, preview.Baf);
            if (blocker != null)
            {
                return ServiceResult<TicketChargeReviewViewModel>.Failure(blocker);
            }
            return ServiceResult<TicketChargeReviewViewModel>.Success(new TicketChargeReviewViewModel
            {
                DispatchTicketId = id, CustomerOrderSlipId = customerOrderSlipId, TicketNumber = ticket.DispatchNumber,
                HasSavedCharges = ticket.Status is MsapConstants.DispatchTicketStatus.ForBilling or MsapConstants.DispatchTicketStatus.Billed,
                SavedBafRate = ticket.BAFRate, ProposedBafRate = preview.Ticket.BAFRate,
                SavedTariffRate = ticket.DispatchRate, ProposedTariffRate = preview.Ticket.DispatchRate,
                SavedGross = ticket.TotalBilling, ProposedGross = preview.Ticket.TotalBilling,
                SavedNet = ticket.TotalNetRevenue, ProposedNet = preview.Ticket.TotalNetRevenue,
                Fingerprint = ScheduledTicketCharges.ReviewFingerprint(ticket, preview.Ticket, schedule)
            });
        }

        public async Task<ServiceResult> RecalculateChargesAsync(int id, int customerOrderSlipId, string username, CancellationToken ct, string? reviewedFingerprint = null)
        {
            try
            {
                var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, ct);
                if (schedule == null)
                {
                    return ServiceResult.Failure("The ticket must belong to an approved vessel schedule.");
                }
                var scheduleId = schedule.VesselScheduleId;
                var result = ServiceResult.Failure("The approved COS is unavailable.");
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    schedule = await unitOfWork.VesselSchedule.GetForUpdateAsync(scheduleId, ct);
                    var job = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(customerOrderSlipId, ct);
                    if (job?.Status != MsapConstants.CustomerOrderSlipStatus.Open || schedule == null
                        || schedule.ApprovedRevision != schedule.Revision
                        || schedule.Status is not (MsapConstants.VesselScheduleStatus.Confirmed or MsapConstants.VesselScheduleStatus.InProgress or MsapConstants.VesselScheduleStatus.Completed))
                    {
                        result = ServiceResult.Failure("Approve the current COS revision before recalculating charges.");
                        return;
                    }
                    var ticket = await unitOfWork.DispatchTicket.GetAsync(t => t.DispatchTicketId == id && t.CustomerOrderSlipId == customerOrderSlipId, ct);
                    if (ticket == null || !ticket.VesselScheduleServiceEntryId.HasValue)
                    {
                        result = ServiceResult.Failure("The expected service ticket was not found in this COS.");
                        return;
                    }
                    if (ticket.Status is not (MsapConstants.DispatchTicketStatus.CalculationPending or MsapConstants.DispatchTicketStatus.ForTariff or MsapConstants.DispatchTicketStatus.ForBilling
                            or MsapConstants.DispatchTicketStatus.ForApproval or MsapConstants.DispatchTicketStatus.Disapproved)
                        || await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == customerOrderSlipId, ct) != null
                        || await unitOfWork.DispatchTicket.GetAsync(t => t.CustomerOrderSlipId == customerOrderSlipId
                            && (t.BillingId.HasValue || t.Status == MsapConstants.DispatchTicketStatus.Billed), ct) != null)
                    {
                        result = ServiceResult.Failure("Only unbilled tickets without linked billing can be recalculated.");
                        return;
                    }
                    var periodBlocker = await GuardClosedPeriodAsync(ticket.Date, ct);
                    if (periodBlocker == null && ticket.DateLeft.HasValue)
                    {
                        periodBlocker = await GuardClosedPeriodAsync(ticket.DateLeft.Value, ct);
                    }
                    if (periodBlocker != null)
                    {
                        result = periodBlocker;
                        return;
                    }
                    schedule = await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(schedule.VesselScheduleId, ct);
                    var entry = schedule?.ServiceEntries.SingleOrDefault(e => e.VesselScheduleServiceEntryId == ticket.VesselScheduleServiceEntryId);
                    if (schedule == null || entry == null || entry.TugboatId != ticket.TugBoatId || entry.ServiceId != ticket.ServiceId)
                    {
                        result = ServiceResult.Failure("The ticket does not match its approved service requirement.");
                        return;
                    }
                    var preview = await ScheduledTicketCharges.PreviewAsync(unitOfWork, ticket, schedule, ct);
                    if (string.IsNullOrEmpty(reviewedFingerprint) || reviewedFingerprint != ScheduledTicketCharges.ReviewFingerprint(ticket, preview.Ticket, schedule))
                    {
                        result = ServiceResult.Failure("Ticket charges or applicable rates changed. Review the comparison again before recalculating.");
                        return;
                    }
                    decimal oldGross = ticket.TotalBilling;
                    decimal oldNet = ticket.TotalNetRevenue;
                    decimal oldBaf = ticket.BAFRate;
                    decimal oldTariff = ticket.DispatchRate;
                    bool oldManualTariff = ticket.IsManualTariffRate;
                    var blocker = ScheduledTicketCharges.Apply(ticket, schedule, entry, preview.Tariff, preview.Baf);
                    if (blocker != null)
                    {
                        throw new InvalidOperationException(blocker);
                    }
                    await RefreshSbmaAsync(ticket, ct);
                    ticket.TariffBy = username;
                    ticket.TariffDate = DateTimeHelper.GetCurrentPhilippineTime();
                    ticket.EditedBy = username;
                    ticket.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Recalculate charges for #{ticket.DispatchNumber}: tariff {oldTariff} -> {ticket.DispatchRate} (manual {oldManualTariff} -> {ticket.IsManualTariffRate}); BAF {oldBaf} -> {ticket.BAFRate}; gross {oldGross} -> {ticket.TotalBilling}; net {oldNet} -> {ticket.TotalNetRevenue}. {blocker ?? "Charges calculated from approved COS terms and service-date rates"}", "Dispatch Ticket", ticket.DispatchTicketId, ticket.DispatchNumber), ct);
                    await unitOfWork.SaveAsync(ct);
                    result = blocker == null ? ServiceResult.Success("Ticket charges recalculated successfully.") : ServiceResult.Failure(blocker);
                }, ct);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recalculate ticket {Id}", id);
                return ServiceResult.Failure("Unable to recalculate charges. Refresh the COS and try again.");
            }
        }

        public async Task<ServiceResult> UpdateDispatchTicketAsync(DispatchTicketViewModel viewModel, IFormFile? imageFile, IFormFile? videoFile, string username, CancellationToken cancellationToken)
        {
            DispatchTicket? ticket = null;
            string? originalImage = null;
            string? originalVideo = null;
            bool committed = false;
            try
            {
                ticket = await unitOfWork.DispatchTicket.GetAsync(t => t.DispatchTicketId == viewModel.DispatchTicketId, cancellationToken);
                originalImage = ticket?.ImageName;
                originalVideo = ticket?.VideoName;
                if (ticket?.VesselScheduleServiceEntryId == null)
                {
                    return ServiceResult.Failure("Link this ticket to an approved COS service before editing.");
                }
                if (viewModel.VesselScheduleServiceEntryId != ticket.VesselScheduleServiceEntryId)
                {
                    return ServiceResult.Failure("The ticket must retain its original COS service link.");
                }
                var result = ServiceResult.Failure("An open COS and active schedule are required before editing this ticket.");
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == ticket.CustomerOrderSlipId, cancellationToken);
                    if (schedule == null)
                    {
                        return;
                    }
                    schedule = await unitOfWork.VesselSchedule.GetForUpdateAsync(schedule.VesselScheduleId, cancellationToken);
                    var job = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(ticket.CustomerOrderSlipId!.Value, cancellationToken);
                    if (job?.Status != MsapConstants.CustomerOrderSlipStatus.Open || schedule == null
                        || schedule.Status is not (MsapConstants.VesselScheduleStatus.Tentative or MsapConstants.VesselScheduleStatus.Confirmed or MsapConstants.VesselScheduleStatus.InProgress or MsapConstants.VesselScheduleStatus.Completed))
                    {
                        return;
                    }
                    result = await UpdateDispatchTicketCoreAsync(viewModel, imageFile, videoFile, username, cancellationToken);
                    if (!result.IsSuccess)
                    {
                        throw new InvalidOperationException(result.Message);
                    }
                }, cancellationToken);
                committed = result.IsSuccess;
                return result;
            }
            catch (InvalidOperationException ex)
            {
                return ServiceResult.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update expected service ticket {Id}", viewModel.DispatchTicketId);
                return ServiceResult.Failure("Failed to update the ticket. Refresh the COS and try again.");
            }
            finally
            {
                if (ticket != null)
                {
                    await DeleteFilesAsync([
                        ticket.ImageName != originalImage ? committed ? originalImage : ticket.ImageName : null,
                        ticket.VideoName != originalVideo ? committed ? originalVideo : ticket.VideoName : null
                    ]);
                }
            }
        }

        private async Task<ServiceResult> UpdateDispatchTicketCoreAsync(DispatchTicketViewModel viewModel, IFormFile? imageFile, IFormFile? videoFile, string username, CancellationToken cancellationToken)
        {
            try
            {
                string? readinessError = await ValidateReadyAsync(viewModel, cancellationToken);
                if (readinessError != null)
                {
                    return ServiceResult.Failure(readinessError);
                }
                if (!await IsTicketCustomerOrderSlipEditableAsync(viewModel.DispatchTicketId!.Value, cancellationToken))
                {
                    return ServiceResult.Failure("Cannot edit ticket — parent COS is cancelled or closed.");
                }

                var currentModel = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == viewModel.DispatchTicketId, cancellationToken);
                if (currentModel == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(currentModel.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (currentModel.CustomerOrderSlipId.HasValue && await unitOfWork.DispatchTicket.GetAsync(dt => dt.CustomerOrderSlipId == currentModel.CustomerOrderSlipId && dt.Status == MsapConstants.DispatchTicketStatus.Billed, cancellationToken) != null)
                {
                    return ServiceResult.Failure("Cannot edit ticket — COS already has billed tickets.");
                }

                if (currentModel.VesselScheduleServiceEntryId.HasValue)
                {
                    if (viewModel.CustomerOrderSlipId != currentModel.CustomerOrderSlipId || viewModel.TugBoatId != currentModel.TugBoatId
                        || viewModel.ServiceId != currentModel.ServiceId || viewModel.CustomerId != currentModel.CustomerId
                        || viewModel.VesselId != currentModel.VesselId || viewModel.PortId != currentModel.PortId
                        || viewModel.TerminalId != currentModel.TerminalId)
                    {
                        return ServiceResult.Failure("The ticket must retain its approved COS, tugboat, service and booking details.");
                    }
                    var actualError = await ValidateExpectedActualAsync(viewModel, cancellationToken);
                    if (actualError != null)
                    {
                        return ServiceResult.Failure(actualError);
                    }
                }

                var originalTotalHours = currentModel.TotalHours;
                var changes = new List<string>();

                void AddChange(string name, object? oldVal, object? newVal)
                {
                    if (!Equals(oldVal, newVal))
                    {
                        changes.Add($"{name}: '{oldVal}' -> '{newVal}'");
                    }
                }

                // Date validation and total hours
                if (viewModel is { DateLeft: not null, DateArrived: not null, TimeLeft: not null, TimeArrived: not null })
                {
                    var departure = viewModel.DateLeft.Value.ToDateTime(viewModel.TimeLeft.Value);
                    var arrival = viewModel.DateArrived.Value.ToDateTime(viewModel.TimeArrived.Value);
                    if (arrival <= departure)
                    {
                        return ServiceResult.Failure("Date/Time Left cannot be later than Date/Time Arrived!");
                    }

                    var newTotalHours = Math.Round(Math.Max((decimal)(arrival - departure).TotalHours, 1m), 2);
                    AddChange("TotalHours", originalTotalHours, newTotalHours);
                    currentModel.TotalHours = newTotalHours;
                }

                // Map other fields and track changes
                AddChange(nameof(viewModel.Date), currentModel.Date, viewModel.Date);
                AddChange(nameof(viewModel.DispatchNumber), currentModel.DispatchNumber, viewModel.DispatchNumber);
                AddChange(nameof(viewModel.COSNumber), currentModel.COSNumber, viewModel.COSNumber);
                AddChange(nameof(viewModel.VoyageNumber), currentModel.VoyageNumber, viewModel.VoyageNumber);
                AddChange(nameof(viewModel.CustomerId), currentModel.CustomerId, viewModel.CustomerId);
                AddChange(nameof(viewModel.DateLeft), currentModel.DateLeft, viewModel.DateLeft);
                AddChange(nameof(viewModel.TimeLeft), currentModel.TimeLeft, viewModel.TimeLeft);
                AddChange(nameof(viewModel.DateArrived), currentModel.DateArrived, viewModel.DateArrived);
                AddChange(nameof(viewModel.TimeArrived), currentModel.TimeArrived, viewModel.TimeArrived);
                AddChange(nameof(viewModel.TerminalId), currentModel.TerminalId, viewModel.TerminalId);
                AddChange(nameof(viewModel.PortId), currentModel.PortId, viewModel.PortId);
                AddChange(nameof(viewModel.ServiceId), currentModel.ServiceId, viewModel.ServiceId);
                AddChange(nameof(viewModel.TugBoatId), currentModel.TugBoatId, viewModel.TugBoatId);
                AddChange(nameof(viewModel.TugMasterId), currentModel.TugMasterId, viewModel.TugMasterId);
                AddChange(nameof(viewModel.VesselId), currentModel.VesselId, viewModel.VesselId);
                AddChange(nameof(viewModel.Remarks), currentModel.Remarks, viewModel.Remarks);

                currentModel.Date = viewModel.Date;
                currentModel.DispatchNumber = viewModel.DispatchNumber;
                currentModel.COSNumber = viewModel.COSNumber;
                currentModel.VoyageNumber = viewModel.VoyageNumber;
                currentModel.CustomerId = viewModel.CustomerId;
                currentModel.DateLeft = viewModel.DateLeft;
                currentModel.TimeLeft = viewModel.TimeLeft;
                currentModel.DateArrived = viewModel.DateArrived;
                currentModel.TimeArrived = viewModel.TimeArrived;
                currentModel.TerminalId = viewModel.TerminalId;
                currentModel.PortId = viewModel.PortId;
                currentModel.ServiceId = viewModel.ServiceId;
                currentModel.TugBoatId = viewModel.TugBoatId;
                currentModel.TugMasterId = viewModel.TugMasterId;
                currentModel.VesselId = viewModel.VesselId;
                currentModel.Remarks = viewModel.Remarks;
                currentModel.CustomerOrderSlipId = viewModel.CustomerOrderSlipId;
                currentModel.EditedBy = username;
                currentModel.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                if (currentModel.VesselScheduleServiceEntryId.HasValue)
                {
                    var duration = currentModel.DateArrived!.Value.ToDateTime(currentModel.TimeArrived!.Value)
                        - currentModel.DateLeft!.Value.ToDateTime(currentModel.TimeLeft!.Value);
                    currentModel.RawDurationMinutes = (int)duration.TotalMinutes;
                    currentModel.RawDurationHours = (decimal)duration.TotalHours;
                    currentModel.COSNumber = (await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == currentModel.CustomerOrderSlipId, cancellationToken))!.CustomerOrderSlipNumber;
                    currentModel.Date = viewModel.Date;
                }

                // Smart Reset Logic: Only reset tariff if critical fields changed
                var criticalFieldsChanged = changes.Any(c =>
                    c.StartsWith("ServiceId:") ||
                    c.StartsWith("TugBoatId:") ||
                    c.StartsWith("DateLeft:") ||
                    c.StartsWith("TimeLeft:") ||
                    c.StartsWith("DateArrived:") ||
                    c.StartsWith("TimeArrived:") ||
                    c.StartsWith("TotalHours:"));

                if (criticalFieldsChanged && !currentModel.VesselScheduleServiceEntryId.HasValue)
                {
                    currentModel.Status = MsapConstants.DispatchTicketStatus.ForTariff;
                    currentModel.DispatchRate = 0;
                    currentModel.DispatchBillingAmount = 0;
                    currentModel.DispatchDiscount = 0;
                    currentModel.DispatchNetRevenue = 0;
                    currentModel.BAFRate = 0;
                    currentModel.BAFBillingAmount = 0;
                    currentModel.BAFDiscount = 0;
                    currentModel.BAFNetRevenue = 0;
                    currentModel.TotalBilling = 0;
                    currentModel.TotalNetRevenue = 0;
                    currentModel.ApOtherTugs = 0;
                    currentModel.ApOtherTugsIncludesVat = true;
                    currentModel.TariffBy = string.Empty;
                    currentModel.TariffDate = default;
                    currentModel.TariffEditedBy = string.Empty;
                    currentModel.TariffEditedDate = null;
                    changes.Add("Tariff data reset due to critical field change");
                }

                if (criticalFieldsChanged && currentModel.VesselScheduleServiceEntryId.HasValue)
                {
                    var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == currentModel.CustomerOrderSlipId, cancellationToken);
                    if (schedule == null)
                    {
                        return ServiceResult.Failure("The approved schedule is unavailable.");
                    }
                    schedule = await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(schedule.VesselScheduleId, cancellationToken);
                    if (schedule == null)
                    {
                        return ServiceResult.Failure("The linked schedule is unavailable.");
                    }
                    var chargeBlocker = await ScheduledTicketCharges.ApplyAsync(unitOfWork, currentModel, schedule, username, cancellationToken);
                    changes.Add(chargeBlocker == null
                        ? "Charges recalculated from approved schedule terms and service-date rates"
                        : $"Calculation pending: {chargeBlocker}");
                }

                await RefreshSbmaAsync(currentModel, cancellationToken);
                // File management
                if (imageFile != null)
                {
                    var ext = Path.GetExtension(imageFile.FileName);
                    var name = Path.GetFileNameWithoutExtension(imageFile.FileName);
                    currentModel.ImageName = $"{name}-img-{Guid.NewGuid():N}{ext}";
                    currentModel.ImageSavedUrl = await cloudStorageService.UploadFileAsync(imageFile, currentModel.ImageName);
                    changes.Add("Image updated");
                }

                if (videoFile != null)
                {
                    var ext = Path.GetExtension(videoFile.FileName);
                    var name = Path.GetFileNameWithoutExtension(videoFile.FileName);
                    currentModel.VideoName = $"{name}-vid-{Guid.NewGuid():N}{ext}";
                    currentModel.VideoSavedUrl = await cloudStorageService.UploadFileAsync(videoFile, currentModel.VideoName);
                    changes.Add("Video updated");
                }

                var auditMessage = changes.Any()
                    ? $"Edit dispatch ticket #{currentModel.DispatchNumber}, {string.Join(", ", changes)}"
                    : $"No changes detected for #{currentModel.DispatchNumber}";

                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, auditMessage, "Dispatch Ticket", currentModel.DispatchTicketId, currentModel.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success("Entry edited successfully!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to edit ticket");
                return ServiceResult.Failure($"Failed to edit ticket: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> SaveTariffAsync(DispatchTicket model, string chargeType, string chargeType2, string username, bool isEdit, CancellationToken cancellationToken)
        {
            try
            {
                if (!await IsTicketCustomerOrderSlipEditableAsync(model.DispatchTicketId, cancellationToken))
                {
                    return ServiceResult.Failure("Cannot set/edit tariff — parent COS is cancelled or closed.");
                }

                var currentModel = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == model.DispatchTicketId, cancellationToken);
                if (currentModel == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                if (currentModel.VesselScheduleServiceEntryId.HasValue)
                {
                    return ServiceResult.Failure("Charges for scheduled tickets come from the approved COS and rate tables; separate tariff entry or approval is not allowed.");
                }

                var guard = await GuardClosedPeriodAsync(currentModel.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (isEdit
                    ? currentModel.Status is not (MsapConstants.DispatchTicketStatus.ForApproval or MsapConstants.DispatchTicketStatus.Disapproved)
                    : currentModel.Status != MsapConstants.DispatchTicketStatus.ForTariff)
                {
                    return ServiceResult.Failure(isEdit
                        ? "Only tickets awaiting approval or disapproved can have their tariff edited."
                        : "Only tickets in 'For Tariff' status can have a tariff set.");
                }

                string? progressBlocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, currentModel.CustomerOrderSlipId, JobProgressCalculator.TariffStage, cancellationToken);
                if (progressBlocker != null)
                {
                    return ServiceResult.Failure(progressBlocker);
                }

                string auditMessage;
                string documentType = "Tariff";
                if (isEdit)
                {
                    var changes = new List<string>();
                    void AddChange(string name, object? oldVal, object? newVal)
                    {
                        if (!Equals(oldVal, newVal))
                        {
                            changes.Add($"{name}: {oldVal} -> {newVal}");
                        }
                    }

                    AddChange("CustomerId", currentModel.CustomerId, model.CustomerId);
                    AddChange("DispatchChargeType", currentModel.DispatchChargeType, chargeType);
                    AddChange("BAFChargeType", currentModel.BAFChargeType, chargeType2);
                    AddChange("DispatchRate", currentModel.DispatchRate, model.DispatchRate);
                    AddChange("BAFRate", currentModel.BAFRate, model.BAFRate);
                    AddChange("DispatchDiscount", currentModel.DispatchDiscount, model.DispatchDiscount);
                    AddChange("BAFDiscount", currentModel.BAFDiscount, model.BAFDiscount);
                    AddChange("ApOtherTugs", currentModel.ApOtherTugs, model.ApOtherTugs);
                    AddChange("ApOtherTugsIncludesVat", currentModel.ApOtherTugsIncludesVat, model.ApOtherTugsIncludesVat);

                    currentModel.TariffEditedBy = username;
                    currentModel.TariffEditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    auditMessage = changes.Any() ? $"Edit tariff #{currentModel.DispatchNumber} {string.Join(", ", changes)}" : $"No changes detected for tariff details #{currentModel.DispatchNumber}";
                }
                else
                {
                    currentModel.TariffBy = username;
                    currentModel.TariffDate = DateTimeHelper.GetCurrentPhilippineTime();
                    auditMessage = $"Set Tariff #{currentModel.DispatchNumber}";
                }

                // Server-side re-calculation to ensure integrity
                decimal dispatchRate = model.DispatchRate;
                decimal dispatchDiscountPercent = model.DispatchDiscount;
                decimal dispatchDiscountAmount = dispatchRate * (dispatchDiscountPercent / 100);
                decimal bafRate = model.BAFRate;
                decimal bafDiscountPercent = model.BAFDiscount;
                decimal bafDiscountAmount = bafRate * (bafDiscountPercent / 100);

                decimal dispatchBilling;
                decimal dispatchRevenue;
                decimal bafBilling;
                decimal bafRevenue;

                var hours = Math.Round(currentModel.TotalHours, 2);

                if (chargeType == "Per hour")
                {
                    dispatchBilling = dispatchRate * hours;
                    dispatchRevenue = (dispatchRate - dispatchDiscountAmount) * hours;
                }
                else
                {
                    dispatchBilling = dispatchRate;
                    dispatchRevenue = dispatchRate - dispatchDiscountAmount;
                }

                if (chargeType2 == "Per hour")
                {
                    bafBilling = bafRate * hours;
                    bafRevenue = (bafRate - bafDiscountAmount) * hours;
                }
                else
                {
                    bafBilling = bafRate;
                    bafRevenue = bafRate - bafDiscountAmount;
                }

                currentModel.Status = MsapConstants.DispatchTicketStatus.ForApproval;
                currentModel.CustomerId = model.CustomerId;
                currentModel.DispatchChargeType = chargeType;
                currentModel.BAFChargeType = chargeType2;
                currentModel.DispatchRate = dispatchRate;
                currentModel.BAFRate = bafRate;
                currentModel.DispatchDiscount = dispatchDiscountPercent;
                currentModel.BAFDiscount = bafDiscountPercent;
                currentModel.DispatchBillingAmount = Math.Round(dispatchBilling, 2);
                currentModel.BAFBillingAmount = Math.Round(bafBilling, 2);
                currentModel.DispatchNetRevenue = Math.Round(dispatchRevenue, 2);
                currentModel.BAFNetRevenue = Math.Round(bafRevenue, 2);
                currentModel.ApOtherTugs = model.ApOtherTugs;
                currentModel.ApOtherTugsIncludesVat = model.ApOtherTugsIncludesVat;
                currentModel.TotalBilling = currentModel.DispatchBillingAmount + currentModel.BAFBillingAmount;
                currentModel.TotalNetRevenue = currentModel.DispatchNetRevenue + currentModel.BAFNetRevenue;

                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, auditMessage, documentType, currentModel.DispatchTicketId, currentModel.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success(isEdit ? "Tariff updated successfully." : "Tariff set successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save tariff for ticket {DispatchTicketId}", model.DispatchTicketId);
                return ServiceResult.Failure($"Failed to save tariff: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> ApproveTariffAsync(int id, string username, CancellationToken cancellationToken)
        {
            try
            {
                if (!await IsTicketCustomerOrderSlipEditableAsync(id, cancellationToken))
                {
                    return ServiceResult.Failure("Cannot approve tariff — parent COS is cancelled or closed.");
                }

                var model = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == id, cancellationToken);
                if (model == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                if (model.VesselScheduleServiceEntryId.HasValue)
                {
                    return ServiceResult.Failure("Charges for scheduled tickets come from the approved COS and rate tables; separate tariff entry or approval is not allowed.");
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (model.Status != MsapConstants.DispatchTicketStatus.ForApproval)
                {
                    return ServiceResult.Failure("Only tickets in 'For Approval' status can be approved.");
                }

                string? blocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, model.CustomerOrderSlipId, JobProgressCalculator.ApprovalStage, cancellationToken);
                if (blocker != null)
                {
                    return ServiceResult.Failure(blocker);
                }

                model.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                model.EditedBy = username;
                model.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Approved tariff for dispatch ticket #{model.DispatchNumber}", "Dispatch Ticket", model.DispatchTicketId, model.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success("Tariff approved successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to approve tariff");
                return ServiceResult.Failure($"Failed to approve tariff: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> RevokeTariffApprovalAsync(int id, string username, CancellationToken cancellationToken)
        {
            try
            {
                if (!await IsTicketCustomerOrderSlipEditableAsync(id, cancellationToken))
                {
                    return ServiceResult.Failure("Cannot revoke approval because the parent COS is not open.");
                }

                var ticket = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == id, cancellationToken);
                if (ticket == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(ticket.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (ticket.Status != MsapConstants.DispatchTicketStatus.ForBilling || ticket.BillingId.HasValue)
                {
                    return ServiceResult.Failure("Only unbilled tickets in 'For Billing' status can have approval revoked.");
                }

                ticket.Status = MsapConstants.DispatchTicketStatus.ForApproval;
                ticket.EditedBy = username;
                ticket.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                await unitOfWork.AuditTrail.AddAsync(
                    new AuditTrail(username, $"Revoked approval for dispatch ticket #{ticket.DispatchNumber}",
                        "Dispatch Ticket", ticket.DispatchTicketId, ticket.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);
                return ServiceResult.Success("Approval revoked successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to revoke tariff approval for ticket {DispatchTicketId}", id);
                return ServiceResult.Failure($"Failed to revoke tariff approval: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> DisapproveTariffAsync(int id, string reason, string username, CancellationToken cancellationToken)
        {
            try
            {
                if (!await IsTicketCustomerOrderSlipEditableAsync(id, cancellationToken))
                {
                    return ServiceResult.Failure("Cannot disapprove tariff — parent COS is cancelled or closed.");
                }

                if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
                {
                    return ServiceResult.Failure("Please provide a detailed reason (at least 10 characters)");
                }

                var model = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == id, cancellationToken);
                if (model == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                if (model.VesselScheduleServiceEntryId.HasValue)
                {
                    return ServiceResult.Failure("Charges for scheduled tickets come from the approved COS and rate tables; separate tariff entry or approval is not allowed.");
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (model.Status != MsapConstants.DispatchTicketStatus.ForApproval)
                {
                    return ServiceResult.Failure("Only tickets in 'For Approval' status can be disapproved.");
                }

                model.Status = MsapConstants.DispatchTicketStatus.Disapproved;
                model.EditedBy = username;
                model.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                model.Remarks = string.IsNullOrEmpty(model.Remarks)
                    ? $"Disapproved: {reason}"
                    : $"{model.Remarks} | Disapproved: {reason}";

                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Disapproved tariff for dispatch ticket #{model.DispatchNumber}. Reason: {reason}", "Dispatch Ticket", model.DispatchTicketId, model.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success("Tariff disapproved successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to disapprove tariff");
                return ServiceResult.Failure($"Failed to disapprove tariff: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        #region Batch Operations

        public async Task<ServiceResult> BatchApproveTariffAsync(List<int> ids, string username, CancellationToken cancellationToken)
        {
            try
            {
                var tickets = (await unitOfWork.DispatchTicket.GetAllAsync(
                    dt => ids.Contains(dt.DispatchTicketId), cancellationToken)).ToList();

                if (tickets.Any(t => t.VesselScheduleServiceEntryId.HasValue))
                {
                    return ServiceResult.Failure("Scheduled tickets use approved COS terms and cannot use batch tariff actions.");
                }

                foreach (var ticket in tickets)
                {
                    var guard = await GuardClosedPeriodAsync(ticket.Date, cancellationToken);
                    if (guard != null)
                    {
                        return guard;
                    }
                }

                var toApprove = tickets.Where(t => t.Status == MsapConstants.DispatchTicketStatus.ForApproval).ToList();
                if (toApprove.Count == 0)
                {
                    return ServiceResult.Failure("No tickets in 'For Approval' status found among the selected.");
                }

                foreach (var ticket in toApprove)
                {
                    if (!await IsTicketCustomerOrderSlipEditableAsync(ticket.DispatchTicketId, cancellationToken))
                    {
                        return ServiceResult.Failure("Cannot approve tariff — parent COS is cancelled or closed.");
                    }
                    string? blocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, ticket.CustomerOrderSlipId, JobProgressCalculator.ApprovalStage, cancellationToken);
                    if (blocker != null)
                    {
                        return ServiceResult.Failure(blocker);
                    }
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    foreach (var ticket in toApprove)
                    {
                        ticket.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                        ticket.EditedBy = username;
                        ticket.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                        await unitOfWork.AuditTrail.AddAsync(
                            new AuditTrail(username, $"Batch approved tariff for dispatch ticket #{ticket.DispatchNumber}", "Dispatch Ticket", ticket.DispatchTicketId, ticket.DispatchNumber),
                            cancellationToken);
                    }

                    foreach (var skipped in tickets.Where(t => t.Status != MsapConstants.DispatchTicketStatus.ForApproval))
                    {
                        await unitOfWork.AuditTrail.AddAsync(
                            new AuditTrail(username, $"Batch approve skipped for #{skipped.DispatchNumber} — current status is '{skipped.Status}'", "Dispatch Ticket", skipped.DispatchTicketId, skipped.DispatchNumber),
                            cancellationToken);
                    }

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                var skippedCount = tickets.Count - toApprove.Count;
                var msg = $"{toApprove.Count} tariff(s) approved successfully.";
                if (skippedCount > 0)
                {
                    msg += $" {skippedCount} ticket(s) skipped (not in 'For Approval' status).";
                }

                return ServiceResult.Success(msg);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to batch approve tariffs");
                return ServiceResult.Failure($"Failed to batch approve tariffs: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> BatchSetTariffAsync(List<int> ids, decimal dispatchRate, decimal bafRate, string chargeType, string chargeType2, string username, CancellationToken cancellationToken)
        {
            try
            {
                var tickets = (await unitOfWork.DispatchTicket.GetAllAsync(
                    dt => ids.Contains(dt.DispatchTicketId), cancellationToken)).ToList();

                if (tickets.Any(t => t.VesselScheduleServiceEntryId.HasValue))
                {
                    return ServiceResult.Failure("Scheduled tickets use approved COS terms and cannot use batch tariff actions.");
                }

                foreach (var ticket in tickets)
                {
                    var guard = await GuardClosedPeriodAsync(ticket.Date, cancellationToken);
                    if (guard != null)
                    {
                        return guard;
                    }
                }

                var toSet = tickets.Where(t => t.Status is MsapConstants.DispatchTicketStatus.ForTariff).ToList();
                if (toSet.Count == 0)
                {
                    return ServiceResult.Failure("No tickets eligible for tariff setting (status must be 'For Tariff').");
                }

                var now = DateTimeHelper.GetCurrentPhilippineTime();

                foreach (var jobId in tickets.Select(t => t.CustomerOrderSlipId).Distinct())
                {
                    string? blocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, jobId, JobProgressCalculator.TariffStage, cancellationToken);
                    if (blocker != null)
                    {
                        return ServiceResult.Failure(blocker);
                    }
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    foreach (var ticket in toSet)
                    {
                        var hours = Math.Round(ticket.TotalHours, 2);

                        decimal dispatchBilling = chargeType == "Per hour" ? dispatchRate * hours : dispatchRate;
                        decimal dispatchRevenue = chargeType == "Per hour"
                            ? dispatchRate * hours
                            : dispatchRate;

                        decimal bafBilling = chargeType2 == "Per hour" ? bafRate * hours : bafRate;
                        decimal bafRevenue = chargeType2 == "Per hour"
                            ? bafRate * hours
                            : bafRate;

                        ticket.Status = MsapConstants.DispatchTicketStatus.ForApproval;
                        ticket.DispatchChargeType = chargeType;
                        ticket.BAFChargeType = chargeType2;
                        ticket.DispatchRate = dispatchRate;
                        ticket.BAFRate = bafRate;
                        ticket.DispatchDiscount = 0;
                        ticket.BAFDiscount = 0;
                        ticket.DispatchBillingAmount = Math.Round(dispatchBilling, 2);
                        ticket.BAFBillingAmount = Math.Round(bafBilling, 2);
                        ticket.DispatchNetRevenue = Math.Round(dispatchRevenue, 2);
                        ticket.BAFNetRevenue = Math.Round(bafRevenue, 2);
                        ticket.TotalBilling = ticket.DispatchBillingAmount + ticket.BAFBillingAmount;
                        ticket.TotalNetRevenue = ticket.DispatchNetRevenue + ticket.BAFNetRevenue;
                        ticket.TariffBy = username;
                        ticket.TariffDate = now;

                        await unitOfWork.AuditTrail.AddAsync(
                            new AuditTrail(username, $"Batch set tariff for #{ticket.DispatchNumber}: Dispatch={dispatchRate:N2}, BAF={bafRate:N2}, Total={ticket.TotalBilling:N2}", "Tariff", ticket.DispatchTicketId, ticket.DispatchNumber),
                            cancellationToken);
                    }

                    foreach (var skipped in tickets.Where(t => t.Status is not MsapConstants.DispatchTicketStatus.ForTariff))
                    {
                        await unitOfWork.AuditTrail.AddAsync(
                            new AuditTrail(username, $"Batch tariff skipped for #{skipped.DispatchNumber} — status is '{skipped.Status}'", "Tariff", skipped.DispatchTicketId, skipped.DispatchNumber),
                            cancellationToken);
                    }

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                var skippedCount = tickets.Count - toSet.Count;
                var msg = $"{toSet.Count} tariff(s) set successfully.";
                if (skippedCount > 0)
                {
                    msg += $" {skippedCount} ticket(s) skipped (not eligible).";
                }

                return ServiceResult.Success(msg);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to batch set tariffs");
                return ServiceResult.Failure($"Failed to batch set tariffs: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        #endregion

        public async Task<ServiceResult> DeleteTicketAsync(int id, string username, CancellationToken cancellationToken)
        {
            try
            {
                if (!await IsTicketCustomerOrderSlipEditableAsync(id, cancellationToken))
                {
                    return ServiceResult.Failure("Cannot delete ticket — parent COS is closed or cancelled.");
                }

                var model = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == id, cancellationToken);
                if (model == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (model.VesselScheduleServiceEntryId.HasValue)
                {
                    return ServiceResult.Failure("Required service tickets cannot be deleted. Revise the booking before actual ticket entry, or cancel the COS.");
                }

                if (model.Status != MsapConstants.DispatchTicketStatus.ForTariff &&
                    model.Status != MsapConstants.DispatchTicketStatus.Disapproved)
                {
                    return ServiceResult.Failure($"Cannot delete ticket — status is '{model.Status}' and must be disapproved or have no tariff applied.");
                }

                model.Status = MsapConstants.DispatchTicketStatus.Deleted;
                await RefreshSbmaAsync(model, cancellationToken);
                model.EditedBy = username;
                model.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Deleted dispatch ticket #{model.DispatchNumber}", "Dispatch Ticket", model.DispatchTicketId, model.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success($"Dispatch Ticket #{model.DispatchNumber} deleted successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete ticket");
                return ServiceResult.Failure($"Failed to delete ticket: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> RestoreTicketAsync(int id, string username, CancellationToken cancellationToken)
        {
            try
            {
                if (!await IsTicketCustomerOrderSlipEditableAsync(id, cancellationToken))
                {
                    return ServiceResult.Failure("Cannot restore ticket — parent COS is closed or cancelled.");
                }

                var model = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == id, cancellationToken);
                if (model == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (model.Status != MsapConstants.DispatchTicketStatus.Deleted)
                {
                    return ServiceResult.Failure("Ticket is not in a deleted state.");
                }

                var restoredInput = new DispatchTicketViewModel();
                restoredInput.FromEntity(model);
                string? readinessError = await ValidateReadyAsync(restoredInput, cancellationToken);
                if (readinessError != null)
                {
                    return ServiceResult.Failure(readinessError);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    if (model.CustomerOrderSlipId.HasValue)
                    {
                        var booking = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == model.CustomerOrderSlipId, cancellationToken);
                        if (booking != null)
                        {
                            booking = await unitOfWork.VesselSchedule.GetForUpdateAsync(booking.VesselScheduleId, cancellationToken);
                        }
                        var parent = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(model.CustomerOrderSlipId.Value, cancellationToken);
                        if (parent?.Status != MsapConstants.CustomerOrderSlipStatus.Open ||
                            booking?.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                        {
                            throw new InvalidOperationException("Cannot restore a ticket under a closed, cancelled or operationally completed booking.");
                        }
                    }
                    readinessError = await ValidateReadyAsync(restoredInput, cancellationToken);
                    if (readinessError != null)
                    {
                        throw new InvalidOperationException(readinessError);
                    }
                    model.Status = MsapConstants.DispatchTicketStatus.ForTariff;
                    model.COSNumber = restoredInput.COSNumber;

                    var readyBooking = await unitOfWork.VesselSchedule.GetAsync(x => x.CustomerOrderSlipId == model.CustomerOrderSlipId, cancellationToken);
                    var readySchedule = readyBooking == null ? null : await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(readyBooking.VesselScheduleId, cancellationToken);
                    if (readySchedule == null)
                    {
                        throw new InvalidOperationException("The approved schedule is unavailable.");
                    }
                    string? chargeError = await ScheduledTicketCharges.ApplyAsync(unitOfWork, model, readySchedule, username, cancellationToken);
                    await RefreshSbmaAsync(model, cancellationToken);

                    model.EditedBy = username;
                    model.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Restored dispatch ticket #{model.DispatchNumber}{(chargeError == null ? "" : $"; Calculation pending: {chargeError}")}", "Dispatch Ticket", model.DispatchTicketId, model.DispatchNumber), cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success($"Dispatch Ticket #{model.DispatchNumber} restored successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to restore ticket");
                return ServiceResult.Failure($"Failed to restore ticket: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> DeleteImageAsync(int id, string username, CancellationToken cancellationToken)
        {
            try
            {
                var model = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == id, cancellationToken);
                if (model == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (!string.IsNullOrEmpty(model.ImageName))
                {
                    await cloudStorageService.DeleteFileAsync(model.ImageName);
                }

                model.ImageName = null;
                model.ImageSavedUrl = null;
                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Deleted image on dispatch ticket #{model.DispatchNumber}", "Dispatch Ticket", model.DispatchTicketId, model.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success("Image Deleted Successfully!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete image");
                return ServiceResult.Failure($"Failed to delete image: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> DeleteVideoAsync(int id, string username, CancellationToken cancellationToken)
        {
            try
            {
                var model = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == id, cancellationToken);
                if (model == null)
                {
                    return ServiceResult.Failure("Ticket not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (!string.IsNullOrEmpty(model.VideoName))
                {
                    await cloudStorageService.DeleteFileAsync(model.VideoName);
                }

                model.VideoName = null;
                model.VideoSavedUrl = null;
                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Deleted video on dispatch ticket #{model.DispatchNumber}", "Dispatch Ticket", model.DispatchTicketId, model.DispatchNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success("Video Deleted Successfully!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete video");
                return ServiceResult.Failure($"Failed to delete video: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<(IEnumerable<DispatchTicket> Data, int RecordsFiltered, int TotalRecords)> GetPagedDispatchTicketsAsync(DataTablesParameters parameters, string filterType, CancellationToken cancellationToken)
        {
            return await unitOfWork.DispatchTicket.GetPagedDispatchTicketsAsync(parameters, filterType, cancellationToken);
        }

        public async Task<ServiceResult<object>> CheckForTariffRateAsync(int customerId, int dispatchTicketId, CancellationToken cancellationToken)
        {
            var dispatchModel = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == dispatchTicketId, cancellationToken);
            if (dispatchModel == null)
            {
                return ServiceResult<object>.Failure("Ticket not found.", ServiceResultStatus.NotFound);
            }

            var tariffRate = dispatchModel.DateLeft.HasValue
                ? await unitOfWork.TariffTable.GetApplicableRateAsync(customerId, dispatchModel.TerminalId, dispatchModel.ServiceId, dispatchModel.DateLeft.Value, cancellationToken)
                : null;

            var bafRate = dispatchModel.DateLeft.HasValue
                ? await unitOfWork.BafTable.GetApplicableRateAsync(customerId, dispatchModel.TerminalId, dispatchModel.ServiceId, dispatchModel.DateLeft.Value, cancellationToken)
                : null;
            return ServiceResult<object>.Success(new
            {
                Dispatch = tariffRate?.Dispatch,
                BAF = bafRate?.Amount,
                DispatchDiscount = tariffRate?.DispatchDiscount ?? 0m,
                BAFDiscount = 0m,
                Exists = tariffRate != null || bafRate != null
            });
        }

        public async Task<bool> IsCustomerOrderSlipEditableAsync(int? customerOrderSlipId, CancellationToken cancellationToken)
        {
            return await unitOfWork.DispatchTicket.IsCustomerOrderSlipEditableAsync(customerOrderSlipId, cancellationToken);
        }

        private async Task<bool> IsTicketCustomerOrderSlipEditableAsync(int dispatchTicketId, CancellationToken cancellationToken)
        {
            var ticket = await unitOfWork.DispatchTicket.GetAsync(dt => dt.DispatchTicketId == dispatchTicketId, cancellationToken);
            return ticket != null && await IsCustomerOrderSlipEditableAsync(ticket.CustomerOrderSlipId, cancellationToken);
        }

        public async Task<List<SelectListItem>> GetCustomerSelectListAsync(CancellationToken cancellationToken)
        {
            return await unitOfWork.GetCustomerListAsyncById(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetTerminalsByPortAsync(int portId, CancellationToken cancellationToken)
        {
            var terminals = await unitOfWork.Terminal.GetAllAsync(t => t.PortId == portId, cancellationToken);
            return terminals.Select(t => new SelectListItem
            {
                Value = t.TerminalId.ToString(),
                Text = t.TerminalName
            }).ToList();
        }

        public async Task<DispatchTicketViewModel> PopulateSelectListsAsync(DispatchTicketViewModel viewModel, CancellationToken cancellationToken)
        {
            if (viewModel.VesselScheduleServiceEntryId.HasValue && viewModel.CustomerOrderSlipId.HasValue)
            {
                var booking = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == viewModel.CustomerOrderSlipId, cancellationToken);
                var details = booking == null ? null : await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(booking.VesselScheduleId, cancellationToken);
                var entry = details?.ServiceEntries.SingleOrDefault(e => e.VesselScheduleServiceEntryId == viewModel.VesselScheduleServiceEntryId);
                if (entry != null)
                {
                    viewModel.TugBoatId = entry.TugboatId;
                    viewModel.ServiceId = entry.ServiceId;
                }
            }
            viewModel = await unitOfWork.DispatchTicket.GetDispatchTicketSelectLists(viewModel, cancellationToken);
            viewModel.Customers = await unitOfWork.GetCustomerListAsyncById(cancellationToken);
            return viewModel;
        }

        private async Task<string?> ValidateReadyAsync(DispatchTicketViewModel model, CancellationToken ct)
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, new ValidationContext(model), results, true))
            {
                return results[0].ErrorMessage;
            }
            string? error = await ValidateExpectedActualAsync(model, ct);
            if (error != null)
            {
                return error;
            }

            var parent = await unitOfWork.CustomerOrderSlip.GetAsync(x => x.CustomerOrderSlipId == model.CustomerOrderSlipId, ct);
            var booking = await unitOfWork.VesselSchedule.GetAsync(x => x.CustomerOrderSlipId == model.CustomerOrderSlipId, ct);
            var schedule = booking == null ? null : await unitOfWork.VesselSchedule.GetScheduleWithDetailsAsync(booking.VesselScheduleId, ct);
            var entry = schedule?.ServiceEntries.SingleOrDefault(x => x.VesselScheduleServiceEntryId == model.VesselScheduleServiceEntryId);
            if (parent == null || schedule == null || entry == null)
            {
                return "Select a COS service before saving its ticket.";
            }
            if (entry.CancelledAt.HasValue || entry.TugboatId != model.TugBoatId || entry.ServiceId != model.ServiceId
                || schedule.Status == MsapConstants.VesselScheduleStatus.Cancelled)
            {
                return "The ticket must match an active COS service requirement.";
            }

            model.CustomerId = parent.CustomerId;
            model.VesselId = parent.VesselId;
            model.PortId = parent.PortId;
            model.TerminalId = parent.TerminalId;
            model.VoyageNumber = parent.VoyageNumber;
            model.COSNumber = parent.CustomerOrderSlipNumber;
            return null;
        }

        private async Task RefreshSbmaAsync(DispatchTicket ticket, CancellationToken ct)
        {
            var schedule = await unitOfWork.VesselSchedule.GetAsync(x => x.CustomerOrderSlipId == ticket.CustomerOrderSlipId, ct);
            List<DispatchTicket> siblings = (await unitOfWork.DispatchTicket.GetAllAsync(x => x.CustomerOrderSlipId == ticket.CustomerOrderSlipId, ct)).ToList();
            Dictionary<int, decimal> amounts = SbmaShareCalculator.Allocate(
                siblings.Where(x => x.Status != MsapConstants.DispatchTicketStatus.Deleted), schedule?.SbmaEnabled == true);
            foreach (DispatchTicket sibling in siblings)
            {
                decimal amount = amounts.GetValueOrDefault(sibling.DispatchTicketId);
                if (amount == sibling.SbmaShareAmount)
                {
                    continue;
                }
                if (sibling.Status == MsapConstants.DispatchTicketStatus.Billed
                    || await GuardClosedPeriodAsync(sibling.Date, ct) != null
                    || sibling.DateLeft.HasValue && await GuardClosedPeriodAsync(sibling.DateLeft.Value, ct) != null
                    || sibling.DateArrived.HasValue && await GuardClosedPeriodAsync(sibling.DateArrived.Value, ct) != null)
                {
                    throw new InvalidOperationException("SBMA rounding would change a billed or closed-period ticket.");
                }
                sibling.SbmaShareAmount = amount;
            }
        }

        private async Task DeleteFilesAsync(IEnumerable<string?> names)
        {
            foreach (string name in names.Where(x => !string.IsNullOrEmpty(x)).Cast<string>())
            {
                try
                {
                    await cloudStorageService.DeleteFileAsync(name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not remove dispatch attachment {FileName}", name);
                }
            }
        }

        private async Task<string?> ValidateExpectedActualAsync(DispatchTicketViewModel model, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(model.DispatchNumber) || model.DispatchNumber.Trim().Length > 20
                || !model.DateLeft.HasValue || !model.DateArrived.HasValue || !model.TimeLeft.HasValue || !model.TimeArrived.HasValue)
            {
                return "Enter the actual ticket number and complete start and end dates/times.";
            }
            var start = model.DateLeft.Value.ToDateTime(model.TimeLeft.Value);
            var end = model.DateArrived.Value.ToDateTime(model.TimeArrived.Value);
            if (end <= start || end > DateTimeHelper.GetCurrentPhilippineTime())
            {
                return "Actual end must be after start and cannot be in the future.";
            }
            if (!model.TugMasterId.HasValue || await unitOfWork.TugMaster.GetAsync(t => t.TugMasterId == model.TugMasterId, ct) == null)
            {
                return "Choose a valid tug master for the actual service.";
            }
            if (model.Date == default || model.Date > DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()))
            {
                return "Enter a valid actual ticket date that is not in the future.";
            }
            return (await GuardClosedPeriodAsync(model.DateArrived.Value, ct))?.Message
                ?? (await GuardClosedPeriodAsync(model.DateLeft.Value, ct))?.Message
                ?? (await GuardClosedPeriodAsync(model.Date, ct))?.Message;
        }

        private async Task<ServiceResult?> GuardClosedPeriodAsync(DateOnly date, CancellationToken ct)
        {
            if ((await unitOfWork.PostedPeriod.GetByYearMonthAsync(date.Year, date.Month, ct))?.IsClosed == true)
            {
                return ServiceResult.Failure($"Cannot modify: {date:MMMM yyyy} is closed.");
            }

            return null;
        }
    }
}
