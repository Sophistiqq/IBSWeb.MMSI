using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Books;
using IBS.Models.MSAP.Enums;
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
        ILogger<BillingService> logger)
    {
        public async Task<Billing?> GetBillingByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
        }

        public async Task<ServiceResult<int>> CreateBillingAsync(Billing model, string username, string company, CancellationToken cancellationToken)
        {
            try
            {
                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return ServiceResult<int>.Failure(guard.Message!);
                }

                if (model.CustomerOrderSlipId is 0)
                {
                    model.CustomerOrderSlipId = null;
                }

                if (model.CustomerOrderSlipId.HasValue)
                {
                    var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetCustomerOrderSlipWithDetailsAsync(model.CustomerOrderSlipId.Value, cancellationToken);
                    if (customerOrderSlip != null)
                    {
                        if (model.CustomerId == 0)
                        {
                            model.CustomerId = customerOrderSlip.CustomerId;
                        }

                        if (model.VesselId == 0)
                        {
                            model.VesselId = customerOrderSlip.VesselId;
                        }

                        if (model.PortId == 0)
                        {
                            model.PortId = customerOrderSlip.PortId;
                        }

                        if (model.TerminalId == 0)
                        {
                            model.TerminalId = customerOrderSlip.TerminalId;
                        }

                        if (string.IsNullOrWhiteSpace(model.VoyageNumber))
                        {
                            model.VoyageNumber = customerOrderSlip.VoyageNumber;
                        }

                        if (string.IsNullOrWhiteSpace(model.COSNumber))
                        {
                            model.COSNumber = customerOrderSlip.COSNumber;
                        }
                    }
                }


                string? progressBlocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, model.CustomerOrderSlipId, JobProgressCalculator.BillingStage, cancellationToken);
                if (progressBlocker != null)
                {
                    return ServiceResult<int>.Failure(progressBlocker);
                }

                var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == model.CustomerId, cancellationToken);
                if (customer == null)
                {
                    return ServiceResult<int>.Failure("Customer not found.");
                }

                model.Customer = customer;
                model.Year = model.Date.Year;
                model.Status = MsapConstants.BillingStatus.ForPosting; // Changed from ForCollection
                model.CreatedBy = username;
                model.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();
                model.Company = company;

                if (model.PrincipalId.HasValue && model.PrincipalId != 0)
                {
                    var principal = await unitOfWork.Principal.GetAsync(p => p.PrincipalId == model.PrincipalId.Value, cancellationToken);
                    if (principal != null)
                    {
                        model.Principal = principal;
                    }
                }

                model.Terms = model.PrincipalId != null && model.PrincipalId != 0
                    ? model.Principal?.Terms
                    : model.Customer.CustomerTerms;

                if (string.IsNullOrEmpty(model.Terms))
                {
                    model.Terms = "COD";
                }

                model.DueDate = await unitOfWork.Terms.ComputeDueDateAsync(model.Terms, model.Date, cancellationToken);

                if (model.IsUndocumented)
                {
                    model.MsapBillingNumber = await unitOfWork.Billing.GenerateBillingNumber(model.Year, cancellationToken);
                }
                else if (string.IsNullOrWhiteSpace(model.MsapBillingNumber))
                {
                    return ServiceResult<int>.Failure("Billing Number is required.");
                }

                if (model.ToBillDispatchTickets == null || !model.ToBillDispatchTickets.Any())
                {
                    return ServiceResult<int>.Failure("At least one dispatch ticket must be selected.");
                }

                decimal total = 0, dispatch = 0, baf = 0;
                var selectedIds = new HashSet<int>();
                foreach (var ticketIdStr in model.ToBillDispatchTickets)
                {
                    var ticketId = int.Parse(ticketIdStr);
                    if (!selectedIds.Add(ticketId))
                    {
                        return ServiceResult<int>.Failure($"Dispatch ticket #{ticketId} was selected more than once.");
                    }

                    var dt = await unitOfWork.DispatchTicket.GetAsync(t => t.DispatchTicketId == ticketId, cancellationToken);
                    if (dt == null)
                    {
                        return ServiceResult<int>.Failure($"Dispatch ticket #{ticketIdStr} not found.");
                    }

                    if (model.CustomerOrderSlipId.HasValue && dt.CustomerOrderSlipId != model.CustomerOrderSlipId)
                    {
                        return ServiceResult<int>.Failure($"Ticket #{dt.DispatchNumber} does not belong to the selected COS.");
                    }

                    if (dt.CustomerId != model.CustomerId)
                    {
                        return ServiceResult<int>.Failure($"Ticket #{dt.DispatchNumber} does not belong to the selected customer.");
                    }

                    string? ticketBlocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, dt.CustomerOrderSlipId, JobProgressCalculator.BillingStage, cancellationToken);
                    if (ticketBlocker != null)
                    {
                        return ServiceResult<int>.Failure(ticketBlocker);
                    }

                    if (dt.Status != MsapConstants.DispatchTicketStatus.ForBilling || dt.BillingId.HasValue)
                    {
                        return ServiceResult<int>.Failure($"Ticket #{dt.DispatchNumber} is not available for billing.");
                    }

                    if (model.BafRates != null && model.BafRates.TryGetValue(dt.DispatchTicketId, out var bafRate))
                    {
                        var chargeType = model.BafChargeTypes != null && model.BafChargeTypes.TryGetValue(dt.DispatchTicketId, out var ct)
                            ? ct
                            : null;
                        await ApplyBafRateAsync(dt, bafRate, chargeType, username, cancellationToken);
                    }

                    total += dt.TotalNetRevenue;
                    dispatch += dt.DispatchNetRevenue;
                    baf += dt.BAFNetRevenue;

                    dt.Billing = model;
                }

                model.Amount = model.Balance = model is { IsVatable: true, IsVatInclusive: false } ? total * VatMultiplier : total;
                model.DispatchAmount = dispatch;
                model.BAFAmount = baf;
                model.IsPaid = false;

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    await unitOfWork.Billing.AddAsync(model, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Created Billing #{model.MsapBillingNumber}", "Billing", model.MsapBillingId, model.MsapBillingNumber), cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult<int>.Success(model.MsapBillingId, "Billing created successfully. Status: For Posting");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create billing");
                return ServiceResult<int>.Failure($"Failed to create billing: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        private async Task ApplyBafRateAsync(DispatchTicket dt, decimal newRate, string? chargeType, string username, CancellationToken cancellationToken)
        {
            var rateChanged = dt.BAFRate != newRate;
            var typeChanged = !string.IsNullOrWhiteSpace(chargeType) && dt.BAFChargeType != chargeType;
            if (!rateChanged && !typeChanged)
            {
                return;
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

        public async Task<ServiceResult> PostBillingAsync(int id, string username, CancellationToken cancellationToken)
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
                    var model = billing;

                    if (model.Status != MsapConstants.BillingStatus.ForPosting)
                    {
                        throw new InvalidOperationException($"Billing #{model.MsapBillingNumber} is already {model.Status}.");
                    }

                    // Mark linked tickets as Billed
                    var linkedTickets = await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == model.MsapBillingId, cancellationToken);
                    foreach (var dt in linkedTickets)
                    {
                        dt.Status = MsapConstants.DispatchTicketStatus.Billed;
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

                    var soldToName = (model.PrincipalId != null ? model.Principal?.PrincipalName : customer.CustomerName) ?? string.Empty;
                    var tinNo = (model.PrincipalId != null ? model.Principal?.TIN : customer.CustomerTin) ?? string.Empty;
                    var address = (model.PrincipalId != null ? model.Principal?.Address1 : customer.CustomerAddress) ?? string.Empty;

                    var salesBook = new SalesBook
                    {
                        TransactionDate = model.Date,
                        SerialNo = model.MsapBillingNumber,
                        SoldTo = soldToName,
                        TinNo = tinNo,
                        Address = address,
                        Description = model.Vessel.VesselName,
                        Amount = model.Amount - model.Discount
                    };

                    if (model.IsVatable)
                    {
                        salesBook.VatableSales = unitOfWork.Billing.ComputeNetOfVat(salesBook.Amount);
                        salesBook.VatAmount = unitOfWork.Billing.ComputeVatAmount(salesBook.VatableSales);
                        salesBook.NetSales = salesBook.VatableSales - salesBook.Discount;
                    }
                    else
                    {
                        salesBook.ZeroRated = salesBook.Amount;
                        salesBook.NetSales = salesBook.ZeroRated - salesBook.Discount;
                    }

                    salesBook.Discount = model.Discount;
                    salesBook.CreatedBy = username;
                    salesBook.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    salesBook.DueDate = model.DueDate;
                    salesBook.DocumentId = model.MsapBillingId;
                    salesBook.Company = model.Company;

                    model.Status = MsapConstants.BillingStatus.ForCollection;

                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Posted Billing #{model.MsapBillingNumber}", "Billing", model.MsapBillingId, model.MsapBillingNumber), cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);

                    if (model.CustomerOrderSlipId.HasValue)
                    {
                        await customerOrderSlipService.TryAutoCloseAsync(model.CustomerOrderSlipId.Value, username, cancellationToken);
                    }
                }, cancellationToken);

                return ServiceResult.Success($"Billing posted successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to post billing {BillingId}", id);
                return ServiceResult.Failure($"Failed to post billing: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> UpdateBillingAsync(Billing model, string username, CancellationToken cancellationToken)
        {
            try
            {
                var currentModel = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == model.MsapBillingId, cancellationToken);
                if (currentModel == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
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
                currentModel.IsVatable = model.IsVatable;
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
                currentModel.IsVatInclusive = model.IsVatInclusive;
                currentModel.PrintWht = model.PrintWht;

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
                var model = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                if (model == null)
                {
                    return ServiceResult.Failure("Billing not found.", ServiceResultStatus.NotFound);
                }

                if (model.Status != MsapConstants.BillingStatus.ForPosting)
                {
                    return ServiceResult.Failure("Only billings with 'For Posting' status can be deleted.");
                }

                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                {
                    return guard;
                }

                var linkedTickets = await unitOfWork.DispatchTicket
                    .GetAllAsync(dt => dt.BillingId == id, cancellationToken);
                foreach (var dt in linkedTickets)
                {
                    dt.BillingId = null;
                }

                await unitOfWork.Billing.RemoveAsync(model, cancellationToken);
                await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username, $"Deleted Billing #{model.MsapBillingNumber}", "Billing", model.MsapBillingId, model.MsapBillingNumber), cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                // Re-open the COS if it was auto-closed
                var customerOrderSlipId = linkedTickets.FirstOrDefault()?.CustomerOrderSlipId;
                if (customerOrderSlipId.HasValue)
                {
                    var customerOrderSlip = await unitOfWork.CustomerOrderSlip.GetAsync(jo => jo.CustomerOrderSlipId == customerOrderSlipId.Value, cancellationToken);
                    if (customerOrderSlip?.Status == MsapConstants.CustomerOrderSlipStatus.Closed)
                    {
                        customerOrderSlip.Status = MsapConstants.CustomerOrderSlipStatus.Open;
                        await unitOfWork.SaveAsync(cancellationToken);
                    }
                }

                return ServiceResult.Success("Billing deleted successfully!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete billing");
                return ServiceResult.Failure($"Failed to delete billing: {ExceptionHelper.GetErrorMessage(ex)}");
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
                    billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == id, cancellationToken);
                    billingNumber = billing!.MsapBillingNumber;

                    if (billing.Status != MsapConstants.BillingStatus.ForCollection)
                    {
                        throw new InvalidOperationException($"Only billings with '{MsapConstants.BillingStatus.ForCollection}' status can be reversed.");
                    }

                    if (billing.CollectionId.HasValue)
                    {
                        throw new InvalidOperationException($"Billing #{billingNumber} cannot be reversed because it is linked to a Collection (CR#{billing.CollectionNumber}).");
                    }

                    var linkedTickets = await unitOfWork.DispatchTicket.GetAllAsync(dt => dt.BillingId == billing.MsapBillingId, cancellationToken);
                    foreach (var dt in linkedTickets)
                    {
                        dt.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                    }

                    billing.Status = MsapConstants.BillingStatus.ForPosting;
                    billing.UnpostedBy = username;
                    billing.UnpostedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    billing.UnpostRemarks = remarks;

                    //  GL contra entries skipped — GL module is still TODO
                    // When GL posting is fixed, create reverse entries here:
                    // Debit Credit (original) → Credit Debit (contra) with reference "REV-#{number}"

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
                    DispatchRate = t.DispatchRate,
                    DispatchAmount = t.DispatchBillingAmount,
                    BAFRate = t.BAFRate,
                    BAFDiscount = t.BAFDiscount,
                    BAFChargeType = t.BAFChargeType ?? string.Empty,
                    BAFAmount = t.BAFBillingAmount,
                    TotalAmount = t.TotalBilling,
                    Port = t.Terminal?.Port?.PortName,
                    Terminal = t.Terminal?.TerminalName
                }).ToList();

            return ServiceResult<CustomerOrderSlipBillingDto>.Success(new CustomerOrderSlipBillingDto
            {
                Header = new CustomerOrderSlipHeaderDto
                {
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
                DispatchRate = t.DispatchRate,
                DispatchAmount = t.DispatchBillingAmount,
                BAFRate = t.BAFRate,
                BAFDiscount = t.BAFDiscount,
                BAFChargeType = t.BAFChargeType ?? string.Empty,
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
            var principals = await unitOfWork.Principal.GetAllAsync(p => p.CustomerId == customerId, cancellationToken);
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

        /// <summary>
        /// Creates two billings for PHIL-CEB / THERMA SOUTH:
        /// Billing 1 – tickets linked, amount = dispatch revenue only.
        /// Billing 2 – no tickets, amount = BAF revenue, user-supplied billing number.
        /// </summary>
        public async Task<ServiceResult<(int DispatchBillingId, int BafBillingId)>> CreatePhilCebSplitAsync(
            Billing model, string bafBillingNumber, string username, string company, CancellationToken cancellationToken)
        {
            try
            {
                var guard = await GuardClosedPeriodAsync(model.Date, cancellationToken);
                if (guard != null)
                    return ServiceResult<(int, int)>.Failure(guard.Message!);

                if (string.IsNullOrWhiteSpace(model.MsapBillingNumber))
                    return ServiceResult<(int, int)>.Failure("Billing Number is required.");

                if (string.IsNullOrWhiteSpace(bafBillingNumber))
                    return ServiceResult<(int, int)>.Failure("BAF Billing Number is required.");

                // Duplicate check
                var dupMain = await unitOfWork.Billing.GetAsync(b => b.MsapBillingNumber == model.MsapBillingNumber, cancellationToken);
                if (dupMain != null)
                    return ServiceResult<(int, int)>.Failure($"Billing number '{model.MsapBillingNumber}' already exists.");

                var dupBaf = await unitOfWork.Billing.GetAsync(b => b.MsapBillingNumber == bafBillingNumber, cancellationToken);
                if (dupBaf != null)
                    return ServiceResult<(int, int)>.Failure($"BAF Billing number '{bafBillingNumber}' already exists.");

                if (model.CustomerOrderSlipId is 0) model.CustomerOrderSlipId = null;

                if (model.ToBillDispatchTickets == null || !model.ToBillDispatchTickets.Any())
                    return ServiceResult<(int, int)>.Failure("At least one dispatch ticket must be selected.");

                var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == model.CustomerId, cancellationToken);
                if (customer == null)
                    return ServiceResult<(int, int)>.Failure("Customer not found.");

                var now = DateTimeHelper.GetCurrentPhilippineTime();

                // Resolve optional principal
                if (model.PrincipalId.HasValue && model.PrincipalId != 0)
                    model.Principal = await unitOfWork.Principal.GetAsync(p => p.PrincipalId == model.PrincipalId.Value, cancellationToken);

                var terms = (model.PrincipalId > 0 ? model.Principal?.Terms : customer.CustomerTerms) ?? "COD";
                var dueDate = await unitOfWork.Terms.ComputeDueDateAsync(terms, model.Date, cancellationToken);

                decimal dispatch = 0, baf = 0;
                var ticketEntities = new List<DispatchTicket>();
                foreach (var idStr in model.ToBillDispatchTickets)
                {
                    var dt = await unitOfWork.DispatchTicket.GetAsync(t => t.DispatchTicketId == int.Parse(idStr), cancellationToken);
                    if (dt == null)
                        return ServiceResult<(int, int)>.Failure($"Dispatch ticket #{idStr} not found.");

                    if (dt.Status != MsapConstants.DispatchTicketStatus.ForBilling || dt.BillingId.HasValue
                        || dt.CustomerId != model.CustomerId || (model.CustomerOrderSlipId.HasValue && dt.CustomerOrderSlipId != model.CustomerOrderSlipId))
                    {
                        return ServiceResult<(int, int)>.Failure($"Ticket #{dt.DispatchNumber} is not available for this billing.");
                    }
                    string? blocker = await JobProgressCalculator.GetBlockerAsync(unitOfWork, dt.CustomerOrderSlipId, JobProgressCalculator.BillingStage, cancellationToken);
                    if (blocker != null)
                    {
                        return ServiceResult<(int, int)>.Failure(blocker);
                    }

                    if (model.BafRates != null && model.BafRates.TryGetValue(dt.DispatchTicketId, out var bafRate))
                    {
                        await ApplyBafRateAsync(dt, bafRate, null, username, cancellationToken);
                    }

                    dispatch += dt.DispatchNetRevenue;
                    baf += dt.BAFNetRevenue;
                    ticketEntities.Add(dt);
                }

                // ── Billing 1: dispatch tickets, dispatch amount only ──────
                var billing1 = new Billing
                {
                    MsapBillingNumber = model.MsapBillingNumber,
                    Date = model.Date,
                    DueDate = dueDate,
                    Year = model.Date.Year,
                    CustomerId = model.CustomerId,
                    Customer = customer,
                    PrincipalId = model.PrincipalId,
                    Principal = model.Principal,
                    BilledTo = model.BilledTo,
                    VesselId = model.VesselId,
                    PortId = model.PortId,
                    TerminalId = model.TerminalId,
                    VoyageNumber = model.VoyageNumber,
                    COSNumber = model.COSNumber,
                    CustomerOrderSlipId = model.CustomerOrderSlipId,
                    IsVatable = model.IsVatable,
                    IsVatInclusive = model.IsVatInclusive,
                    PrintWht = model.PrintWht,
                    Terms = terms,
                    Company = company,
                    Status = MsapConstants.BillingStatus.ForPosting,
                    CreatedBy = username,
                    CreatedDate = now,
                    IsPaid = false,
                    DispatchAmount = dispatch,
                    BAFAmount = 0,
                };
                billing1.Amount = billing1.Balance = billing1 is { IsVatable: true, IsVatInclusive: false }
                    ? dispatch * VatMultiplier : dispatch;

                var bafTickets = new List<DispatchTicket>();

                foreach (var dt in ticketEntities)
                {
                    // Capture original BAF rate & amounts before zeroing out on original ticket
                    var origBafRate = dt.BAFRate;
                    var origBafBillingAmt = dt.BAFBillingAmount;
                    var origBafNetRev = dt.BAFNetRevenue;

                    dt.Billing = billing1;
                    // Zero out BAF on original ticket so Billing 1 only shows tickets/dispatch
                    dt.BAFBillingAmount = 0;
                    dt.BAFNetRevenue = 0;
                    dt.BAFRate = 0;
                    dt.TotalBilling = dt.DispatchBillingAmount;
                    dt.TotalNetRevenue = dt.DispatchNetRevenue;

                    // Clone ticket for BAF billing (Billing 2)
                    // Keep BAF rate & amounts in BAF fields so Preview renders under BUNKER ADJUSTMENT FACTORS
                    var bafTicket = new DispatchTicket
                    {
                        DispatchNumber = string.IsNullOrEmpty(dt.DispatchNumber) ? "BAF-A" : $"{dt.DispatchNumber}-A",
                        Date = dt.Date,
                        DateLeft = dt.DateLeft,
                        TimeLeft = dt.TimeLeft,
                        DateArrived = dt.DateArrived,
                        TimeArrived = dt.TimeArrived,
                        TotalHours = dt.TotalHours,
                        CustomerId = dt.CustomerId,
                        VesselId = dt.VesselId,
                        PortId = dt.PortId,
                        TerminalId = dt.TerminalId,
                        TugBoatId = dt.TugBoatId,
                        TugMasterId = dt.TugMasterId,
                        ServiceId = dt.ServiceId,
                        CustomerOrderSlipId = dt.CustomerOrderSlipId,
                        Status = dt.Status,
                        CreatedBy = username,
                        CreatedDate = now,
                        DispatchRate = 0,
                        DispatchBillingAmount = 0,
                        DispatchNetRevenue = 0,
                        BAFRate = origBafRate,
                        BAFBillingAmount = origBafBillingAmt,
                        BAFNetRevenue = origBafNetRev,
                        TotalBilling = origBafBillingAmt,
                        TotalNetRevenue = origBafNetRev
                    };
                    bafTickets.Add(bafTicket);
                }

                await unitOfWork.Billing.AddAsync(billing1, cancellationToken);
                await unitOfWork.AuditTrail.AddAsync(
                    new AuditTrail(username, $"Created Billing #{billing1.MsapBillingNumber} (PHIL-CEB dispatch split)", "Billing", billing1.MsapBillingId, billing1.MsapBillingNumber),
                    cancellationToken);

                // ── Billing 2: BAF tickets with -A suffix ────────────────
                var billing2 = new Billing
                {
                    MsapBillingNumber = bafBillingNumber,
                    Date = model.Date,
                    DueDate = dueDate,
                    Year = model.Date.Year,
                    CustomerId = model.CustomerId,
                    PrincipalId = model.PrincipalId,
                    BilledTo = model.BilledTo,
                    VesselId = model.VesselId,
                    PortId = model.PortId,
                    TerminalId = model.TerminalId,
                    VoyageNumber = model.VoyageNumber,
                    COSNumber = model.COSNumber,
                    CustomerOrderSlipId = model.CustomerOrderSlipId,
                    IsVatable = model.IsVatable,
                    IsVatInclusive = model.IsVatInclusive,
                    PrintWht = model.PrintWht,
                    Terms = terms,
                    Company = company,
                    Status = MsapConstants.BillingStatus.ForPosting,
                    CreatedBy = username,
                    CreatedDate = now,
                    IsPaid = false,
                    DispatchAmount = 0,
                    BAFAmount = baf,
                };
                billing2.Amount = billing2.Balance = billing2 is { IsVatable: true, IsVatInclusive: false }
                    ? baf * VatMultiplier : baf;

                foreach (var bt in bafTickets)
                {
                    bt.Billing = billing2;
                    await unitOfWork.DispatchTicket.AddAsync(bt, cancellationToken);
                }

                await unitOfWork.Billing.AddAsync(billing2, cancellationToken);
                await unitOfWork.AuditTrail.AddAsync(
                    new AuditTrail(username, $"Created Billing #{billing2.MsapBillingNumber} (PHIL-CEB BAF split)", "Billing", billing2.MsapBillingId, billing2.MsapBillingNumber),
                    cancellationToken);

                await unitOfWork.SaveAsync(cancellationToken);

                return ServiceResult<(int, int)>.Success(
                    (billing1.MsapBillingId, billing2.MsapBillingId),
                    $"Billings created: #{billing1.MsapBillingNumber} (dispatch) and #{billing2.MsapBillingNumber} (BAF).");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create PHIL-CEB split billing");
                return ServiceResult<(int, int)>.Failure($"Failed to create split billing: {ExceptionHelper.GetErrorMessage(ex)}");
            }
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
