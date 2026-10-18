using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class CustomerOrderSlipService(IUnitOfWork unitOfWork, ILogger<CustomerOrderSlipService> logger)
    {
        public async Task<ServiceResult> CancelServiceAsync(int customerOrderSlipId, int entryId, string? reason, string username, string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 || string.IsNullOrWhiteSpace(userId))
            {
                return ServiceResult.Failure("Enter a cancellation reason of up to 500 characters.");
            }
            var result = ServiceResult.Failure("The planned service cannot be cancelled in its current state.");
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, ct);
                    if (schedule == null)
                    {
                        return;
                    }
                    schedule = await unitOfWork.VesselSchedule.GetForUpdateAsync(schedule.VesselScheduleId, ct);
                    var job = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(customerOrderSlipId, ct);
                    var entry = schedule?.ServiceEntries.SingleOrDefault(e => e.VesselScheduleServiceEntryId == entryId);
                    if (entry == null || job?.Status != MsapConstants.CustomerOrderSlipStatus.Open
                        || schedule!.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled
                        || await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == customerOrderSlipId, ct) != null
                        || await unitOfWork.DispatchTicket.GetAsync(t => t.VesselScheduleServiceEntryId == entryId, ct) != null)
                    {
                        return;
                    }
                    if (entry.CancelledAt.HasValue)
                    {
                        result = ServiceResult.Success("This service is already cancelled.");
                        return;
                    }
                    entry.CancelledAt = DateTimeHelper.GetCurrentPhilippineTime();
                    entry.CancelledByUserId = userId;
                    entry.CancellationReason = reason.Trim();
                    if (schedule.ServiceEntries.All(e => e.CancelledAt.HasValue))
                    {
                        schedule.Status = MsapConstants.VesselScheduleStatus.Cancelled;
                        job.Status = MsapConstants.CustomerOrderSlipStatus.Cancelled;
                    }
                    schedule.EditedBy = username;
                    schedule.EditedDate = entry.CancelledAt;
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                        $"Cancelled unperformed service #{entryId} on COS #{job.CustomerOrderSlipNumber}. Reason: {entry.CancellationReason}",
                        "Customer Order Slip", job.CustomerOrderSlipId, job.CustomerOrderSlipNumber) { UserId = userId }, ct);
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult.Success("Unperformed service cancelled. Its planned row and reason were retained.");
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to cancel planned service {EntryId}", entryId);
                return ServiceResult.Failure("Unable to cancel the service. Refresh the COS and try again.");
            }
            return result;
        }

        public async Task<IEnumerable<CustomerOrderSlip>> GetAllCustomerOrderSlipsAsync(CancellationToken cancellationToken)
        {
            var customerOrderSlips = await unitOfWork.CustomerOrderSlip.GetAllCustomerOrderSlipsWithDetailsAsync(cancellationToken);
            return customerOrderSlips;
        }

        public async Task<CustomerOrderSlip?> GetCustomerOrderSlipByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await unitOfWork.CustomerOrderSlip.GetCustomerOrderSlipWithDetailsAsync(id, cancellationToken);
        }

        public async Task<CustomerOrderSlipViewModel> PopulateCustomerOrderSlipViewModelAsync(CustomerOrderSlipViewModel? viewModel, CancellationToken cancellationToken)
        {
            viewModel ??= new CustomerOrderSlipViewModel { Date = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()) };

            viewModel.Customers = await unitOfWork.GetCustomerListAsyncById(cancellationToken);

            var vessels = await unitOfWork.Vessel.GetAllAsync(cancellationToken: cancellationToken);
            viewModel.Vessels = vessels
                .OrderBy(v => v.VesselName)
                .Select(v => new SelectListItem
                {
                    Value = v.VesselId.ToString(),
                    Text = $"{v.VesselName} ({v.VesselType})"
                })
                .ToList();

            var ports = await unitOfWork.Port.GetAllAsync(cancellationToken: cancellationToken);
            viewModel.Ports = ports
                .OrderBy(p => p.PortName)
                .Select(p => new SelectListItem
                {
                    Value = p.PortId.ToString(),
                    Text = p.PortName
                })
                .ToList();

            viewModel.Terminals = viewModel.PortId != 0
                ? (await unitOfWork.Terminal.GetAllAsync(t => t.PortId == viewModel.PortId, cancellationToken: cancellationToken))
                    .OrderBy(t => t.TerminalName)
                    .Select(t => new SelectListItem
                    {
                        Value = t.TerminalId.ToString(),
                        Text = t.TerminalName
                    })
                    .ToList()
                : new List<SelectListItem>();

            return viewModel;
        }

        public virtual async Task<ServiceResult<int>> CreateCustomerOrderSlipAsync(CustomerOrderSlip customerOrderSlip, string username, CancellationToken cancellationToken)
        {
            try
            {
                var guard = await GuardClosedPeriodAsync(customerOrderSlip.Date, cancellationToken);
                if (guard != null)
                {
                    return ServiceResult<int>.Failure(guard.Message!);
                }

                var timeError = ValidateTimeRange(customerOrderSlip.PlannedStartTime, customerOrderSlip.PlannedEndTime);
                if (timeError != null)
                {
                    return ServiceResult<int>.Failure(timeError);
                }

                customerOrderSlip.Status = MsapConstants.CustomerOrderSlipStatus.Open;
                customerOrderSlip.CreatedBy = username;
                customerOrderSlip.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    customerOrderSlip.CustomerOrderSlipNumber = await unitOfWork.CustomerOrderSlip.GenerateCustomerOrderSlipNumber(cancellationToken);
                    await unitOfWork.CustomerOrderSlip.AddAsync(customerOrderSlip, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                    await RecordAuditAsync($"Created COS #{customerOrderSlip.CustomerOrderSlipNumber}", username, cancellationToken, customerOrderSlip.CustomerOrderSlipId, customerOrderSlip.CustomerOrderSlipNumber);
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult<int>.Success(customerOrderSlip.CustomerOrderSlipId, $"COS #{customerOrderSlip.CustomerOrderSlipNumber} created successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating COS");
                return ServiceResult<int>.Failure($"Failed to create COS: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<string?> GetEditErrorAsync(CustomerOrderSlip job, CancellationToken ct)
        {
            if (job.Status != MsapConstants.CustomerOrderSlipStatus.Open)
            {
                return "Only an Open COS can be edited.";
            }
            if (await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == job.CustomerOrderSlipId, ct) != null)
            {
                return "Edit the vessel schedule instead. Scheduled COS records cannot be revised directly.";
            }
            if (await unitOfWork.DispatchTicket.GetAsync(t => t.CustomerOrderSlipId == job.CustomerOrderSlipId, ct) != null)
            {
                return "This COS already has Dispatch Tickets and cannot be edited.";
            }
            if (await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == job.CustomerOrderSlipId, ct) != null)
            {
                return "This COS already has billing and cannot be edited.";
            }
            var period = await GuardClosedPeriodAsync(job.Date, ct);
            return period?.Message;
        }

        public virtual async Task<ServiceResult> UpdateCustomerOrderSlipAsync(CustomerOrderSlip model, string username, CancellationToken cancellationToken)
        {
            try
            {
                var result = ServiceResult.Failure("Failed to update COS.");
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(model.CustomerOrderSlipId, cancellationToken);
                    if (customerOrderSlip == null)
                    {
                        result = ServiceResult.Failure("COS not found.", ServiceResultStatus.NotFound);
                        return;
                    }
                    string? error = await GetEditErrorAsync(customerOrderSlip, cancellationToken);
                    if (error != null)
                    {
                        result = ServiceResult.Failure(error);
                        return;
                    }
                    var timeError = ValidateTimeRange(model.PlannedStartTime, model.PlannedEndTime);
                    if (timeError != null)
                    {
                        result = ServiceResult.Failure(timeError);
                        return;
                    }

                    var old = (customerOrderSlip.CustomerId, customerOrderSlip.VesselId, customerOrderSlip.PortId, customerOrderSlip.TerminalId);

                    customerOrderSlip.Date = model.Date;
                    customerOrderSlip.CustomerId = model.CustomerId;
                    customerOrderSlip.VesselId = model.VesselId;
                    customerOrderSlip.PortId = model.PortId;
                    customerOrderSlip.TerminalId = model.TerminalId;
                    customerOrderSlip.COSNumber = model.COSNumber;
                    customerOrderSlip.VoyageNumber = model.VoyageNumber;
                    customerOrderSlip.PlannedStartTime = model.PlannedStartTime;
                    customerOrderSlip.PlannedEndTime = model.PlannedEndTime;
                    customerOrderSlip.PreferredTugboatId = model.PreferredTugboatId;
                    customerOrderSlip.RequiredTugCount = model.RequiredTugCount;
                    customerOrderSlip.Remarks = model.Remarks;
                    customerOrderSlip.EditedBy = username;
                    customerOrderSlip.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    // Cascade updates to related records
                    await SyncRelatedRecordsAsync(customerOrderSlip, old, cancellationToken);

                    await RecordAuditAsync($"Edited COS #{customerOrderSlip.CustomerOrderSlipNumber}", username, cancellationToken, customerOrderSlip.CustomerOrderSlipId, customerOrderSlip.CustomerOrderSlipNumber);
                    await unitOfWork.SaveAsync(cancellationToken);

                    result = ServiceResult.Success($"COS #{customerOrderSlip.CustomerOrderSlipNumber} updated successfully.");
                }, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating COS {CustomerOrderSlipId}", model.CustomerOrderSlipId);
                return ServiceResult.Failure($"Failed to update COS: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public Task<ServiceResult> CancelCustomerOrderSlipAsync(int id, string username, CancellationToken ct = default)
        {
            return ChangeBookingStatusAsync(id, MsapConstants.VesselScheduleStatus.Cancelled, username, ct);
        }

        public Task<ServiceResult> CompleteBookingAsync(int id, string username, CancellationToken ct = default)
        {
            return ChangeBookingStatusAsync(id, MsapConstants.VesselScheduleStatus.Completed, username, ct);
        }

        public static bool CanCompleteService(IEnumerable<DispatchTicket> tickets)
        {
            var active = tickets.Where(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted).ToList();
            return active.Count > 0 && active.All(t => t.DateLeft != null && t.TimeLeft != null && t.DateArrived != null && t.TimeArrived != null
                && t.DateArrived.Value.ToDateTime(t.TimeArrived.Value) > t.DateLeft.Value.ToDateTime(t.TimeLeft.Value)
                && t.DateArrived.Value.ToDateTime(t.TimeArrived.Value) <= DateTimeHelper.GetCurrentPhilippineTime());
        }

        private async Task<ServiceResult> ChangeBookingStatusAsync(int id, string status, string username, CancellationToken ct)
        {
            var result = ServiceResult.Failure("Failed to update the booking. Please try again.");
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    // Booking actions always lock the schedule before its COS.
                    var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == id, ct);
                    if (schedule != null)
                    {
                        schedule = await unitOfWork.VesselSchedule.GetForUpdateAsync(schedule.VesselScheduleId, ct);
                    }
                    var job = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(id, ct);
                    if (job == null)
                    {
                        result = ServiceResult.Failure("COS not found.", ServiceResultStatus.NotFound);
                        return;
                    }
                    if (job.Status == MsapConstants.CustomerOrderSlipStatus.Cancelled ||
                        schedule?.Status is MsapConstants.VesselScheduleStatus.Cancelled or MsapConstants.VesselScheduleStatus.Completed)
                    {
                        result = ServiceResult.Failure("This booking is already completed or cancelled.", ServiceResultStatus.ValidationError);
                        return;
                    }
                    var tickets = (await unitOfWork.DispatchTicket.GetAllAsync(t => t.CustomerOrderSlipId == id, ct)).ToList();
                    if (status == MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        var guard = await GuardClosedPeriodAsync(job.Date, ct);
                        if (guard != null)
                        {
                            result = guard;
                            return;
                        }
                        if (job.Status != MsapConstants.CustomerOrderSlipStatus.Open || tickets.Any(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted || t.BillingId != null)
                            || await unitOfWork.Billing.GetAsync(b => b.CustomerOrderSlipId == id, ct) != null)
                        {
                            result = ServiceResult.Failure("Cannot cancel this COS. Resolve its active Dispatch Tickets and billing first. Posted billing must follow the reversal workflow.", ServiceResultStatus.ValidationError);
                            return;
                        }
                        job.Status = MsapConstants.CustomerOrderSlipStatus.Cancelled;
                        job.EditedBy = username;
                        job.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                        await RecordAuditAsync($"Cancelled COS #{job.CustomerOrderSlipNumber}; linked booking cancelled and records retained.", username, ct, id, job.CustomerOrderSlipNumber);
                    }
                    else
                    {
                        if (schedule == null || !CanCompleteService(tickets)
                            || (await JobProgressCalculator.LoadAsync(unitOfWork, job, schedule, ct)).Stage < JobProgressCalculator.TariffStage)
                        {
                            result = ServiceResult.Failure("Record valid actual start and end times for all active Dispatch Tickets before completing the vessel booking.", ServiceResultStatus.ValidationError);
                            return;
                        }
                    }
                    if (schedule != null)
                    {
                        schedule.Status = status;
                        schedule.EditedBy = username;
                        schedule.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Vessel schedule #{schedule.VesselScheduleId} marked {status} through COS #{job.CustomerOrderSlipNumber}.", "Vessel Schedule", schedule.VesselScheduleId), ct);
                    }
                    await unitOfWork.SaveAsync(ct);
                    result = ServiceResult.Success(status == MsapConstants.VesselScheduleStatus.Cancelled
                        ? "COS and linked vessel booking cancelled. Records were retained."
                        : "Service completed. No more Dispatch Tickets can be added. Tariff, approval, billing, posting and collection can continue through the COS.");
                }, ct);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to change booking status for COS {Id}", id);
                return ServiceResult.Failure("Failed to update the booking. Please try again.");
            }
        }

        private async Task SyncRelatedRecordsAsync(CustomerOrderSlip customerOrderSlip, (int CustomerId, int VesselId, int PortId, int TerminalId) old, CancellationToken cancellationToken)
        {
            var criticalChanged = customerOrderSlip.CustomerId != old.CustomerId ||
                                  customerOrderSlip.VesselId != old.VesselId ||
                                  customerOrderSlip.PortId != old.PortId ||
                                  customerOrderSlip.TerminalId != old.TerminalId;

            // 1. Update unbilled dispatch tickets
            var tickets = await unitOfWork.DispatchTicket.GetAllAsync(
                dt => dt.CustomerOrderSlipId == customerOrderSlip.CustomerOrderSlipId &&
                      dt.Status != MsapConstants.DispatchTicketStatus.Billed &&
                      dt.Status != MsapConstants.DispatchTicketStatus.Deleted,
                cancellationToken);

            foreach (var ticket in tickets)
            {
                ticket.CustomerId = customerOrderSlip.CustomerId;
                ticket.VesselId = customerOrderSlip.VesselId;
                ticket.VoyageNumber = customerOrderSlip.VoyageNumber;
                ticket.COSNumber = customerOrderSlip.COSNumber;
                ticket.PortId = customerOrderSlip.PortId;
                ticket.TerminalId = customerOrderSlip.TerminalId;
                ticket.Date = customerOrderSlip.Date;

                if (criticalChanged && ticket.Status is not MsapConstants.DispatchTicketStatus.ForTariff)
                {
                    ticket.Status = MsapConstants.DispatchTicketStatus.ForTariff;
                    ticket.DispatchRate = 0;
                    ticket.DispatchBillingAmount = 0;
                    ticket.DispatchDiscount = 0;
                    ticket.DispatchNetRevenue = 0;
                    ticket.BAFRate = 0;
                    ticket.BAFBillingAmount = 0;
                    ticket.BAFDiscount = 0;
                    ticket.BAFNetRevenue = 0;
                    ticket.TotalBilling = 0;
                    ticket.TotalNetRevenue = 0;
                    ticket.ApOtherTugs = 0;
                    ticket.ApOtherTugsIncludesVat = true;
                    ticket.TariffBy = string.Empty;
                    ticket.TariffDate = default;
                    ticket.TariffEditedBy = string.Empty;
                    ticket.TariffEditedDate = null;
                }
            }

            // 2. Update unposted/uncollected billings
            var billings = await unitOfWork.Billing.GetAllAsync(
                b => b.CustomerOrderSlipId == customerOrderSlip.CustomerOrderSlipId &&
                     (b.Status == MsapConstants.BillingStatus.ForPosting || b.Status == MsapConstants.BillingStatus.ForCollection),
                cancellationToken);

            foreach (var billing in billings)
            {
                billing.CustomerId = customerOrderSlip.CustomerId;
                billing.VesselId = customerOrderSlip.VesselId;
                billing.VoyageNumber = customerOrderSlip.VoyageNumber;
                billing.COSNumber = customerOrderSlip.COSNumber;
                billing.PortId = customerOrderSlip.PortId;
                billing.TerminalId = customerOrderSlip.TerminalId;
                billing.Date = customerOrderSlip.Date;
            }
        }

        public virtual async Task TryAutoCloseAsync(int customerOrderSlipId, string username, CancellationToken cancellationToken)
        {
            try
            {
                var closedStatuses = new[] { MsapConstants.DispatchTicketStatus.Billed, MsapConstants.DispatchTicketStatus.Deleted };
                var anyUnbilled = await unitOfWork.DispatchTicket.GetAsync(
                    dt => dt.CustomerOrderSlipId == customerOrderSlipId && !closedStatuses.Contains(dt.Status),
                    cancellationToken) != null;

                if (anyUnbilled)
                {
                    return;
                }

                var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetAsync(jo => jo.CustomerOrderSlipId == customerOrderSlipId, cancellationToken);
                if (customerOrderSlip == null || customerOrderSlip.Status != MsapConstants.CustomerOrderSlipStatus.Open)
                {
                    return;
                }

                customerOrderSlip.Status = MsapConstants.CustomerOrderSlipStatus.Closed;
                await RecordAuditAsync($"Auto-closed COS #{customerOrderSlip.CustomerOrderSlipNumber}", username, cancellationToken, customerOrderSlip.CustomerOrderSlipId, customerOrderSlip.CustomerOrderSlipNumber);
                await unitOfWork.SaveAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-close failed for COS {CustomerOrderSlipId}", customerOrderSlipId);
                throw;
            }
        }

        public async Task<(IEnumerable<CustomerOrderSlip> Data, int RecordsFiltered, int TotalRecords)> GetPagedCustomerOrderSlipsAsync(DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            return await unitOfWork.CustomerOrderSlip.GetPagedCustomerOrderSlipsAsync(parameters, cancellationToken);
        }

        private async Task RecordAuditAsync(string activity, string username, CancellationToken cancellationToken, int? recordId = null, string? referenceNumber = null)
        {
            var audit = new AuditTrail(username, activity, "COS", recordId, referenceNumber);
            await unitOfWork.AuditTrail.AddAsync(audit, cancellationToken);
        }

        public async Task<ServiceResult> AssignTugboatAsync(int customerOrderSlipId, int tugboatId, string username, CancellationToken cancellationToken)
        {
            try
            {
                var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetCustomerOrderSlipWithDetailsAsync(customerOrderSlipId, cancellationToken);
                if (customerOrderSlip == null)
                {
                    return ServiceResult.Failure("COS not found.", ServiceResultStatus.NotFound);
                }

                if (await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId && s.ServiceEntries.Count > 0, cancellationToken) != null)
                {
                    return ServiceResult.Failure("Revise tugboats and services through the vessel schedule before actual ticket entry.");
                }
                if (customerOrderSlip.Status != MsapConstants.CustomerOrderSlipStatus.Open ||
                    await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId &&
                        (s.Status == MsapConstants.VesselScheduleStatus.Completed || s.Status == MsapConstants.VesselScheduleStatus.Cancelled), cancellationToken) != null)
                {
                    return ServiceResult.Failure("Cannot change tug assignments for a closed, cancelled or operationally completed booking.");
                }

                var guard = await GuardClosedPeriodAsync(customerOrderSlip.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                var tugboat = await unitOfWork.Tugboat.GetAsync(t => t.TugboatId == tugboatId, cancellationToken);
                if (tugboat == null)
                {
                    return ServiceResult.Failure("Tugboat not found.", ServiceResultStatus.NotFound);
                }

                // Check if already assigned
                bool alreadyAssigned = customerOrderSlip.PreferredTugboatId == tugboatId || customerOrderSlip.DispatchTickets.Any(dt => dt.TugBoatId == tugboatId);
                if (alreadyAssigned)
                {
                    return ServiceResult.Failure("Tugboat is already assigned to this COS.");
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(customerOrderSlipId, cancellationToken);
                    var booking = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, cancellationToken);
                    if (customerOrderSlip?.Status != MsapConstants.CustomerOrderSlipStatus.Open ||
                        booking?.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        throw new InvalidOperationException("Cannot assign tugs to a closed, cancelled or operationally completed booking.");
                    }
                    if (customerOrderSlip.PreferredTugboatId == null)
                    {
                        customerOrderSlip.PreferredTugboatId = tugboatId;
                    }
                    else
                    {
                        // Assign as an additional tugboat by creating a pending DispatchTicket
                        var services = await unitOfWork.Service.GetAllAsync(cancellationToken: cancellationToken);
                        var service = services.FirstOrDefault();
                        if (service == null)
                        {
                            throw new InvalidOperationException("Cannot assign tugboat: no service configured.");
                        }
                        int serviceId = service.ServiceId;

                        var ticket = new DispatchTicket
                        {
                            CustomerOrderSlipId = customerOrderSlip.CustomerOrderSlipId,
                            TugBoatId = tugboatId,
                            CustomerId = customerOrderSlip.CustomerId,
                            VesselId = customerOrderSlip.VesselId,
                            PortId = customerOrderSlip.PortId,
                            TerminalId = customerOrderSlip.TerminalId,
                            ServiceId = serviceId,
                            Date = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()),
                            DispatchNumber = $"P{customerOrderSlip.CustomerOrderSlipId:X}T{tugboatId:X}",
                            Status = MsapConstants.DispatchTicketStatus.ForTariff,
                            CreatedBy = username,
                            CreatedDate = DateTimeHelper.GetCurrentPhilippineTime()
                        };

                        await unitOfWork.DispatchTicket.AddAsync(ticket, cancellationToken);
                    }

                    await RecordAuditAsync($"Assigned tugboat {tugboat.TugboatName} to COS #{customerOrderSlip.CustomerOrderSlipNumber}", username, cancellationToken, customerOrderSlip.CustomerOrderSlipId, customerOrderSlip.CustomerOrderSlipNumber);
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success("Tugboat assigned successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error assigning tugboat");
                return ServiceResult.Failure($"Failed to assign tugboat: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> UnassignTugboatAsync(int customerOrderSlipId, int tugboatId, string username, CancellationToken cancellationToken)
        {
            try
            {
                var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetCustomerOrderSlipWithDetailsAsync(customerOrderSlipId, cancellationToken);
                if (customerOrderSlip == null)
                {
                    return ServiceResult.Failure("COS not found.", ServiceResultStatus.NotFound);
                }

                if (await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId && s.ServiceEntries.Count > 0, cancellationToken) != null)
                {
                    return ServiceResult.Failure("Revise tugboats and services through the vessel schedule before actual ticket entry.");
                }
                if (customerOrderSlip.Status != MsapConstants.CustomerOrderSlipStatus.Open ||
                    await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId &&
                        (s.Status == MsapConstants.VesselScheduleStatus.Completed || s.Status == MsapConstants.VesselScheduleStatus.Cancelled), cancellationToken) != null)
                {
                    return ServiceResult.Failure("Cannot change tug assignments for a closed, cancelled or operationally completed booking.");
                }

                var guard = await GuardClosedPeriodAsync(customerOrderSlip.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                var tug = await unitOfWork.Tugboat.GetAsync(t => t.TugboatId == tugboatId, cancellationToken);
                string? tugboatName = tug?.TugboatName;

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var lockedJob = await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(customerOrderSlipId, cancellationToken);
                    var booking = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, cancellationToken);
                    if (lockedJob?.Status != MsapConstants.CustomerOrderSlipStatus.Open ||
                        booking?.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        throw new InvalidOperationException("Cannot unassign tugs from a closed, cancelled or operationally completed booking.");
                    }
                    if (customerOrderSlip.PreferredTugboatId == tugboatId)
                    {
                        customerOrderSlip.PreferredTugboatId = null;
                    }

                    var ticketToRemove = customerOrderSlip.DispatchTickets.FirstOrDefault(dt => dt.TugBoatId == tugboatId);
                    if (ticketToRemove != null)
                    {
                        if (ticketToRemove.Status != MsapConstants.DispatchTicketStatus.ForTariff)
                        {
                            throw new InvalidOperationException("Cannot unassign a tugboat with an active or processed dispatch ticket.");
                        }

                        await unitOfWork.DispatchTicket.RemoveAsync(ticketToRemove, cancellationToken);
                    }

                    await RecordAuditAsync($"Unassigned tugboat {tugboatName ?? "Unknown"} from COS #{customerOrderSlip.CustomerOrderSlipNumber}", username, cancellationToken, customerOrderSlip.CustomerOrderSlipId, customerOrderSlip.CustomerOrderSlipNumber);
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success("Tugboat unassigned successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error unassigning tugboat");
                return ServiceResult.Failure($"Failed to unassign tugboat: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<List<SelectListItem>> GetCustomerOrderSlipSelectListAsync(CancellationToken cancellationToken)
        {
            var customerOrderSlips = await unitOfWork.CustomerOrderSlip.GetAllCustomerOrderSlipsWithDetailsAsync(cancellationToken);
            return customerOrderSlips
                .Take(100)
                .Select(j => new SelectListItem
                {
                    Value = j.CustomerOrderSlipId.ToString(),
                    Text = $"{j.CustomerOrderSlipNumber} - {j.Vessel.VesselName}"
                })
                .ToList();
        }

        private static string? ValidateTimeRange(DateTime? start, DateTime? end)
        {
            if (start.HasValue && end.HasValue && end <= start)
            {
                return "Planned End Time must be strictly after Planned Start Time.";
            }
            return null;
        }

        private async Task<ServiceResult?> GuardClosedPeriodAsync(DateOnly date, CancellationToken ct)
        {
            if (await unitOfWork.PostedPeriod.IsMonthClosedAsync(date.Year, date.Month, ct))
            {
                return ServiceResult.Failure($"Cannot modify: {date:MMMM yyyy} is closed.");
            }

            return null;
        }
    }
}
