using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
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
        IAccessControlService accessControl,
        ICloudStorageService? cloudStorageService = null)
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
                    var existingDraft = await unitOfWork.Billing.GetAsync(
                        b => b.CustomerOrderSlipId == model.CustomerOrderSlipId.Value && b.Status == MsapConstants.BillingStatus.ForPosting && b.BasicBillingId == null,
                        cancellationToken);
                    if (existingDraft != null)
                    {
                        model.SeparateBaf = existingDraft.BafBilling != null;
                    }
                    var prepared = await PrepareBillingAsync(model, username, company, cancellationToken);
                    if (!prepared.IsSuccess || prepared.Data == null)
                    {
                        throw new InvalidOperationException(prepared.Message ?? "Unable to prepare billing.");
                    }
                    if (existingDraft != null)
                    {
                        var fieldDiffs = new List<MsapFieldChange>();
                        void TrackDiff(string field, string label, object? beforeVal, object? afterVal)
                        {
                            var bStr = beforeVal?.ToString() ?? "";
                            var aStr = afterVal?.ToString() ?? "";
                            if (bStr != aStr)
                            {
                                fieldDiffs.Add(new MsapFieldChange { Field = field, Label = label, Before = bStr, After = aStr });
                            }
                        }

                        TrackDiff("MsapBillingNumber", "Billing Number", existingDraft.MsapBillingNumber, model.MsapBillingNumber);
                        TrackDiff("Date", "Billing Date", existingDraft.Date.ToString("yyyy-MM-dd"), model.Date.ToString("yyyy-MM-dd"));
                        TrackDiff("BilledTo", "Billed To", existingDraft.BilledTo, model.BilledTo);
                        TrackDiff("PrincipalId", "Principal", existingDraft.PrincipalId, model.PrincipalId);
                        TrackDiff("Terms", "Payment Terms", existingDraft.Terms, model.Terms);
                        TrackDiff("DueDate", "Due Date", existingDraft.DueDate.ToString("yyyy-MM-dd"), model.DueDate.ToString("yyyy-MM-dd"));
                        TrackDiff("Remarks", "Billing Remarks", existingDraft.Remarks, model.Remarks);
                        TrackDiff("DispatchAmount", "Dispatch Amount", existingDraft.DispatchAmount.ToString("N2"), model.DispatchAmount.ToString("N2"));
                        TrackDiff("BAFAmount", "BAF Amount", existingDraft.BAFAmount.ToString("N2"), model.BAFAmount.ToString("N2"));
                        TrackDiff("Amount", "Total Amount", existingDraft.Amount.ToString("N2"), model.Amount.ToString("N2"));

                        existingDraft.MsapBillingNumber = model.MsapBillingNumber;
                        existingDraft.Date = model.Date;
                        existingDraft.DueDate = model.DueDate;
                        existingDraft.Year = model.Year;
                        existingDraft.BilledTo = model.BilledTo;
                        existingDraft.PrincipalId = model.PrincipalId;
                        existingDraft.Principal = model.Principal;
                        existingDraft.IsPrincipal = model.IsPrincipal;
                        existingDraft.Terms = model.Terms;
                        existingDraft.Remarks = model.Remarks;
                        existingDraft.IsVatable = model.IsVatable;
                        existingDraft.IsVatInclusive = model.IsVatInclusive;
                        existingDraft.PrintWht = model.PrintWht;
                        existingDraft.PrintDiscount = model.PrintDiscount;
                        existingDraft.IsUndocumented = model.IsUndocumented;
                        existingDraft.DispatchAmount = model.DispatchAmount;
                        existingDraft.BAFAmount = model.BAFAmount;
                        existingDraft.Amount = model.Amount;
                        existingDraft.Balance = model.Balance;
                        existingDraft.EditedBy = username;
                        existingDraft.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                        if (existingDraft.BafBilling != null && model.BafBilling != null)
                        {
                            existingDraft.BafBilling.MsapBillingNumber = model.BafBilling.MsapBillingNumber;
                            existingDraft.BafBilling.Date = model.BafBilling.Date;
                            existingDraft.BafBilling.DueDate = model.BafBilling.DueDate;
                            existingDraft.BafBilling.Year = model.BafBilling.Year;
                            existingDraft.BafBilling.BilledTo = model.BafBilling.BilledTo;
                            existingDraft.BafBilling.PrincipalId = model.BafBilling.PrincipalId;
                            existingDraft.BafBilling.Principal = model.BafBilling.Principal;
                            existingDraft.BafBilling.IsPrincipal = model.BafBilling.IsPrincipal;
                            existingDraft.BafBilling.Terms = model.BafBilling.Terms;
                            existingDraft.BafBilling.Remarks = model.BafBilling.Remarks;
                            existingDraft.BafBilling.IsVatable = model.BafBilling.IsVatable;
                            existingDraft.BafBilling.IsVatInclusive = model.BafBilling.IsVatInclusive;
                            existingDraft.BafBilling.PrintWht = model.BafBilling.PrintWht;
                            existingDraft.BafBilling.PrintDiscount = model.BafBilling.PrintDiscount;
                            existingDraft.BafBilling.IsUndocumented = model.BafBilling.IsUndocumented;
                            existingDraft.BafBilling.BAFAmount = model.BafBilling.BAFAmount;
                            existingDraft.BafBilling.Amount = model.BafBilling.Amount;
                            existingDraft.BafBilling.Balance = model.BafBilling.Balance;
                            existingDraft.BafBilling.EditedBy = username;
                            existingDraft.BafBilling.EditedDate = existingDraft.EditedDate;
                        }

                        foreach (var ticket in model.PaidDispatchTickets!)
                        {
                            ticket.BillingId = existingDraft.MsapBillingId;
                            ticket.Billing = existingDraft;
                        }

                        if (existingDraft.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted && fieldDiffs.Count > 0)
                        {
                            if (existingDraft.PeriodStatus == MsapPeriodStatus.Final)
                            {
                                existingDraft.PeriodStatus = MsapPeriodStatus.Adjusted;
                                fieldDiffs.Insert(0, new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted });
                            }
                            await unitOfWork.MsapAdjustmentLog.AddAsync(new MsapAdjustmentLog
                            {
                                DocumentType = "Billing",
                                RecordId = existingDraft.MsapBillingId,
                                ReferenceNumber = existingDraft.MsapBillingNumber,
                                Reason = existingDraft.UnpostRemarks ?? "Billing commercial settings revised",
                                ChangesJson = System.Text.Json.JsonSerializer.Serialize(fieldDiffs),
                                EditedBy = username,
                                EditedDate = existingDraft.EditedDate.Value
                            }, cancellationToken);
                        }

                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                            $"Updated Billing #{existingDraft.MsapBillingNumber} (rev {existingDraft.Revision}). COS discount: {model.CosDiscountPercent}; skipped: {model.SkipCosDiscount}; SBMA: {model.SbmaEnabled}.",
                            "Billing", existingDraft.MsapBillingId, existingDraft.MsapBillingNumber), cancellationToken);
                        await unitOfWork.SaveAsync(cancellationToken);
                        model.MsapBillingId = existingDraft.MsapBillingId;
                        return;
                    }
                    string? expectedBirBasic = null;
                    string? expectedBirBaf = null;
                    if (!model.IsUndocumented)
                    {
                        expectedBirBasic = await unitOfWork.Billing.GetSuggestedBirBillingNumberAsync(company, model.Date.Year, cancellationToken);
                        if (model.SeparateBaf)
                        {
                            expectedBirBaf = unitOfWork.Billing.IncrementBillingSeries(expectedBirBasic);
                        }
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
                        var expectedNumber = bill.BasicBillingId.HasValue ? expectedBirBaf : expectedBirBasic;
                        var isSeriesOverride = !model.IsUndocumented
                            && !string.IsNullOrWhiteSpace(expectedNumber)
                            && !string.Equals(bill.MsapBillingNumber, expectedNumber, StringComparison.OrdinalIgnoreCase);
                        var overrideNote = isSeriesOverride
                            ? $" [Series override: expected {expectedNumber}, prepared {bill.MsapBillingNumber}]"
                            : "";
                        if (isSeriesOverride)
                        {
                            await unitOfWork.MsapAdjustmentLog.AddAsync(new MsapAdjustmentLog
                            {
                                DocumentType = "Billing",
                                RecordId = bill.MsapBillingId,
                                ReferenceNumber = bill.MsapBillingNumber,
                                Reason = $"BIR invoice series override (expected {expectedNumber}, prepared {bill.MsapBillingNumber})",
                                ChangesJson = System.Text.Json.JsonSerializer.Serialize(new[]
                                {
                                    new MsapFieldChange
                                    {
                                        Field = nameof(Billing.MsapBillingNumber),
                                        Label = "Billing Number (Series Override)",
                                        Before = $"Expected {expectedNumber}",
                                        After = bill.MsapBillingNumber
                                    }
                                }),
                                EditedBy = username,
                                EditedDate = DateTimeHelper.GetCurrentPhilippineTime()
                            }, cancellationToken);
                        }
                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                            $"Created Billing #{bill.MsapBillingNumber}" + (model.SeparateBaf ? " (separate Basic/BAF preparation)" : "")
                            + $". COS discount: {model.CosDiscountPercent}; skipped: {model.SkipCosDiscount}; SBMA: {model.SbmaEnabled}.{overrideNote}",
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
            if (model.Date == default || model.CustomerOrderSlipId is not > 0)
            {
                return ServiceResult<List<Billing>>.Failure("Billing date and COS are required.");
            }
            var existingDraft = await unitOfWork.Billing.GetAsync(
                b => b.CustomerOrderSlipId == model.CustomerOrderSlipId.Value && b.Status == MsapConstants.BillingStatus.ForPosting && b.BasicBillingId == null,
                cancellationToken);
            if (existingDraft != null)
            {
                model.SeparateBaf = existingDraft.BafBilling != null;
            }
            var isAuthorizedRevision = existingDraft != null
                && (existingDraft.UnpostedDate.HasValue || existingDraft.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted);
            if (!isAuthorizedRevision)
            {
                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return ServiceResult<List<Billing>>.Failure(guard.Message!);
                }
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
            if (tickets.Count == 0 || tickets.Any(t => t.CustomerId != customer.CustomerId
                || (t.BillingId.HasValue && t.BillingId != existingDraft?.MsapBillingId)
                || t.Status != MsapConstants.DispatchTicketStatus.ForBilling))
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
            if (model.IsUndocumented && (existingDraft == null || string.IsNullOrWhiteSpace(model.MsapBillingNumber)))
            {
                model.MsapBillingNumber = await unitOfWork.Billing.GenerateBillingNumber(model.Date.Year, cancellationToken);
            }
            else if (!model.IsUndocumented && string.IsNullOrWhiteSpace(model.MsapBillingNumber))
            {
                model.MsapBillingNumber = await unitOfWork.Billing.GetSuggestedBirBillingNumberAsync(company, model.Date.Year, cancellationToken);
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
            else if (!model.IsUndocumented && model.SeparateBaf && string.IsNullOrWhiteSpace(bafNumber))
            {
                bafNumber = unitOfWork.Billing.IncrementBillingSeries(basicNumber);
            }
            if (basicNumber.Length is 0 or > 10 || (model.SeparateBaf && (bafNumber.Length is 0 or > 10)))
            {
                return ServiceResult<List<Billing>>.Failure("Each billing number must contain 1 to 10 characters.");
            }
            if (model.SeparateBaf && string.Equals(basicNumber, bafNumber, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<List<Billing>>.Failure("Basic and BAF billing numbers must be different.");
            }
            int? ignoreBasicId = existingDraft?.MsapBillingId;
            int? ignoreBafId = existingDraft?.BafBilling?.MsapBillingId;
            var duplicate = await unitOfWork.Billing.GetAsync(b => b.Company == company && b.Year == model.Date.Year
                && b.MsapBillingId != ignoreBasicId && b.MsapBillingId != ignoreBafId
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
            var canOverrideCommercial = existingDraft != null
                && (existingDraft.RejectedAt.HasValue || existingDraft.UnpostedDate.HasValue);
            if (schedule != null && ((model.CosDiscountPercent.HasValue
                && (model.CosDiscountPercent != schedule.CosDiscountPercent || model.SkipCosDiscount != schedule.SkipCosDiscount))
                || (model.SbmaEnabled.HasValue && model.SbmaEnabled != schedule.SbmaEnabled)))
            {
                if (model.CosDiscountPercent.HasValue
                    && (model.CosDiscountPercent != schedule.CosDiscountPercent || model.SkipCosDiscount != schedule.SkipCosDiscount)
                    && !canOverrideCommercial)
                {
                    return ServiceResult<List<Billing>>.Failure("Discount overrides require a Reviewer or Approver revision request. Use Adjust Billing after rejection.");
                }
                if (!isAuthorizedRevision)
                {
                    foreach (var period in tickets.Select(t => t.Date).Distinct())
                    {
                        if (await unitOfWork.PostedPeriod.IsMonthClosedAsync(period.Year, period.Month, cancellationToken))
                        {
                            return ServiceResult<List<Billing>>.Failure("Cannot change commercial settings for tickets in a closed period.");
                        }
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
            if (string.IsNullOrWhiteSpace(model.Terms))
            {
                model.Terms = (model.Principal?.Terms ?? customer.CustomerTerms) ?? "COD";
            }
            model.Terms = model.Terms.Trim();
            model.Remarks = model.Remarks?.Trim();
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
                    Terms = model.Terms,
                    Remarks = model.Remarks,
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
                    PrintDiscount = model.PrintDiscount,
                    IsUndocumented = model.IsUndocumented,
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
            model.PrintDiscount = model.Customer.PrintDiscount;
        }

        public async Task<ServiceResult> ReviewBillingAsync(int id, int reviewedRevision, string username, string reviewerUserId, CancellationToken cancellationToken)
        {
            try
            {
                var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                if (billing == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    _ = await unitOfWork.Billing.GetForUpdateAsync(billing.MsapBillingId, cancellationToken)
                        ?? throw new InvalidOperationException("This billing preparation no longer exists.");

                    if (string.IsNullOrEmpty(reviewerUserId) || !await accessControl.HasAccessAsync(reviewerUserId, ProcedureEnum.ReviewBilling))
                    {
                        throw new InvalidOperationException("You do not have permission to review prepared billings.");
                    }
                    if (billing.Revision != reviewedRevision)
                    {
                        throw new InvalidOperationException("This bill changed. Refresh and review the current revision before endorsing.");
                    }
                    if (billing.Status != MsapConstants.BillingStatus.ForPosting)
                    {
                        throw new InvalidOperationException($"Billing #{billing.MsapBillingNumber} is already {billing.Status}.");
                    }

                    string? blocker = await JobProgressCalculator.GetBillingBlockerAsync(unitOfWork, billing, JobProgressCalculator.PostingStage, cancellationToken);
                    if (blocker != null)
                    {
                        throw new InvalidOperationException(blocker);
                    }

                    DateTime reviewedAt = DateTimeHelper.GetCurrentPhilippineTime();
                    billing.ReviewedRevision = billing.Revision;
                    billing.ReviewedByUserId = reviewerUserId;
                    billing.ReviewedAt = reviewedAt;
                    billing.Status = MsapConstants.BillingStatus.Reviewed;

                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Reviewed and endorsed billing #{billing.MsapBillingNumber} (rev {billing.Revision}) for final approval.", "Billing", billing.MsapBillingId, billing.MsapBillingNumber)
                    {
                        UserId = reviewerUserId,
                        ActorName = username
                    }, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success($"Billing #{billing.MsapBillingNumber} endorsed for final approval.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to review billing {Id}", id);
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        public async Task<ServiceResult> RejectBillingAsync(int id, string reason, string username, string actorUserId, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Trim().Length > 500)
                {
                    return ServiceResult.Failure("Please provide a valid rejection reason (5 to 500 characters).", ServiceResultStatus.ValidationError);
                }

                var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                if (billing == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var related = billing.BasicBilling ?? billing.BafBilling;
                    foreach (int billingId in new[] { billing.MsapBillingId, related?.MsapBillingId ?? billing.MsapBillingId }.Distinct().OrderBy(v => v))
                    {
                        _ = await unitOfWork.Billing.GetForUpdateAsync(billingId, cancellationToken)
                            ?? throw new InvalidOperationException("This billing preparation no longer exists.");
                    }

                    bool canReview = !string.IsNullOrEmpty(actorUserId) && await accessControl.HasAccessAsync(actorUserId, ProcedureEnum.ReviewBilling);
                    bool canApprove = !string.IsNullOrEmpty(actorUserId) && await accessControl.HasAccessAsync(actorUserId, ProcedureEnum.ApproveFinalBilling);

                    if (billing.Status == MsapConstants.BillingStatus.ForPosting && !canReview)
                    {
                        throw new InvalidOperationException("You do not have permission to reject billings at the Reviewer stage.");
                    }
                    if (billing.Status == MsapConstants.BillingStatus.Reviewed && !canApprove && !canReview)
                    {
                        throw new InvalidOperationException("You do not have permission to reject billings at the Approver stage.");
                    }
                    if (billing.Status is not (MsapConstants.BillingStatus.ForPosting or MsapConstants.BillingStatus.Reviewed))
                    {
                        throw new InvalidOperationException($"Cannot reject billing #{billing.MsapBillingNumber} because its status is {billing.Status}.");
                    }

                    DateTime rejectedAt = DateTimeHelper.GetCurrentPhilippineTime();
                    string stage = billing.Status == MsapConstants.BillingStatus.Reviewed ? "Approver" : "Reviewer";
                    var targetBills = new[] { billing, related }.Where(b => b != null).Distinct().ToList();

                    foreach (var bill in targetBills)
                    {
                        if (bill!.Status is MsapConstants.BillingStatus.ForCollection or MsapConstants.BillingStatus.Collected)
                        {
                            continue;
                        }
                        bill.Revision += 1;
                        bill.Status = MsapConstants.BillingStatus.ForPosting;
                        bill.ReviewedRevision = null;
                        bill.ReviewedByUserId = null;
                        bill.ReviewedAt = null;
                        bill.ApprovedRevision = null;
                        bill.ApprovedByUserId = null;
                        bill.ApprovedAt = null;
                        bill.RejectedByUserId = actorUserId;
                        bill.RejectedAt = rejectedAt;
                        bill.RejectionReason = reason.Trim();

                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"{stage} rejected billing #{bill.MsapBillingNumber} (rev {bill.Revision}). Reason: {reason.Trim()}", "Billing", bill.MsapBillingId, bill.MsapBillingNumber)
                        {
                            UserId = actorUserId,
                            ActorName = username
                        }, cancellationToken);
                    }

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success($"Billing #{billing.MsapBillingNumber} returned to Encoder for revision.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reject billing {Id}", id);
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        public async Task<ServiceResult> AdjustBillingAsync(BillingAdjustViewModel model, string username, string userId, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5 || model.Reason.Trim().Length > 500)
                {
                    return ServiceResult.Failure("Please provide a justification reason between 5 and 500 characters.", ServiceResultStatus.ValidationError);
                }
                if (model.AuthorizationFile is not { Length: > 0 })
                {
                    return ServiceResult.Failure("An approved authorization document (PDF, PNG, or JPEG) is required to adjust billing rates, basis, or discounts.", ServiceResultStatus.ValidationError);
                }
                if (!await OtherTugChargeService.IsSupportedDocumentAsync(model.AuthorizationFile, cancellationToken))
                {
                    return ServiceResult.Failure("Upload a valid PDF, PNG, or JPEG authorization document of up to 10 MB.", ServiceResultStatus.ValidationError);
                }
                if (cloudStorageService == null)
                {
                    return ServiceResult.Failure("Storage service is not configured for authorization documents.");
                }

                var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == model.MsapBillingId, cancellationToken);
                if (billing == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }
                if (billing.Status != MsapConstants.BillingStatus.ForPosting)
                {
                    return ServiceResult.Failure($"Billing #{billing.MsapBillingNumber} must be in For Posting status to adjust.", ServiceResultStatus.ValidationError);
                }
                if (!billing.RejectedAt.HasValue && !billing.UnpostedDate.HasValue)
                {
                    return ServiceResult.Failure("Adjust Billing is only unlocked after a Reviewer/Approver rejection or Finance Head reopening.", ServiceResultStatus.ValidationError);
                }
                if (model.CosDiscountPercent is < 0 or > 100)
                {
                    return ServiceResult.Failure("Discount must be between 0% and 100%.", ServiceResultStatus.ValidationError);
                }

                string extension = Path.GetExtension(model.AuthorizationFile.FileName).ToLowerInvariant();
                string uploadedAttachment = $"adjustment-auth-{Guid.NewGuid():N}{extension}";
                await cloudStorageService.UploadFileAsync(model.AuthorizationFile, uploadedAttachment);

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    int sourceId = billing.BasicBillingId ?? billing.MsapBillingId;
                    var primaryBill = billing.BasicBillingId.HasValue
                        ? await unitOfWork.Billing.GetForUpdateAsync(sourceId, cancellationToken) ?? throw new InvalidOperationException("Primary billing not found.")
                        : await unitOfWork.Billing.GetForUpdateAsync(billing.MsapBillingId, cancellationToken) ?? throw new InvalidOperationException("Billing not found.");
                    var bafBill = primaryBill.BafBilling != null
                        ? await unitOfWork.Billing.GetForUpdateAsync(primaryBill.BafBilling.MsapBillingId, cancellationToken)
                        : null;

                    var tickets = (await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == sourceId, cancellationToken)).ToList();
                    var schedule = primaryBill.CustomerOrderSlipId.HasValue
                        ? await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == primaryBill.CustomerOrderSlipId.Value, cancellationToken)
                        : null;

                    var fieldDiffs = new List<MsapFieldChange>();
                    decimal effectiveDiscount = model.SkipCosDiscount ? 0m : model.CosDiscountPercent;

                    if (schedule != null)
                    {
                        decimal prevDiscount = schedule.SkipCosDiscount ? 0m : (schedule.CosDiscountPercent ?? 0m);
                        if (prevDiscount != effectiveDiscount)
                        {
                            fieldDiffs.Add(new MsapFieldChange
                            {
                                Field = "CosDiscountPercent",
                                Label = "Customer Discount %",
                                Before = $"{prevDiscount:N2}%",
                                After = $"{effectiveDiscount:N2}%"
                            });
                        }
                        schedule.CosDiscountPercent = model.CosDiscountPercent;
                        schedule.SkipCosDiscount = model.SkipCosDiscount;
                    }

                    foreach (var ticketInput in model.Tickets)
                    {
                        var ticket = tickets.FirstOrDefault(t => t.DispatchTicketId == ticketInput.DispatchTicketId);
                        if (ticket == null)
                        {
                            continue;
                        }

                        string newDispatchType = ticketInput.DispatchChargeType == "Per move" ? "Per move" : "Per hour";
                        string newBafType = ticketInput.BAFChargeType == "Per move" ? "Per move" : "Per hour";

                        if (!string.Equals(ticket.DispatchChargeType, newDispatchType, StringComparison.Ordinal))
                        {
                            fieldDiffs.Add(new MsapFieldChange { Field = $"Ticket_{ticket.DispatchTicketId}_DispatchBasis", Label = $"{ticket.DispatchNumber} Dispatch Basis", Before = ticket.DispatchChargeType ?? "—", After = newDispatchType });
                        }
                        if (ticket.DispatchRate != ticketInput.DispatchRate)
                        {
                            fieldDiffs.Add(new MsapFieldChange { Field = $"Ticket_{ticket.DispatchTicketId}_DispatchRate", Label = $"{ticket.DispatchNumber} Tariff Rate", Before = ticket.DispatchRate.ToString("N2"), After = ticketInput.DispatchRate.ToString("N2") });
                            ticket.IsManualTariffRate = true;
                        }
                        if (!string.Equals(ticket.BAFChargeType, newBafType, StringComparison.Ordinal))
                        {
                            fieldDiffs.Add(new MsapFieldChange { Field = $"Ticket_{ticket.DispatchTicketId}_BafBasis", Label = $"{ticket.DispatchNumber} BAF Basis", Before = ticket.BAFChargeType ?? "—", After = newBafType });
                        }
                        if (ticket.BAFRate != ticketInput.BAFRate)
                        {
                            fieldDiffs.Add(new MsapFieldChange { Field = $"Ticket_{ticket.DispatchTicketId}_BafRate", Label = $"{ticket.DispatchNumber} BAF Rate", Before = ticket.BAFRate.ToString("N2"), After = ticketInput.BAFRate.ToString("N2") });
                        }

                        ticket.DispatchChargeType = newDispatchType;
                        ticket.DispatchRate = ticketInput.DispatchRate;
                        ticket.BAFChargeType = newBafType;
                        ticket.BAFRate = ticketInput.BAFRate;
                        ticket.EditedBy = username;
                        ticket.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                        ScheduledTicketCharges.ApplyDiscount(ticket, effectiveDiscount);
                    }

                    decimal oldTotal = primaryBill.Amount + (bafBill?.Amount ?? 0m);
                    primaryBill.DispatchAmount = tickets.Sum(t => t.DispatchNetRevenue);
                    decimal totalBaf = tickets.Sum(t => t.BAFNetRevenue);
                    if (bafBill != null)
                    {
                        primaryBill.BAFAmount = 0m;
                        primaryBill.Amount = primaryBill.Balance = primaryBill.DispatchAmount;
                        bafBill.BAFAmount = totalBaf;
                        bafBill.Amount = bafBill.Balance = totalBaf;
                    }
                    else
                    {
                        primaryBill.BAFAmount = totalBaf;
                        primaryBill.Amount = primaryBill.Balance = primaryBill.DispatchAmount + totalBaf;
                    }
                    decimal newTotal = primaryBill.Amount + (bafBill?.Amount ?? 0m);
                    if (oldTotal != newTotal)
                    {
                        fieldDiffs.Add(new MsapFieldChange { Field = "Amount", Label = "Total Billing Amount", Before = oldTotal.ToString("N2"), After = newTotal.ToString("N2") });
                    }

                    var editedAt = DateTimeHelper.GetCurrentPhilippineTime();
                    foreach (var bill in new[] { primaryBill, bafBill }.Where(b => b != null))
                    {
                        bill!.EditedBy = username;
                        bill.EditedDate = editedAt;
                        bill.AdjustmentAttachmentUrl = uploadedAttachment;

                        if (fieldDiffs.Count > 0)
                        {
                            await unitOfWork.MsapAdjustmentLog.AddAsync(new MsapAdjustmentLog
                            {
                                DocumentType = "Billing",
                                RecordId = bill.MsapBillingId,
                                ReferenceNumber = bill.MsapBillingNumber,
                                Reason = model.Reason.Trim(),
                                AttachmentUrl = uploadedAttachment,
                                ChangesJson = System.Text.Json.JsonSerializer.Serialize(fieldDiffs),
                                EditedBy = username,
                                EditedDate = editedAt
                            }, cancellationToken);
                        }

                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Adjusted billing #{bill.MsapBillingNumber} (rev {bill.Revision}). Reason: {model.Reason.Trim()}", "Billing", bill.MsapBillingId, bill.MsapBillingNumber)
                        {
                            UserId = userId,
                            ActorName = username
                        }, cancellationToken);
                    }

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success($"Billing #{billing.MsapBillingNumber} adjusted and ready for Reviewer re-review.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to adjust billing {Id}", model.MsapBillingId);
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
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

                var isReopenedRevision = billing.UnpostedDate.HasValue
                    || billing.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
                if (!isReopenedRevision)
                {
                    var guard = await GuardClosedPeriodAsync(billing.Date, cancellationToken);
                    if (guard != null)
                    {
                        return guard;
                    }
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
                    var lockedGuard = isReopenedRevision ? null : await GuardClosedPeriodAsync(model.Date, cancellationToken);
                    string? lockedBlocker = await JobProgressCalculator.GetBillingBlockerAsync(unitOfWork, model, JobProgressCalculator.PostingStage, cancellationToken);
                    if (lockedGuard != null || lockedBlocker != null)
                    {
                        throw new InvalidOperationException(lockedGuard?.Message ?? lockedBlocker);
                    }

                    if (model.Status != MsapConstants.BillingStatus.Reviewed)
                    {
                        throw new InvalidOperationException(model.Status == MsapConstants.BillingStatus.ForPosting
                            ? $"Billing #{model.MsapBillingNumber} must be endorsed by a Reviewer before final approval."
                            : $"Billing #{model.MsapBillingNumber} is already {model.Status}.");
                    }
                    if (model.ReviewedRevision != model.Revision || string.IsNullOrEmpty(model.ReviewedByUserId) || !model.ReviewedAt.HasValue)
                    {
                        throw new InvalidOperationException("This billing must be reviewed at the current revision before final approval.");
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

        public async Task<ServiceResult> ReopenBillingForRevisionAsync(int id, string reason, string username, string approverUserId, CancellationToken cancellationToken, IFormFile? authorizationFile = null)
        {
            string? uploadedAttachment = null;
            try
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                {
                    return ServiceResult.Failure("Please provide a valid reason for reopening this billing (minimum 5 characters).", ServiceResultStatus.ValidationError);
                }

                var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                if (billing == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }

                if (string.IsNullOrEmpty(approverUserId) || !await accessControl.HasAccessAsync(approverUserId, ProcedureEnum.ReverseBilling))
                {
                    return ServiceResult.Failure("You do not have permission to reopen approved billings.", ServiceResultStatus.ValidationError);
                }

                if (billing.Status != MsapConstants.BillingStatus.ForCollection || billing.AmountPaid > 0 || billing.IsPaid || billing.CollectionId.HasValue)
                {
                    return ServiceResult.Failure("Cannot reopen billing: it has recorded collections or has already been collected. Void or cancel the collections first.", ServiceResultStatus.ValidationError);
                }

                var related = billing.BasicBilling ?? billing.BafBilling;
                var targetBillsPreview = new[] { billing, related }.Where(b => b != null).Distinct().ToList();
                bool requiresAuthorizationDoc = targetBillsPreview.Any(b => b!.PeriodStatus == MsapPeriodStatus.Final);

                if (requiresAuthorizationDoc && authorizationFile is not { Length: > 0 })
                {
                    return ServiceResult.Failure("An approved authorization document (PDF, PNG, or JPEG) is required to reopen a finalized-period billing.", ServiceResultStatus.ValidationError);
                }

                if (authorizationFile is { Length: > 0 })
                {
                    if (!await OtherTugChargeService.IsSupportedDocumentAsync(authorizationFile, cancellationToken))
                    {
                        return ServiceResult.Failure("Upload a valid PDF, PNG, or JPEG authorization document of up to 10 MB.", ServiceResultStatus.ValidationError);
                    }
                    if (cloudStorageService == null)
                    {
                        return ServiceResult.Failure("Storage service is not configured for authorization documents.");
                    }
                    string extension = Path.GetExtension(authorizationFile.FileName).ToLowerInvariant();
                    uploadedAttachment = $"adjustment-auth-{Guid.NewGuid():N}{extension}";
                    await cloudStorageService.UploadFileAsync(authorizationFile, uploadedAttachment);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var relatedInTx = billing.BasicBilling ?? billing.BafBilling;
                    var targetBills = new[] { billing, relatedInTx }.Where(b => b != null).Distinct().ToList();

                    foreach (var bill in targetBills)
                    {
                        if (bill!.AmountPaid > 0 || bill.IsPaid || bill.CollectionId.HasValue)
                        {
                            throw new InvalidOperationException($"Cannot reopen billing #{bill.MsapBillingNumber}: collections exist.");
                        }

                        int priorRevision = bill.Revision;
                        string priorStatus = bill.Status;

                        bill.Revision += 1;
                        bill.Status = MsapConstants.BillingStatus.ForPosting;
                        bill.UnpostedBy = username;
                        bill.UnpostedDate = DateTimeHelper.GetCurrentPhilippineTime();
                        bill.UnpostRemarks = reason.Trim();
                        bill.ReviewedRevision = null;
                        bill.ReviewedByUserId = null;
                        bill.ReviewedAt = null;
                        bill.ApprovedRevision = null;
                        bill.ApprovedByUserId = null;
                        bill.ApprovedAt = null;

                        await unitOfWork.Billing.VoidSalesBookAsync(bill.MsapBillingId, priorRevision, username, reason.Trim(), cancellationToken);

                        if (bill.PeriodStatus == MsapPeriodStatus.Final)
                        {
                            bill.PeriodStatus = MsapPeriodStatus.Adjusted;
                            var log = new MsapAdjustmentLog
                            {
                                DocumentType = "Billing",
                                RecordId = bill.MsapBillingId,
                                ReferenceNumber = bill.MsapBillingNumber,
                                Reason = reason.Trim(),
                                AttachmentUrl = uploadedAttachment,
                                ChangesJson = System.Text.Json.JsonSerializer.Serialize(new[]
                                {
                                    new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted },
                                    new MsapFieldChange { Field = "Status", Label = "Status", Before = priorStatus, After = MsapConstants.BillingStatus.ForPosting },
                                    new MsapFieldChange { Field = "Revision", Label = "Revision", Before = priorRevision.ToString(), After = bill.Revision.ToString() }
                                }),
                                EditedBy = username,
                                EditedDate = DateTimeHelper.GetCurrentPhilippineTime()
                            };
                            await unitOfWork.MsapAdjustmentLog.AddAsync(log, cancellationToken);
                        }

                        await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Reopened billing #{bill.MsapBillingNumber} for revision (rev {bill.Revision}). Reason: {reason.Trim()}", "Billing", bill.MsapBillingId, bill.MsapBillingNumber), cancellationToken);
                    }

                    int sourceId = billing.BasicBillingId ?? billing.MsapBillingId;
                    var linkedTickets = await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == sourceId, cancellationToken);
                    foreach (var dt in linkedTickets)
                    {
                        dt.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                    }

                    if (billing.CustomerOrderSlipId.HasValue)
                    {
                        var cos = await unitOfWork.CustomerOrderSlip.GetAsync(c => c.CustomerOrderSlipId == billing.CustomerOrderSlipId.Value, cancellationToken);
                        if (cos?.Status == MsapConstants.CustomerOrderSlipStatus.Closed)
                        {
                            cos.Status = MsapConstants.CustomerOrderSlipStatus.Open;
                            cos.EditedBy = username;
                            cos.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                        }
                        var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == billing.CustomerOrderSlipId.Value, cancellationToken);
                        if (schedule?.Status == MsapConstants.VesselScheduleStatus.Completed)
                        {
                            schedule = await unitOfWork.VesselSchedule.GetForUpdateAsync(schedule.VesselScheduleId, cancellationToken);
                            if (schedule != null)
                            {
                                schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
                                schedule.EditedBy = username;
                                schedule.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                            }
                        }
                    }

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success($"Billing #{billing.MsapBillingNumber} reopened for revision successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reopen billing {BillingId} for revision", id);
                return ServiceResult.Failure($"Failed to reopen billing for revision: {ExceptionHelper.GetErrorMessage(ex)}");
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

            var existingDraft = await unitOfWork.Billing.GetAsync(
                b => b.CustomerOrderSlipId == customerOrderSlipId && b.Status == MsapConstants.BillingStatus.ForPosting && b.BasicBillingId == null,
                cancellationToken);
            int? draftBillingId = existingDraft?.MsapBillingId;

            var eligibleTickets = customerOrderSlip.DispatchTickets
                .Where(t => t.Status == MsapConstants.DispatchTicketStatus.ForBilling && (t.BillingId == null || t.BillingId == draftBillingId))
                .ToList();
            var sbmaAmounts = SbmaShareCalculator.Allocate(eligibleTickets, true);
            var tickets = eligibleTickets
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
                    DispatchDiscount = t.DispatchDiscount,
                    DispatchNetAmount = t.DispatchNetRevenue,
                    DispatchAmount = t.DispatchBillingAmount,
                    DispatchChargeType = t.DispatchChargeType ?? string.Empty,
                    BAFRate = t.BAFRate,
                    BAFDiscount = t.BAFDiscount,
                    BAFChargeType = t.BAFChargeType ?? string.Empty,
                    BafNetAmount = t.BAFNetRevenue,
                    BAFAmount = t.BAFBillingAmount,
                    TotalAmount = t.TotalBilling,
                    SbmaShareIfEnabled = sbmaAmounts.GetValueOrDefault(t.DispatchTicketId),
                    Port = t.Terminal?.Port?.PortName,
                    Terminal = t.Terminal?.TerminalName
                }).ToList();

            var activeTickets = customerOrderSlip.DispatchTickets
                .Where(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted)
                .ToList();
            var schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == customerOrderSlipId, cancellationToken);
            return ServiceResult<CustomerOrderSlipBillingDto>.Success(new CustomerOrderSlipBillingDto
            {
                Header = new CustomerOrderSlipHeaderDto
                {
                    CustomerOrderSlipNumber = customerOrderSlip.CustomerOrderSlipNumber,
                    VesselName = customerOrderSlip.Vessel.VesselName,
                    PortName = customerOrderSlip.Port.PortName,
                    TerminalName = customerOrderSlip.Terminal.TerminalName,
                    ServiceStart = activeTickets.Count > 0 ? activeTickets.Min(t => t.DateLeft) : null,
                    ServiceEnd = activeTickets.Count > 0 ? activeTickets.Max(t => t.DateArrived) : null,
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
            model.PaymentTerms = await unitOfWork.Terms.GetFilprideTermsListAsyncByCode(cancellationToken);
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
                printDiscount = customer.PrintDiscount,
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

        public static bool IsUnapprovedDraft(Billing bill)
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
