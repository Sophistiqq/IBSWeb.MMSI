using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using static IBS.Utility.MSAP.Constants.TaxConstants;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class CollectionService(
        IUnitOfWork unitOfWork,
        ILogger<CollectionService> logger)
    {

        public async Task<Collection?> GetCollectionByIdAsync(int id, CancellationToken cancellationToken)
        {
            var collection = await unitOfWork.Collection.GetAsync(c => c.MsapCollectionId == id, cancellationToken);
            if (collection != null)
            {
                collection.PaidBills = (await unitOfWork.Billing.GetAllAsync(b => b.CollectionId == collection.MsapCollectionId, cancellationToken)).ToList();
            }
            return collection;
        }

        public async Task<List<SelectListItem>> GetBankAccountsAsync(CancellationToken ct)
        {
            return (await unitOfWork.BankAccount.GetAllAsync(b => b.Company == MsapConstants.Company_MMSI, ct))
                .Select(b => new SelectListItem
                {
                    Value = b.BankAccountId.ToString(),
                    Text = $"{b.Bank} {b.AccountNo} {b.AccountName}"
                }).ToList();
        }

        public async Task<ServiceResult> UpdateBankingAsync(int id, CollectionBankingAction action, DateOnly date,
            int? bankId, string username, CancellationToken ct)
        {
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var collection = await unitOfWork.Collection.GetForUpdateAsync(id, ct)
                        ?? throw new InvalidOperationException("Collection not found.");
                    var guard = await GuardClosedPeriodAsync(collection.Date, ct) ?? await GuardClosedPeriodAsync(date, ct);
                    if (guard != null)
                    {
                        throw new InvalidOperationException(guard.Message);
                    }
                    if (date == default || date < collection.Date || date > DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()))
                    {
                        throw new InvalidOperationException("Use an actual transaction date on or after the collection date, up to today.");
                    }
                    if (collection.ClearedDate.HasValue)
                    {
                        throw new InvalidOperationException("This collection has already cleared.");
                    }

                    string description;
                    switch (action)
                    {
                        case CollectionBankingAction.Deposit:
                            if (collection.DepositDate.HasValue)
                            {
                                throw new InvalidOperationException("This collection is already deposited.");
                            }
                            if ((collection.CheckAmount > 0 && date < collection.CheckDate) || date < collection.ReturnedDate)
                            {
                                throw new InvalidOperationException("Deposit date cannot precede the check date or the last return date.");
                            }
                            var bank = bankId.HasValue
                                ? await unitOfWork.BankAccount.GetAsync(b => b.BankAccountId == bankId.Value && b.Company == MsapConstants.Company_MMSI, ct) : null;
                            if (bank == null)
                            {
                                throw new InvalidOperationException("Select a valid MMSI bank account.");
                            }
                            collection.BankId = bank.BankAccountId;
                            collection.BankAccountName = bank.AccountName;
                            collection.BankAccountNumber = bank.AccountNo;
                            collection.DepositDate = date;
                            description = collection.ReturnedDate.HasValue ? "Redeposit" : "Deposit";
                            break;
                        case CollectionBankingAction.ReturnCheck:
                            if (collection.CheckAmount <= 0 || !collection.DepositDate.HasValue || date < collection.DepositDate)
                            {
                                throw new InvalidOperationException("Only a deposited check can be returned, on or after its deposit date.");
                            }
                            collection.ReturnedDate = date;
                            collection.DepositDate = null;
                            description = "Return check";
                            break;
                        case CollectionBankingAction.Clear:
                            if (!collection.DepositDate.HasValue || date < collection.DepositDate)
                            {
                                throw new InvalidOperationException("Record a deposit first. Clearing date cannot precede its deposit date.");
                            }
                            collection.ClearedDate = date;
                            description = "Clear";
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported banking action.");
                    }

                    collection.EditedBy = username;
                    collection.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                        $"{description} collection #{collection.MsapCollectionNumber} on {date:yyyy-MM-dd}, bank #{collection.BankId}",
                        "Collection", collection.MsapCollectionId, collection.MsapCollectionNumber), ct);
                    await unitOfWork.SaveAsync(ct);
                }, ct);
                return ServiceResult.Success("Collection banking details updated successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update collection banking details");
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        private static (decimal Ewt, decimal Wvat, decimal Net) GetSettlementAmounts(Billing billing, Customer customer, decimal gross)
        {
            decimal ewt = customer.WithHoldingTax && billing.BilledTo == MsapConstants.BilledToLocal
                ? Math.Round(billing.IsVatable ? gross / VatMultiplier * EwtRate : gross * EwtRate, 2) : 0;
            decimal wvat = customer.WithHoldingVat && billing.BilledTo == MsapConstants.BilledToLocal && billing.IsVatable
                ? Math.Round(gross / VatMultiplier * WvatRate, 2) : 0;
            return (ewt, wvat, Math.Round(gross - ewt - wvat, 2));
        }

        private async Task<Dictionary<int, decimal>> GetSettlementAllocationsAsync(CreateCollectionViewModel model, CancellationToken ct)
        {
            if (model.Date == default || (model.CheckAmount > 0 &&
                (!model.CheckDate.HasValue || string.IsNullOrWhiteSpace(model.CheckNumber) || string.IsNullOrWhiteSpace(model.CheckBank))))
            {
                throw new InvalidOperationException("Enter a collection date and, for checks, the check date, number and issuing bank.");
            }
            if (model.BillingPayments == null || model.BillingPayments.Count == 0)
            {
                throw new InvalidOperationException("Select at least one outstanding billing to collect.");
            }
            if (model.BillingPayments.Select(p => p.BillingId).Distinct().Count() != model.BillingPayments.Count)
            {
                throw new InvalidOperationException("Each billing can only be allocated once in a collection.");
            }
            if (model.Amount != model.BillingPayments.Sum(p => p.AmountToPay) || model.Amount != model.CashAmount + model.CheckAmount
                || model.CashAmount < 0 || model.CheckAmount < 0 || model.EWT < 0 || model.WVAT < 0)
            {
                throw new InvalidOperationException("Cash plus check must match the allocated net payments. Withholding amounts are recorded separately.");
            }
            var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == model.CustomerId, ct)
                ?? throw new InvalidOperationException("Customer not found.");
            List<int> ids = model.BillingPayments.Select(p => p.BillingId).ToList();
            if (model.MsapCollectionId.HasValue)
            {
                ids.AddRange((await unitOfWork.Billing.GetAllAsync(b => b.CollectionId == model.MsapCollectionId, ct)).Select(b => b.MsapBillingId));
            }
            var locked = new Dictionary<int, Billing>();
            foreach (int id in ids.Distinct().Order())
            {
                locked[id] = await unitOfWork.Billing.GetForUpdateAsync(id, ct)
                    ?? throw new InvalidOperationException("Selected billing not found.");
            }
            var allocations = new Dictionary<int, decimal>();
            decimal totalEwt = 0;
            decimal totalWvat = 0;
            foreach (var payment in model.BillingPayments)
            {
                var billing = locked[payment.BillingId];
                if (billing.CustomerId != model.CustomerId)
                {
                    throw new InvalidOperationException("Selected billings must belong to the collection customer.");
                }
                if (billing.CollectionId.HasValue && billing.CollectionId != model.MsapCollectionId)
                {
                    throw new InvalidOperationException($"Billing #{billing.MsapBillingNumber} already belongs to a collection. Review or edit that receipt instead of creating another.");
                }
                if (billing.Status is not (MsapConstants.BillingStatus.ForCollection or MsapConstants.BillingStatus.Collected))
                {
                    throw new InvalidOperationException("Only posted billings can receive collection. Review and post all billings first.");
                }
                if (model.JobOrderId.HasValue && billing.JobOrderId != model.JobOrderId
                    && await unitOfWork.DispatchTicket.GetAsync(t => t.JobOrderId == model.JobOrderId && t.BillingId == billing.MsapBillingId, ct) == null)
                {
                    throw new InvalidOperationException("Selected billing does not belong to this Job Order.");
                }
                string? blocker = await JobProgressCalculator.GetBillingBlockerAsync(unitOfWork, billing, JobProgressCalculator.CollectionStage, ct);
                if (blocker != null)
                {
                    throw new InvalidOperationException(blocker);
                }
                decimal available = billing.Balance + (billing.CollectionId.HasValue ? billing.AmountPaid : 0);
                var due = GetSettlementAmounts(billing, customer, available);
                if (available <= 0 || payment.AmountToPay <= 0 || payment.AmountToPay > due.Net)
                {
                    throw new InvalidOperationException($"Payment for billing #{billing.MsapBillingNumber} must be positive and cannot exceed its remaining net balance ({due.Net:N2}).");
                }
                decimal proportion = payment.AmountToPay / due.Net;
                decimal ewt = Math.Round(due.Ewt * proportion, 2);
                decimal wvat = Math.Round(due.Wvat * proportion, 2);
                allocations[payment.BillingId] = payment.AmountToPay == due.Net ? available : payment.AmountToPay + ewt + wvat;
                totalEwt += ewt;
                totalWvat += wvat;
            }
            if (model.EWT != totalEwt || model.WVAT != totalWvat)
            {
                throw new InvalidOperationException("Withholding amounts do not match the selected billing payments. Reload the billings and review the withholding totals.");
            }
            return allocations;
        }

        public async Task<ServiceResult<int>> CreateCollectionAsync(CreateCollectionViewModel viewModel, string username, CancellationToken cancellationToken)
        {
            try
            {
                var guard = await GuardClosedPeriodAsync(viewModel.Date, cancellationToken);
                if (guard != null)
                {
                    return ServiceResult<int>.Failure(guard.Message!);
                }

                int collectionId = 0;
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    viewModel.MsapCollectionId = null;
                    var allocations = await GetSettlementAllocationsAsync(viewModel, cancellationToken);
                    var model = await MapToEntityAsync(viewModel, cancellationToken);
                    model.CreatedBy = username;
                    model.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    if (model.IsUndocumented)
                    {
                        model.MsapCollectionNumber = await unitOfWork.Collection.GenerateCollectionNumber(cancellationToken);
                    }
                    else
                    {
                        model.MsapCollectionNumber = viewModel.MsapCollectionNumber ?? string.Empty;
                    }

                    await unitOfWork.Collection.AddAsync(model, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);
                    collectionId = model.MsapCollectionId;

                    // Allocate payment
                    if (viewModel.BillingPayments != null)
                    {
                        model.PaidBills = [];
                        foreach (var payment in viewModel.BillingPayments)
                        {
                            var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == payment.BillingId, cancellationToken);
                            if (billing != null)
                            {
                                billing.CollectionId = model.MsapCollectionId;
                                billing.CollectionNumber = model.MsapCollectionNumber;
                                await unitOfWork.Collection.UpdateBillingPayment(payment.BillingId, allocations[payment.BillingId], cancellationToken);
                                model.PaidBills.Add(billing);
                            }
                        }
                    }

                    // Final save for all changes
                    await unitOfWork.SaveAsync(cancellationToken);

                    // Audit trail
                    var billIds = viewModel.BillingPayments?.Select(p => p.BillingId) ?? new List<int>();
                    var audit = new AuditTrail(username, $"Create collection #{model.MsapCollectionNumber} for billings #{string.Join(", #", billIds)}", "Collection", model.MsapCollectionId, model.MsapCollectionNumber);
                    await unitOfWork.AuditTrail.AddAsync(audit, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);

                }, cancellationToken);

                return ServiceResult<int>.Success(collectionId, "Collection created successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create collection");
                return ServiceResult<int>.Failure($"Failed to create collection: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> UpdateCollectionAsync(CreateCollectionViewModel viewModel, string username, CancellationToken cancellationToken)
        {
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var currentModel = viewModel.MsapCollectionId.HasValue
                        ? await unitOfWork.Collection.GetForUpdateAsync(viewModel.MsapCollectionId.Value, cancellationToken) : null;
                    if (currentModel == null)
                    {
                        throw new InvalidOperationException("Collection not found.");
                    }

                    var guard = await GuardClosedPeriodAsync(currentModel.Date, cancellationToken);
                    if (guard != null)
                    {
                        throw new InvalidOperationException(guard.Message);
                    }

                    if (currentModel.CustomerId != viewModel.CustomerId)
                    {
                        throw new InvalidOperationException("Customer cannot be changed on an existing collection.");
                    }

                    if (currentModel.IsPrinted || currentModel.DepositDate.HasValue || currentModel.ReturnedDate.HasValue || currentModel.ClearedDate.HasValue)
                    {
                        throw new InvalidOperationException("Cannot edit a collection after printing or recording a deposit.");
                    }

                    var allocations = await GetSettlementAllocationsAsync(viewModel, cancellationToken);

                    // Revert old allocations
                    var oldBillings = await unitOfWork.Billing.GetAllAsync(b => b.CollectionId == currentModel.MsapCollectionId, cancellationToken);
                    foreach (var billing in oldBillings)
                    {
                        billing.Status = MsapConstants.BillingStatus.ForCollection;
                        billing.CollectionId = null;
                        billing.CollectionNumber = null;
                        await unitOfWork.Collection.RemoveBillingPayment(billing.MsapBillingId, billing.AmountPaid, 0, cancellationToken);
                    }

                    if (viewModel.BillingPayments != null)
                    {
                        foreach (var payment in viewModel.BillingPayments)
                        {
                            var billing = await unitOfWork.Billing.GetAsync(b => b.MsapBillingId == payment.BillingId, cancellationToken);
                            if (billing != null)
                            {
                                billing.CollectionId = currentModel.MsapCollectionId;
                                billing.CollectionNumber = currentModel.MsapCollectionNumber;
                                await unitOfWork.Collection.UpdateBillingPayment(payment.BillingId, allocations[payment.BillingId], cancellationToken);
                            }
                        }
                    }

                    // Track changes for audit
                    var audit = new AuditTrail(username, $"Edit collection #{currentModel.MsapCollectionNumber}", "Collection", currentModel.MsapCollectionId, currentModel.MsapCollectionNumber);
                    await unitOfWork.AuditTrail.AddAsync(audit, cancellationToken);

                    // Update entity
                    currentModel.Date = viewModel.Date;
                    currentModel.CustomerId = viewModel.CustomerId;
                    currentModel.IsUndocumented = viewModel.IsUndocumented;
                    if (!currentModel.IsUndocumented)
                    {
                        currentModel.MsapCollectionNumber = viewModel.MsapCollectionNumber ?? currentModel.MsapCollectionNumber;
                    }
                    currentModel.ReferenceNo = viewModel.ReferenceNo;
                    currentModel.Remarks = viewModel.Remarks;
                    currentModel.CashAmount = viewModel.CashAmount;
                    currentModel.CheckAmount = viewModel.CheckAmount;
                    currentModel.CheckNumber = viewModel.CheckNumber;
                    currentModel.CheckDate = viewModel.CheckDate;
                    currentModel.CheckBank = viewModel.CheckBank;
                    currentModel.CheckBranch = viewModel.CheckBranch;
                    currentModel.Amount = viewModel.Amount;
                    currentModel.EWT = viewModel.EWT;
                    currentModel.WVAT = viewModel.WVAT;
                    currentModel.Total = viewModel.Amount + viewModel.EWT + viewModel.WVAT; // Total should be Gross (Cash + EWT + WVAT)

                    currentModel.EditedBy = username;
                    currentModel.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success("Collection modified successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to edit collection");
                return ServiceResult.Failure($"Failed to edit collection: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }


        public async Task<(IEnumerable<Collection> Data, int RecordsFiltered, int TotalRecords)> GetPagedCollectionsAsync(DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            return await unitOfWork.Collection.GetPagedCollectionsAsync(parameters, cancellationToken);
        }

        public async Task<CreateCollectionViewModel> PopulateCreateViewModelAsync(CancellationToken cancellationToken)
        {
            return new CreateCollectionViewModel
            {
                Customers = await unitOfWork.Collection.GetMsapCustomersWithCollectiblesSelectList(0, string.Empty, cancellationToken)
            };
        }

        public async Task<CreateCollectionViewModel?> PopulateEditViewModelAsync(int id, CancellationToken cancellationToken)
        {
            var model = await unitOfWork.Collection.GetAsync(c => c.MsapCollectionId == id, cancellationToken);
            if (model == null)
            {
                return null;
            }

            var viewModel = MapToViewModel(model);
            var billings = await unitOfWork.Billing.GetBillingsByCollectionIdAsync(id, cancellationToken);
            viewModel.ToCollectBillings = billings
                .Select(b => b.MsapBillingId.ToString())
                .ToList();

            viewModel.Customers = await unitOfWork.Collection.GetMsapCustomersWithCollectiblesSelectList(id, model.Customer.Type, cancellationToken);
            viewModel.Billings = await GetEditBillingsAsync(model.CustomerId, model.MsapCollectionId, cancellationToken);

            return viewModel;
        }

        public async Task<ServiceResult<object>> GetUncollectedBillingsForTableAsync(int customerId, int? collectionId, CancellationToken cancellationToken, int? jobOrderId = null)
        {
            try
            {
                var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == customerId, cancellationToken);
                if (customer == null)
                {
                    return ServiceResult<object>.Failure("Customer not found.");
                }

                var billings = await unitOfWork.Collection.GetMsapUncollectedBillingsByCustomerList(customerId, cancellationToken);
                if (collectionId.HasValue && collectionId.Value != 0)
                {
                    var alreadyCollected = await unitOfWork.Billing.GetAllAsync(b => b.CollectionId == collectionId.Value, cancellationToken);
                    billings.AddRange(alreadyCollected);
                }

                if (jobOrderId.HasValue)
                {
                    IEnumerable<DispatchTicket> tickets = await unitOfWork.DispatchTicket.GetAllAsync(t => t.JobOrderId == jobOrderId.Value && t.BillingId.HasValue, cancellationToken);
                    List<int> ids = tickets.Select(t => t.BillingId!.Value).ToList();
                    billings = billings.Where(b => b.JobOrderId == jobOrderId.Value || ids.Contains(b.MsapBillingId)).ToList();
                }
                var result = billings
                    .DistinctBy(b => b.MsapBillingId)
                    .Select(b =>
                    {
                        decimal available = b.Balance + (collectionId.HasValue && b.CollectionId == collectionId ? b.AmountPaid : 0);
                        var settlement = GetSettlementAmounts(b, customer, available);

                        return new
                        {
                            msapBillingId = b.MsapBillingId,
                            msapBillingNumber = b.MsapBillingNumber,
                            date = b.Date,
                            amount = available,
                            balance = b.Balance,
                            ewt = settlement.Ewt,
                            wvat = settlement.Wvat,
                            net = settlement.Net,
                            isVatable = b.IsVatable,
                            isSelected = collectionId.HasValue && b.CollectionId == collectionId.Value
                        };
                    });

                return ServiceResult<object>.Success(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get billings for table");
                return ServiceResult<object>.Failure($"Failed to get billings: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult<IEnumerable<Billing>>> GetSelectedBillingsAsync(List<string> billingIds, CancellationToken cancellationToken)
        {
            var ids = billingIds.Select(int.Parse).ToList();
            var billings = await unitOfWork.Billing.GetAllAsync(b => ids.Contains(b.MsapBillingId), cancellationToken);
            return ServiceResult<IEnumerable<Billing>>.Success(billings);
        }

        public async Task<bool> IsCustomerVatableAsync(int customerId, CancellationToken cancellationToken)
        {
            var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == customerId, cancellationToken);
            return customer?.VatType == MsapConstants.VatType_Vatable;
        }

        public async Task<ServiceResult<object>> GetBankAccountDetailsAsync(int bankId, CancellationToken cancellationToken)
        {
            var bank = await unitOfWork.BankAccount.GetAsync(b => b.BankAccountId == bankId, cancellationToken);
            if (bank == null)
            {
                return ServiceResult<object>.Failure("Bank not found.", ServiceResultStatus.NotFound);
            }

            return ServiceResult<object>.Success(new { bank = bank.Bank, accountNo = bank.AccountNo, accountName = bank.AccountName });
        }

        public async Task<List<SelectListItem>?> GetUncollectedBillingsSelectListAsync(int? customerId, CancellationToken cancellationToken)
        {
            return await unitOfWork.Collection.GetMsapUncollectedBillingsByCustomer(customerId, cancellationToken);
        }

        private async Task<Collection> MapToEntityAsync(CreateCollectionViewModel viewModel, CancellationToken cancellationToken)
        {
            var model = new Collection
            {
                MsapCollectionId = viewModel.MsapCollectionId ?? 0,
                IsUndocumented = viewModel.IsUndocumented,
                Date = viewModel.Date,
                CustomerId = viewModel.CustomerId,
                Amount = viewModel.Amount,
                EWT = viewModel.EWT,
                WVAT = viewModel.WVAT,
                Total = viewModel.Amount + viewModel.EWT + viewModel.WVAT, // Total should be Gross (Cash + EWT + WVAT)
                CashAmount = viewModel.CashAmount,
                CheckAmount = viewModel.CheckAmount,
                CheckNumber = viewModel.CheckNumber,
                CheckDate = viewModel.CheckDate,
                CheckBank = viewModel.CheckBank,
                CheckBranch = viewModel.CheckBranch,
                ReferenceNo = viewModel.ReferenceNo,
                Remarks = viewModel.Remarks,
                Customer = (await unitOfWork.Customer.GetAsync(c => c.CustomerId == viewModel.CustomerId, cancellationToken))!,
                Company = MsapConstants.Company_MMSI
            };

            return model;
        }

        private CreateCollectionViewModel MapToViewModel(Collection model)
        {
            return new CreateCollectionViewModel
            {
                MsapCollectionId = model.MsapCollectionId,
                MsapCollectionNumber = model.MsapCollectionNumber,
                IsUndocumented = model.IsUndocumented,
                Date = model.Date,
                CustomerId = model.CustomerId,
                Amount = model.Amount,
                EWT = model.EWT,
                WVAT = model.WVAT,
                CashAmount = model.CashAmount,
                CheckAmount = model.CheckAmount,
                CheckNumber = model.CheckNumber,
                CheckDate = model.CheckDate,
                CheckBank = model.CheckBank,
                CheckBranch = model.CheckBranch,
                ReferenceNo = model.ReferenceNo,
                Remarks = model.Remarks,
            };
        }

        public async Task<List<SelectListItem>> GetCustomerSelectListAsync(int? collectionId, int customerId, CancellationToken cancellationToken)
        {
            var cust = await unitOfWork.Customer.GetAsync(c => c.CustomerId == customerId, cancellationToken);
            return await unitOfWork.Collection.GetMsapCustomersWithCollectiblesSelectList(
                collectionId ?? 0,
                cust?.Type ?? string.Empty,
                cancellationToken);
        }

        private async Task<List<SelectListItem>?> GetEditBillingsAsync(int? customerId, int? collectionId, CancellationToken cancellationToken)
        {
            var list = await unitOfWork.Collection.GetMsapUncollectedBillingsByCustomer(customerId, cancellationToken);
            if (collectionId.HasValue && collectionId.Value != 0)
            {
                var model = await unitOfWork.Collection.GetAsync(c => c.MsapCollectionId == collectionId.Value, cancellationToken);
                if (model?.CustomerId == customerId)
                {
                    list?.AddRange(await unitOfWork.Collection.GetMsapCollectedBillsById(collectionId.Value, cancellationToken));
                }
            }
            return list;
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
