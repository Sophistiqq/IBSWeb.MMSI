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
                            $"Created Billing #{bill.MsapBillingNumber}" + (model.SeparateBaf ? " (separate Basic/BAF preparation)" : "")
                            + $". COS discount: {model.CosDiscountPercent}; skipped: {model.SkipCosDiscount}; SBMA: {model.SbmaEnabled}.",
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
            if (model.CosDiscountPercent is < 0 or > 100)
            {
                return ServiceResult<List<Billing>>.Failure("COS discount must be between 0% and 100%.");
            }
            var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == job.CustomerOrderSlipId, cancellationToken);
            if (schedule == null && (model.CosDiscountPercent.HasValue || model.SbmaEnabled.HasValue))
            {
                return ServiceResult<List<Billing>>.Failure("Commercial settings require a linked schedule.");
            }
            if (schedule != null && ((model.CosDiscountPercent.HasValue
                && (model.CosDiscountPercent != schedule.CosDiscountPercent || model.SkipCosDiscount != schedule.SkipCosDiscount))
                || (model.SbmaEnabled.HasValue && model.SbmaEnabled != schedule.SbmaEnabled)))
            {
                foreach (var period in tickets.Select(t => t.Date).Distinct())
                {
                    if (await unitOfWork.PostedPeriod.IsMonthClosedAsync(period.Year, period.Month, cancellationToken))
                    {
                        return ServiceResult<List<Billing>>.Failure("Cannot change commercial settings for tickets in a closed period.");
                    }
                }
                if (model.CosDiscountPercent.HasValue)
                {
                    schedule.CosDiscountPercent = model.CosDiscountPercent;
                    schedule.SkipCosDiscount = model.SkipCosDiscount;
                    foreach (var ticket in tickets)
                    {
                        ScheduledTicketCharges.ApplyDiscount(ticket, model.SkipCosDiscount ? 0m : model.CosDiscountPercent.Value);
                    }
                }
                if (model.SbmaEnabled.HasValue)
                {
                    schedule.SbmaEnabled = model.SbmaEnabled.Value;
                    SbmaShareCalculator.Apply(tickets, schedule.SbmaEnabled);
                }
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
            model.COSNumber = job.CustomerOrderSlipNumber;
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
                        _ = await unitOfWork.Billing.GetForUpdateAsync(billingId, cancellationToken)
                            ?? throw new InvalidOperationException("This billing preparation no longer exists.");
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
                        _ = await unitOfWork.Billing.GetForUpdateAsync(bill.MsapBillingId, cancellationToken)
                            ?? throw new InvalidOperationException("This billing preparation no longer exists.");
                        if (!IsUnapprovedDraft(bill))
                        {
                            throw new InvalidOperationException("Only unapproved, unpaid drafts can be discarded. Both Basic and BAF bills must qualify.");
                        }
                        var guard = await GuardClosedPeriodAsync(bill.Date, cancellationToken);
                        if (guard != null)
                        {
                            throw new InvalidOperationException(guard.Message);
                        }
                    }
                    if ((await unitOfWork.Billing.GetBillingIdsWithSalesBookAsync(pair.Select(b => b.MsapBillingId).ToList(), cancellationToken)).Count > 0)
                    {
                        throw new InvalidOperationException("This preparation has Sales Book history and cannot be discarded. Approved bills must be retained.");
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
                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Discarded draft billing #{bill.MsapBillingNumber}", "Billing", bill.MsapBillingId, bill.MsapBillingNumber), cancellationToken);
                    }
                    if (basic.CustomerOrderSlipId.HasValue)
                    {
                        var job = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == basic.CustomerOrderSlipId.Value, cancellationToken);
                        if (job != null)
                        {
                            if (job.Status == MsapConstants.CustomerOrderSlipStatus.Closed)
                            {
                                job.Status = MsapConstants.CustomerOrderSlipStatus.Open;
                            }
                            await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                                $"Discarded draft billing preparation {string.Join(", ", pair.Select(b => $"#{b.MsapBillingNumber}"))}; actual service tickets retained.",
                                "COS", job.CustomerOrderSlipId, job.CustomerOrderSlipNumber), cancellationToken);
                        }
                    }
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);
                return ServiceResult.Success("Draft billing preparation discarded. Actual service tickets are available for billing again.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to discard draft billing");
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        public async Task<(IEnumerable<Billing> Data, int RecordsFiltered, int TotalRecords)> GetPagedBillingsAsync(DataTablesParameters parameters, CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            var (data, filtered, total) = await unitOfWork.Billing.GetPagedBillingsAsync(parameters, cancellationToken, customerOrderSlipId);
            List<Billing> billings = data.ToList();
            List<int> preparationIds = billings
                .SelectMany(b => new[] { b.MsapBillingId, b.BasicBillingId ?? b.MsapBillingId, b.BafBilling?.MsapBillingId ?? b.MsapBillingId })
                .Distinct().ToList();
            List<int> history = await unitOfWork.Billing.GetBillingIdsWithSalesBookAsync(preparationIds, cancellationToken);
            foreach (Billing bill in billings)
            {
                Billing basic = bill.BasicBilling ?? bill;
                Billing? baf = basic.BafBilling;
                bill.CanDiscard = IsUnapprovedDraft(basic) && !history.Contains(basic.MsapBillingId)
                    && (baf == null || (IsUnapprovedDraft(baf) && !history.Contains(baf.MsapBillingId)));
            }
            return (billings, filtered, total);
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
                    DispatchChargeType = t.DispatchChargeType ?? string.Empty,
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
                    SbmaEnabled = schedule?.SbmaEnabled,
                    IsIoc = schedule?.IsIoc ?? customerOrderSlip.Terminal.IsIoc,
                    HasSchedule = schedule != null,
                    VesselId = customerOrderSlip.VesselId,
                    PortId = customerOrderSlip.PortId,
                    TerminalId = customerOrderSlip.TerminalId,
                    VoyageNumber = customerOrderSlip.VoyageNumber,
                    COSNumber = customerOrderSlip.CustomerOrderSlipNumber
                },
                Tickets = tickets
            });
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
                address = p.Address,
                tinNo = p.TIN,
                businessStyle = p.BusinessType,
                terms = p.Terms
            }).ToList();
        }

        private static bool IsUnapprovedDraft(Billing bill)
        {
            return bill.Status == MsapConstants.BillingStatus.ForPosting && bill.AmountPaid == 0 && !bill.IsPaid
                && !bill.CollectionId.HasValue && !bill.ApprovedRevision.HasValue
                && !bill.ApprovedAt.HasValue && string.IsNullOrEmpty(bill.ApprovedByUserId)
                && !bill.UnpostedDate.HasValue && string.IsNullOrEmpty(bill.UnpostedBy);
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
