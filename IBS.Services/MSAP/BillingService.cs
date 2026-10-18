using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using IBS.DTOs.MSAP;
using static IBS.Utility.MSAP.Constants.TaxConstants;

namespace IBS.Services.MSAP
{
    public class BillingService(
        IUnitOfWork unitOfWork,
        CustomerOrderSlipService customerOrderSlipService,
        ILogger<BillingService> logger,
        IAccessControlService accessControl)
    {
        public static bool CanPrint(Billing billing)
        {
            return (billing.Status is MsapConstants.BillingStatus.ForCollection or MsapConstants.BillingStatus.Collected)
                && billing.ApprovedRevision == billing.Revision
                && !string.IsNullOrEmpty(billing.ApprovedByUserId) && billing.ApprovedAt.HasValue;
        }

        public async Task<Billing?> GetBillingByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
        }

        public async Task<ServiceResult<int>> CreateBillingAsync(Billing model, string username, string company, CancellationToken cancellationToken)
        {
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    model.MsapBillingId = 0;
                    model.BasicBillingId = null;
                    model.BasicBilling = null;
                    model.BafBilling = null;
                    if (!model.CustomerOrderSlipId.HasValue)
                    {
                        throw new InvalidOperationException("Select a COS before preparing billing.");
                    }
                    var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == model.CustomerOrderSlipId, cancellationToken);
                    if (schedule != null)
                    {
                        await unitOfWork.VesselSchedule.GetForUpdateAsync(schedule.VesselScheduleId, cancellationToken);
                    }
                    await unitOfWork.CustomerOrderSlip.GetForUpdateAsync(model.CustomerOrderSlipId.Value, cancellationToken);
                    var prepared = await PrepareBillingAsync(model, username, company, cancellationToken);
                    if (!prepared.IsSuccess || prepared.Data == null)
                    {
                        throw new InvalidOperationException(prepared.Message ?? "Unable to prepare billing.");
                    }
                    foreach (var bill in prepared.Data)
                    {
                        await unitOfWork.Billing.AddAsync(bill, cancellationToken);
                    }
                    foreach (var ticket in model.PaidDispatchTickets!)
                    {
                        ticket.Billing = model;
                    }
                    await unitOfWork.SaveAsync(cancellationToken);
                    foreach (var bill in prepared.Data)
                    {
                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                            $"Created Billing #{bill.MsapBillingNumber}" + (model.SeparateBaf ? " (separate Basic/BAF preparation)" : ""),
                            "Billing", bill.MsapBillingId, bill.MsapBillingNumber), cancellationToken);
                    }
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);
                return ServiceResult<int>.Success(model.MsapBillingId,
                    model.SeparateBaf ? $"Created Basic #{model.MsapBillingNumber} and BAF #{model.BafBillingNumber}." : "Billing saved for reviewing.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create billing");
                return ServiceResult<int>.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        public async Task<ServiceResult<List<Billing>>> PrepareBillingAsync(Billing model, string username, string company, CancellationToken cancellationToken)
        {
            var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
            if (guard != null)
            {
                return ServiceResult<List<Billing>>.Failure(guard.Message!);
            }
            if (model.Date == default || model.CustomerOrderSlipId is not > 0)
            {
                return ServiceResult<List<Billing>>.Failure("Billing date and COS are required.");
            }
            var job = await unitOfWork.CustomerOrderSlip.GetCustomerOrderSlipWithDetailsAsync(model.CustomerOrderSlipId.Value, cancellationToken);
            if (job == null || job.CustomerId != model.CustomerId)
            {
                return ServiceResult<List<Billing>>.Failure("The selected COS does not belong to this customer.");
            }
            var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == job.CustomerId, cancellationToken);
            if (customer == null || !customer.IsActive || customer.Company != company)
            {
                return ServiceResult<List<Billing>>.Failure("The customer is unavailable for this company.");
            }
            string? blocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, job.CustomerOrderSlipId, JobProgressCalculator.BillingStage, cancellationToken);
            if (blocker != null)
            {
                return ServiceResult<List<Billing>>.Failure(blocker);
            }
            var tickets = job.DispatchTickets.Where(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted).ToList();
            if (tickets.Count == 0 || tickets.Any(t => t.CustomerId != customer.CustomerId || t.BillingId.HasValue || t.Status != MsapConstants.DispatchTicketStatus.ForBilling))
            {
                return ServiceResult<List<Billing>>.Failure("All COS tickets must be ready and unbilled. Partial billing is not allowed.");
            }
            model.Principal = null;
            if (model.BilledTo == "PRINCIPAL")
            {
                model.Principal = await unitOfWork.Principal.GetAsync(p => p.PrincipalId == model.PrincipalId && p.CustomerId == customer.CustomerId && p.IsActive, cancellationToken);
                if (model.Principal == null)
                {
                    return ServiceResult<List<Billing>>.Failure("Select an active principal belonging to the service customer.");
                }
            }
            else
            {
                model.BilledTo = "LOCAL";
                model.PrincipalId = null;
            }
            if (model.IsUndocumented)
            {
                model.MsapBillingNumber = await unitOfWork.Billing.GenerateBillingNumber(model.Date.Year, cancellationToken);
            }
            string basicNumber = model.MsapBillingNumber?.Trim() ?? "";
            string bafNumber = model.BafBillingNumber?.Trim() ?? "";
            if (model.IsUndocumented && model.SeparateBaf)
            {
                if (!int.TryParse(basicNumber.AsSpan(1), out int sequence))
                {
                    return ServiceResult<List<Billing>>.Failure("Unable to generate the separate BAF billing number.");
                }
                bafNumber = $"B{sequence + 1:D6}";
            }
            if (basicNumber.Length is 0 or > 10 || (model.SeparateBaf && (bafNumber.Length is 0 or > 10)))
            {
                return ServiceResult<List<Billing>>.Failure("Each billing number must contain 1 to 10 characters.");
            }
            if (model.SeparateBaf && string.Equals(basicNumber, bafNumber, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<List<Billing>>.Failure("Basic and BAF billing numbers must be different.");
            }
            var duplicate = await unitOfWork.Billing.GetAsync(b => b.Company == company && b.Year == model.Date.Year
                && (b.MsapBillingNumber == basicNumber || (model.SeparateBaf && b.MsapBillingNumber == bafNumber)), cancellationToken);
            if (duplicate != null)
            {
                return ServiceResult<List<Billing>>.Failure($"Billing number '{duplicate.MsapBillingNumber}' is already used for this year and company.");
            }
            model.MsapBillingNumber = basicNumber;
            model.BafBillingNumber = bafNumber;
            model.Customer = customer;
            model.VesselId = job.VesselId;
            model.Vessel = job.Vessel;
            model.PortId = job.PortId;
            model.Port = job.Port;
            model.TerminalId = job.TerminalId;
            model.Terminal = job.Terminal;
            model.VoyageNumber = job.VoyageNumber;
            model.COSNumber = job.COSNumber;
            ApplyCustomerTaxSettings(model);
            model.Terms = (model.Principal?.Terms ?? customer.CustomerTerms) ?? "COD";
            model.DueDate = await unitOfWork.Terms.ComputeDueDateAsync(model.Terms, model.Date, cancellationToken);
            model.Year = model.Date.Year;
            model.Company = company;
            model.Status = MsapConstants.BillingStatus.ForPosting;
            model.CreatedBy = username;
            model.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();
            model.PaidDispatchTickets = tickets;
            model.UniqueTugboats = tickets.Select(t => t.Tugboat.TugboatName).Distinct().ToList();
            model.DispatchAmount = tickets.Sum(t => t.DispatchNetRevenue);
            decimal baf = tickets.Sum(t => t.BAFNetRevenue);
            model.BAFAmount = model.SeparateBaf ? 0 : baf;
            model.Amount = model.Balance = model.DispatchAmount + model.BAFAmount;
            model.IsPrincipal = model.PrincipalId.HasValue;
            var statements = new List<Billing> { model };
            if (model.SeparateBaf)
            {
                model.BafBilling = new Billing
                {
                    BasicBilling = model,
                    MsapBillingNumber = bafNumber,
                    Date = model.Date,
                    DueDate = model.DueDate,
                    Year = model.Year,
                    CustomerId = model.CustomerId,
                    Customer = customer,
                    PrincipalId = model.PrincipalId,
                    Principal = model.Principal,
                    IsPrincipal = model.IsPrincipal,
                    BilledTo = model.BilledTo,
                    VesselId = model.VesselId,
                    Vessel = model.Vessel,
                    PortId = model.PortId,
                    Port = model.Port,
                    TerminalId = model.TerminalId,
                    Terminal = model.Terminal,
                    VoyageNumber = model.VoyageNumber,
                    COSNumber = model.COSNumber,
                    CustomerOrderSlipId = model.CustomerOrderSlipId,
                    IsVatable = model.IsVatable,
                    IsVatInclusive = model.IsVatInclusive,
                    PrintWht = model.PrintWht,
                    IsUndocumented = model.IsUndocumented,
                    Terms = model.Terms,
                    Company = company,
                    Status = model.Status,
                    CreatedBy = username,
                    CreatedDate = model.CreatedDate,
                    BAFAmount = baf,
                    Amount = baf,
                    Balance = baf,
                    PaidDispatchTickets = tickets,
                    UniqueTugboats = model.UniqueTugboats
                };
                statements.Add(model.BafBilling);
            }
            foreach (var statement in statements)
            {
                unitOfWork.Billing.ProcessAddress(statement, cancellationToken);
            }
            return ServiceResult<List<Billing>>.Success(statements);
        }

        public static void ApplyCustomerTaxSettings(Billing model)
        {
            model.IsVatable = model.Customer.VatType == "Vatable";
            // shortcut: VAT-inclusive stays fixed until customer settings provide this preference.
            model.IsVatInclusive = true;
            model.PrintWht = model.Customer.WithHoldingTax;
        }

        private async Task ApplyBafRateAsync(DispatchTicket dt, decimal newRate, string? chargeType, string username, CancellationToken cancellationToken)
        {
            var rateChanged = dt.BAFRate != newRate;
            var typeChanged = !string.IsNullOrWhiteSpace(chargeType) && dt.BAFChargeType != chargeType;
            if (!rateChanged && !typeChanged)
            {
                return;
            }
            if (dt.VesselScheduleServiceEntryId.HasValue)
            {
                throw new InvalidOperationException("Scheduled ticket charges must follow approved COS terms and cannot be changed during billing.");
            }

            var oldRate = dt.BAFRate;
            if (typeChanged)
            {
                dt.BAFChargeType = chargeType;
            }

            var hours = Math.Round(dt.TotalHours, 2);
            var discountAmount = newRate * (dt.BAFDiscount / 100);
            var perHour = dt.BAFChargeType == "Per hour";
            var bafBilling = perHour ? newRate * hours : newRate;
            var bafRevenue = perHour ? (newRate - discountAmount) * hours : newRate - discountAmount;

            dt.BAFRate = Math.Round(newRate, 2);
            dt.BAFBillingAmount = Math.Round(bafBilling, 2);
            dt.BAFNetRevenue = Math.Round(bafRevenue, 2);
            dt.TotalBilling = Math.Round(dt.DispatchBillingAmount + dt.BAFBillingAmount, 2);
            dt.TotalNetRevenue = Math.Round(dt.DispatchNetRevenue + dt.BAFNetRevenue, 2);

            var typeDesc = typeChanged ? $" ({dt.BAFChargeType})" : string.Empty;
            await unitOfWork.AuditTrail.AddAsync(
                new AuditTrail(username, $"BAF rate adjusted during billing on #{dt.DispatchNumber}: {oldRate:N2} → {newRate:N2}{typeDesc}", "Dispatch Ticket", dt.DispatchTicketId, dt.DispatchNumber),
                cancellationToken);
        }

        public async Task<ServiceResult> PostBillingAsync(int id, string username, CancellationToken cancellationToken, string approverUserId, int reviewedRevision)
        {
            try
            {
                var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                if (billing == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(billing.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                string? progressBlocker = await JobProgressCalculator.GetBillingBlockerAsync(unitOfWork, billing, JobProgressCalculator.PostingStage, cancellationToken);
                if (progressBlocker != null)
                {
                    return ServiceResult.Failure(progressBlocker);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var related = billing.BasicBilling ?? billing.BafBilling;
                    foreach (int billingId in new[] { billing.MsapBillingId, related?.MsapBillingId ?? billing.MsapBillingId }.Distinct().OrderBy(value => value))
                    {
                        await unitOfWork.Billing.GetForUpdateAsync(billingId, cancellationToken);
                    }
                    var model = billing;
                    if (string.IsNullOrEmpty(approverUserId) || !await accessControl.HasAccessAsync(approverUserId, ProcedureEnum.ApproveFinalBilling))
                    {
                        throw new InvalidOperationException("You do not have permission to approve final bills.");
                    }
                    if (model.Revision != reviewedRevision)
                    {
                        throw new InvalidOperationException("This bill changed. Review the current revision before approving.");
                    }
                    var lockedGuard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                    string? lockedBlocker = await JobProgressCalculator.GetBillingBlockerAsync(unitOfWork, model, JobProgressCalculator.PostingStage, cancellationToken);
                    if (lockedGuard != null || lockedBlocker != null)
                    {
                        throw new InvalidOperationException(lockedGuard?.Message ?? lockedBlocker);
                    }

                    if (model.Status != MsapConstants.BillingStatus.ForPosting)
                    {
                        throw new InvalidOperationException($"Billing #{model.MsapBillingNumber} is already {model.Status}.");
                    }

                    // Actual tickets become billed after both numbered components are approved.
                    if (related == null || related.Status is MsapConstants.BillingStatus.ForCollection or MsapConstants.BillingStatus.Collected)
                    {
                        int sourceId = model.BasicBillingId ?? model.MsapBillingId;
                        var linkedTickets = await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == sourceId, cancellationToken);
                        foreach (var dt in linkedTickets)
                        {
                            dt.Status = MsapConstants.DispatchTicketStatus.Billed;
                        }
                    }

                    var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == model.CustomerId, cancellationToken);
                    if (customer == null)
                    {
                        throw new InvalidOperationException("Customer not found.");
                    }

                    model.Customer = customer;

                    if (model.PrincipalId.HasValue && model.PrincipalId != 0)
                    {
                        model.Principal = await unitOfWork.Principal.GetAsync(p => p.PrincipalId == model.PrincipalId.Value, cancellationToken);
                    }
                    model.Vessel = await unitOfWork.Vessel.GetAsync(v => v.VesselId == model.VesselId, cancellationToken) ?? null!;

                    DateTime approvedAt = DateTimeHelper.GetCurrentPhilippineTime();
                    var salesBook = BillingBookEntries.Create(model, approvedAt, username, approverUserId);
                    await unitOfWork.Billing.AddSalesBookAsync(salesBook, cancellationToken);

                    model.ApprovedRevision = model.Revision;
                    model.ApprovedByUserId = approverUserId;
                    model.ApprovedAt = approvedAt;
                    model.Status = MsapConstants.BillingStatus.ForCollection;

                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Approved final billing #{model.MsapBillingNumber}, revision {model.Revision}", "Billing", model.MsapBillingId, model.MsapBillingNumber)
                    { UserId = approverUserId, ActorName = salesBook.ApprovedByName }, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);

                    if (model.CustomerOrderSlipId.HasValue)
                    {
                        await customerOrderSlipService.TryAutoCloseAsync(model.CustomerOrderSlipId.Value, username, cancellationToken);
                    }
                }, cancellationToken);

                return ServiceResult.Success("Final bill approved successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to approve final billing {BillingId}", id);
                return ServiceResult.Failure($"Failed to approve final billing: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> UpdateBillingAsync(Billing model, string username, CancellationToken cancellationToken)
        {
            ServiceResult result = ServiceResult.Failure("Unable to update billing.");
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    await unitOfWork.Billing.GetForUpdateAsync(model.MsapBillingId, cancellationToken);
                    result = await UpdateBillingCoreAsync(model, username, cancellationToken);
                    if (!result.IsSuccess)
                    {
                        throw new InvalidOperationException(result.Message);
                    }
                }, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update billing {BillingId}", model.MsapBillingId);
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        private async Task<ServiceResult> UpdateBillingCoreAsync(Billing model, string username, CancellationToken cancellationToken)
        {
            try
            {
                var currentModel = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == model.MsapBillingId, cancellationToken);
                if (currentModel == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }

                // shortcut: use delete/reprepare until the editor can revise both split components together.
                if (currentModel.BasicBillingId.HasValue || currentModel.BafBilling != null)
                {
                    return ServiceResult.Failure("Split billings must be corrected together. Delete the unposted preparation and prepare it again.");
                }
                var guard = await GuardClosedPeriodAsync(currentModel.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                if (currentModel.Status != MsapConstants.BillingStatus.ForPosting)
                {
                    return ServiceResult.Failure("Only billings with 'For Posting' status can be edited.");
                }

                var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == model.CustomerId, cancellationToken);
                if (customer == null)
                {
                    return ServiceResult.Failure("Customer not found.");
                }

                string? progressBlocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, currentModel.CustomerOrderSlipId, JobProgressCalculator.BillingStage, cancellationToken);
                if (progressBlocker != null)
                {
                    return ServiceResult.Failure(progressBlocker);
                }
                if (model.ToBillDispatchTickets != null)
                {
                    foreach (var ticketId in model.ToBillDispatchTickets)
                    {
                        var ticket = await unitOfWork.DispatchTicket.GetAsync(t => t.DispatchTicketId == int.Parse(ticketId), cancellationToken);
                        string? blocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, ticket?.CustomerOrderSlipId, JobProgressCalculator.BillingStage, cancellationToken);
                        if (blocker != null)
                        {
                            return ServiceResult.Failure(blocker);
                        }
                    }
                }

                var requestedPeriodGuard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (requestedPeriodGuard != null)
                {
                    return requestedPeriodGuard;
                }

                // Update ticket billing references only when ticket selection was submitted
                if (model.ToBillDispatchTickets != null)
                {
                    var oldTickets = await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == model.MsapBillingId, cancellationToken);
                    foreach (var dt in oldTickets)
                    {
                        dt.BillingId = null;
                    }
                }

                // Update properties
                currentModel.Customer = customer;
                ApplyCustomerTaxSettings(currentModel);
                currentModel.CustomerId = model.CustomerId;
                currentModel.PrincipalId = model.PrincipalId;
                currentModel.VoyageNumber = model.VoyageNumber;
                currentModel.COSNumber = model.COSNumber;
                currentModel.Date = model.Date;
                currentModel.Year = model.Date.Year;
                currentModel.PortId = model.PortId;
                currentModel.TerminalId = model.TerminalId;
                currentModel.VesselId = model.VesselId;
                currentModel.BilledTo = model.BilledTo;
                if (model.CustomerOrderSlipId.HasValue)
                {
                    currentModel.CustomerOrderSlipId = model.CustomerOrderSlipId;
                }

                if (model.ToBillDispatchTickets != null)
                {
                    decimal total = 0, dispatch = 0, baf = 0;
                    var selectedIds = new HashSet<int>();
                    foreach (var ticketIdStr in model.ToBillDispatchTickets)
                    {
                        var ticketId = int.Parse(ticketIdStr);
                        if (!selectedIds.Add(ticketId))
                        {
                            return ServiceResult.Failure($"Dispatch ticket #{ticketId} was selected more than once.");
                        }

                        var dt = await unitOfWork.DispatchTicket.GetAsync(t => t.DispatchTicketId == ticketId, cancellationToken);
                        if (dt == null)
                        {
                            return ServiceResult.Failure($"Dispatch ticket #{ticketIdStr} not found.");
                        }

                        if (dt.Status != MsapConstants.DispatchTicketStatus.ForBilling ||
                            (dt.BillingId.HasValue && dt.BillingId != currentModel.MsapBillingId))
                        {
                            return ServiceResult.Failure($"Ticket #{dt.DispatchNumber} is not available for billing.");
                        }

                        if (dt.CustomerId != model.CustomerId)
                        {
                            return ServiceResult.Failure($"Ticket #{dt.DispatchNumber} does not belong to the selected customer.");
                        }

                        if (model.BafRates != null && model.BafRates.TryGetValue(dt.DispatchTicketId, out var bafRate))
                        {
                            await ApplyBafRateAsync(dt, bafRate, null, username, cancellationToken);
                        }

                        total += dt.TotalNetRevenue;
                        dispatch += dt.DispatchNetRevenue;
                        baf += dt.BAFNetRevenue;

                        dt.BillingId = currentModel.MsapBillingId;
                        dt.Billing = currentModel;
                    }

                    currentModel.Amount = currentModel.Balance = currentModel is { IsVatable: true, IsVatInclusive: false } ? total * VatMultiplier : total;
                    currentModel.DispatchAmount = dispatch;
                    currentModel.BAFAmount = baf;
                }

                currentModel.Revision++;
                currentModel.ApprovedRevision = null;
                currentModel.ApprovedByUserId = null;
                currentModel.ApprovedAt = null;
                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Edit billing #{currentModel.MsapBillingNumber}", "Billing", currentModel.MsapBillingId, currentModel.MsapBillingNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult.Success("Entry edited successfully!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to edit billing");
                return ServiceResult.Failure($"Failed to edit billing: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> DeleteBillingAsync(int id, string username, CancellationToken cancellationToken)
        {
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var selected = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken)
                        ?? throw new InvalidOperationException("Billing not found.");
                    var basic = selected.BasicBilling ?? selected;
                    var pair = new List<Billing> { basic };
                    if (basic.BafBilling != null)
                    {
                        pair.Add(basic.BafBilling);
                    }
                    foreach (var bill in pair.OrderBy(b => b.MsapBillingId))
                    {
                        await unitOfWork.Billing.GetForUpdateAsync(bill.MsapBillingId, cancellationToken);
                        if (bill.Status != MsapConstants.BillingStatus.ForPosting || bill.AmountPaid != 0 || bill.CollectionId.HasValue)
                        {
                            throw new InvalidOperationException("Only unposted, unpaid billings can be deleted. Both split components must qualify.");
                        }
                        if (await unitOfWork.Billing.HasSalesBookAsync(bill.MsapBillingId, cancellationToken))
                        {
                            throw new InvalidOperationException("This billing has Sales Book history and cannot be deleted. Revise and resubmit the reversed bill instead.");
                        }
                        var guard = await GuardClosedPeriodAsync(bill.Date, cancellationToken);
                        if (guard != null)
                        {
                            throw new InvalidOperationException(guard.Message);
                        }
                    }
                    var tickets = await unitOfWork.DispatchTicket.GetAllAsync(t => t.BillingId == basic.MsapBillingId, cancellationToken);
                    foreach (var ticket in tickets)
                    {
                        ticket.Billing = null;
                        ticket.BillingId = null;
                        ticket.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                    }
                    foreach (var bill in pair.AsEnumerable().Reverse())
                    {
                        await unitOfWork.Billing.RemoveAsync(bill, cancellationToken);
                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Deleted Billing #{bill.MsapBillingNumber}", "Billing", bill.MsapBillingId, bill.MsapBillingNumber), cancellationToken);
                    }
                    if (basic.CustomerOrderSlipId.HasValue)
                    {
                        var job = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == basic.CustomerOrderSlipId.Value, cancellationToken);
                        if (job?.Status == MsapConstants.CustomerOrderSlipStatus.Closed)
                        {
                            job.Status = MsapConstants.CustomerOrderSlipStatus.Open;
                        }
                    }
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);
                return ServiceResult.Success("Billing preparation deleted. Actual service tickets are available for billing again.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete billing");
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }
        public async Task<ServiceResult> ReverseBillingAsync(int id, string username, string? remarks, CancellationToken cancellationToken)
        {
            try
            {
                var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                if (billing == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }

                var guard = await GuardClosedPeriodAsync(billing.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                string billingNumber = billing.MsapBillingNumber;

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var partner = billing.BasicBilling ?? billing.BafBilling;
                    foreach (int billingId in new[] { id, partner?.MsapBillingId ?? id }.Distinct().OrderBy(value => value))
                    {
                        await unitOfWork.Billing.GetForUpdateAsync(billingId, cancellationToken);
                    }
                    billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                    billingNumber = billing!.MsapBillingNumber;

                    if (billing.Status != MsapConstants.BillingStatus.ForCollection)
                    {
                        throw new InvalidOperationException($"Only billings with '{MsapConstants.BillingStatus.ForCollection}' status can be reversed.");
                    }

                    if (billing.CollectionId.HasValue || billing.AmountPaid != 0)
                    {
                        throw new InvalidOperationException($"Billing #{billingNumber} cannot be reversed because it is linked to a Collection (CR#{billing.CollectionNumber}).");
                    }

                    int sourceId = billing.BasicBillingId ?? billing.MsapBillingId;
                    var linkedTickets = await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == sourceId, cancellationToken);
                    foreach (var dt in linkedTickets)
                    {
                        dt.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                    }

                    billing.Status = MsapConstants.BillingStatus.ForPosting;
                    billing.Revision++;
                    billing.ApprovedRevision = null;
                    billing.ApprovedByUserId = null;
                    billing.ApprovedAt = null;
                    billing.UnpostedBy = username;
                    billing.UnpostedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    billing.UnpostRemarks = remarks;

                    await unitOfWork.Billing.VoidSalesBookAsync(billing.MsapBillingId, username, remarks,
                        DateTimeHelper.GetCurrentPhilippineTime(), cancellationToken);

                    await unitOfWork.AuditTrail.AddAsync(
                        new AuditTrail(username, $"Reversed (unposted) Billing #{billingNumber}", "Billing", billing.MsapBillingId, billingNumber),
                        cancellationToken);

                    if (billing.CustomerOrderSlipId.HasValue)
                    {
                        var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetAsync(jo => jo.CustomerOrderSlipId == billing.CustomerOrderSlipId, cancellationToken);
                        if (customerOrderSlip is { Status: MsapConstants.CustomerOrderSlipStatus.Closed })
                        {
                            customerOrderSlip.Status = MsapConstants.CustomerOrderSlipStatus.Open;
                        }
                    }

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success($"Billing #{billingNumber} reversed successfully and is back to '{MsapConstants.BillingStatus.ForPosting}'.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reverse billing {BillingId}", id);
                return ServiceResult.Failure($"Failed to reverse billing: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<(IEnumerable<Billing> Data, int RecordsFiltered, int TotalRecords)> GetPagedBillingsAsync(DataTablesParameters parameters, CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            return await unitOfWork.Billing.GetPagedBillingsAsync(parameters, cancellationToken, customerOrderSlipId);
        }

        public async Task<List<object>> SearchPrincipalsAsync(string? term, int customerId, CancellationToken cancellationToken)
        {
            var result = await unitOfWork.Principal.SearchPrincipalsAsync(term ?? string.Empty, customerId, 10, cancellationToken);

            return result.Select(p => (object)new
            {
                value = p.PrincipalId,
                name = p.PrincipalName,
                address = p.Address1,
                tinNo = p.TIN,
                businessStyle = p.BusinessType,
                terms = p.Terms
            }).ToList();
        }

        public async Task<List<object>> GetBillableCustomerOrderSlipsAsync(int customerId, CancellationToken cancellationToken)
        {
            var result = await unitOfWork.CustomerOrderSlip.SearchBillableCustomerOrderSlipsAsync("", customerId, 999, cancellationToken);

            return result.Select(j => (object)new
            {
                value = j.CustomerOrderSlipId,
                name = j.CustomerOrderSlipNumber,
                description = j.Remarks ?? ""
            }).ToList();
        }

        public async Task<ServiceResult<CustomerOrderSlipBillingDto>> GetDispatchTicketsByCustomerOrderSlipAsync(int customerOrderSlipId, CancellationToken cancellationToken)
        {
            var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetCustomerOrderSlipWithDetailsAsync(customerOrderSlipId, cancellationToken);
            if (customerOrderSlip == null)
            {
                return ServiceResult<CustomerOrderSlipBillingDto>.Failure("COS not found");
            }

            string? blocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, customerOrderSlipId, JobProgressCalculator.BillingStage, cancellationToken);
            if (blocker != null)
            {
                return ServiceResult<CustomerOrderSlipBillingDto>.Failure(blocker);
            }

            var tickets = customerOrderSlip.DispatchTickets
                .Where(t => t is { Status: MsapConstants.DispatchTicketStatus.ForBilling, BillingId: null })
                .Select(t => new CustomerOrderSlipTicketDto
                {
                    DispatchTicketId = t.DispatchTicketId,
                    DispatchNo = t.DispatchNumber,
                    Tugboat = t.Tugboat.TugboatName,
                    Service = t.Service.ServiceName,
                    Duration = t.TotalHours,
                    ActualHours = t.RawDurationHours,
                    IsCompanyOwned = t.Tugboat.IsCompanyOwned,
                    IsManualTariffRate = t.IsManualTariffRate,
                    IsManualBafRate = t.IsManualBafRate,
                    DispatchRate = t.DispatchRate,
                    DispatchNetAmount = t.DispatchNetRevenue,
                    DispatchAmount = t.DispatchBillingAmount,
                    BAFRate = t.BAFRate,
                    BAFDiscount = t.BAFDiscount,
                    BAFChargeType = t.BAFChargeType ?? string.Empty,
                    BafNetAmount = t.BAFNetRevenue,
                    BAFAmount = t.BAFBillingAmount,
                    TotalAmount = t.TotalBilling,
                    Port = t.Terminal?.Port?.PortName,
                    Terminal = t.Terminal?.TerminalName
                }).ToList();

            var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, cancellationToken);
            return ServiceResult<CustomerOrderSlipBillingDto>.Success(new CustomerOrderSlipBillingDto
            {
                Header = new CustomerOrderSlipHeaderDto
                {
                    CustomerOrderSlipNumber = customerOrderSlip.CustomerOrderSlipNumber,
                    VesselName = customerOrderSlip.Vessel.VesselName,
                    PortName = customerOrderSlip.Port.PortName,
                    TerminalName = customerOrderSlip.Terminal.TerminalName,
                    ServiceStart = customerOrderSlip.DispatchTickets.Min(t => t.DateLeft),
                    ServiceEnd = customerOrderSlip.DispatchTickets.Max(t => t.DateArrived),
                    CosDiscountPercent = schedule?.CosDiscountPercent,
                    SkipCosDiscount = schedule?.SkipCosDiscount ?? false,
                    VesselId = customerOrderSlip.VesselId,
                    PortId = customerOrderSlip.PortId,
                    TerminalId = customerOrderSlip.TerminalId,
                    VoyageNumber = customerOrderSlip.VoyageNumber,
                    COSNumber = customerOrderSlip.COSNumber
                },
                Tickets = tickets
            });
        }

        public async Task<ServiceResult<CustomerOrderSlipBillingDto>> GetDispatchTicketsByCustomerAsync(int customerId, CancellationToken cancellationToken)
        {
            var tickets = await unitOfWork.DispatchTicket.GetAllAsync(t =>
                t.CustomerId == customerId &&
                t.Status == MsapConstants.DispatchTicketStatus.ForBilling &&
                t.BillingId == null &&
                t.CustomerOrderSlipId == null,
                cancellationToken);

            var ticketDtos = tickets.Select(t => new CustomerOrderSlipTicketDto
            {
                DispatchTicketId = t.DispatchTicketId,
                DispatchNo = t.DispatchNumber,
                Tugboat = t.Tugboat.TugboatName,
                Service = t.Service.ServiceName,
                Duration = t.TotalHours,
                ActualHours = t.RawDurationHours,
                IsCompanyOwned = t.Tugboat.IsCompanyOwned,
                IsManualTariffRate = t.IsManualTariffRate,
                IsManualBafRate = t.IsManualBafRate,
                DispatchRate = t.DispatchRate,
                DispatchNetAmount = t.DispatchNetRevenue,
                DispatchAmount = t.DispatchBillingAmount,
                BAFRate = t.BAFRate,
                BAFDiscount = t.BAFDiscount,
                BAFChargeType = t.BAFChargeType ?? string.Empty,
                BafNetAmount = t.BAFNetRevenue,
                BAFAmount = t.BAFBillingAmount,
                TotalAmount = t.TotalBilling,
                Port = t.Terminal?.Port?.PortName,
                Terminal = t.Terminal?.TerminalName
            }).ToList();

            return ServiceResult<CustomerOrderSlipBillingDto>.Success(new CustomerOrderSlipBillingDto
            {
                Header = new CustomerOrderSlipHeaderDto(),
                Tickets = ticketDtos
            });
        }

        public async Task<List<SelectListItem>?> GetPrincipalsSelectListAsync(int customerId, CancellationToken cancellationToken)
        {
            var principals = await unitOfWork.Principal.GetAllAsync(t => t.CustomerId == customerId, cancellationToken);
            return principals.Select(t => new SelectListItem
            {
                Value = t.PrincipalId.ToString(),
                Text = t.PrincipalName
            }).ToList();
        }

        public async Task<List<SelectListItem>?> GetEditTicketsSelectListAsync(int? customerId, int billingId, CancellationToken cancellationToken)
        {
            var list = await unitOfWork.Billing.GetMsapUnbilledTicketsByCustomer(customerId, cancellationToken);
            if (billingId != 0)
            {
                var billedTickets = await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == billingId, cancellationToken);
                if (billedTickets.Any() && billedTickets.First().CustomerId == customerId)
                {
                    list?.AddRange(await unitOfWork.Billing.GetMsapBilledTicketsById(billingId, cancellationToken));
                }
            }
            return list;
        }

        public async Task<Billing> PopulateTicketListsAsync(Billing model, CancellationToken cancellationToken)
        {
            model.ToBillDispatchTickets = await unitOfWork.Billing
                .GetToBillDispatchTicketListAsync(model.MsapBillingId, cancellationToken);

            model.PaidDispatchTickets = await unitOfWork.Billing
                .GetPaidDispatchTicketsAsync(model.MsapBillingId, cancellationToken);

            model.UniqueTugboats = await unitOfWork.Billing
                .GetUniqueTugboatsListAsync(model.MsapBillingId, cancellationToken);

            unitOfWork.Billing.ProcessAddress(model, cancellationToken);
            return model;
        }

        public async Task<IEnumerable<DispatchTicket>> GetDispatchTicketsByIdsAsync(List<int> dispatchTicketIds, CancellationToken cancellationToken)
        {
            return await unitOfWork.DispatchTicket
                .GetAllAsync(t => dispatchTicketIds.Contains(t.DispatchTicketId), cancellationToken);
        }

        public async Task<Billing> PopulateBillingSelectListsAsync(Billing model, CancellationToken cancellationToken)
        {
            model.Vessels = await unitOfWork.Vessel.GetMsapVesselsSelectList(cancellationToken);
            model.Ports = await unitOfWork.Port.GetMsapPortsSelectList(cancellationToken);
            model.Customers = await unitOfWork.Billing.GetMsapCustomersWithBillablesSelectList(model.CustomerId, "", cancellationToken);

            if (model.PortId != 0)
            {
                model.Terminals = await unitOfWork.Terminal.GetMsapTerminalsSelectList(model.PortId, cancellationToken);
            }

            return model;
        }

        public async Task<ServiceResult<object>> GetCustomerDetailsAsync(int customerId, CancellationToken cancellationToken)
        {
            var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == customerId, cancellationToken);
            if (customer == null)
            {
                return ServiceResult<object>.Failure("Customer not found.", ServiceResultStatus.NotFound);
            }

            var principals = await unitOfWork.Principal.GetAllAsync(p => p.CustomerId == customerId, cancellationToken);
            var hasPrincipal = principals.Any();

            return ServiceResult<object>.Success(new
            {
                value = customer.CustomerId,
                name = customer.CustomerName,
                address = customer.CustomerAddress,
                tinNo = customer.CustomerTin,
                terms = customer.CustomerTerms,
                vatType = customer.VatType,
                isUndoc = customer.Type,
                businessStyle = customer.BusinessStyle ?? "-",
                withholdingTax = customer.WithHoldingTax,
                withholdingVat = customer.WithHoldingVat,
                hasPrincipal
            });
        }

        public async Task<List<object>> GetPrincipalsByCustomerAsync(int customerId, CancellationToken cancellationToken)
        {
            var principals = await unitOfWork.Principal.GetAllAsync(p => p.CustomerId == customerId && p.IsActive, cancellationToken);
            return principals.Select(p => (object)new
            {
                value = p.PrincipalId,
                name = p.PrincipalName,
                address = p.Address1,
                tinNo = p.TIN,
                businessStyle = p.BusinessType,
                terms = p.Terms
            }).ToList();
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
