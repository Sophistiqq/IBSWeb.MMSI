using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using static IBS.Utility.MSAP.Constants.TaxConstants;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class CollectionService(
        IUnitOfWork unitOfWork,
        ILogger<CollectionService> logger,
        ICloudStorageService? cloudStorageService = null)
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
            int? bankId, string username, CancellationToken ct, string? reason = null, IFormFile? authorizationFile = null)
        {
            try
            {
                if (authorizationFile is { Length: > 0 })
                {
                    if (!await OtherTugChargeService.IsSupportedDocumentAsync(authorizationFile, ct))
                    {
                        return ServiceResult.Failure("Upload a valid PDF, PNG, or JPEG authorization document of up to 10 MB.");
                    }
                    if (cloudStorageService == null)
                    {
                        return ServiceResult.Failure("Storage service is not configured for authorization documents.");
                    }
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var collection = await unitOfWork.Collection.GetForUpdateAsync(id, ct)
                        ?? throw new InvalidOperationException("Collection not found.");
                    if (collection.IsVoided)
                    {
                        throw new InvalidOperationException("Cannot update banking on a voided collection.");
                    }
                    var isFinalized = collection.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
                    var isBankingDateClosed = await unitOfWork.PostedPeriod.IsMonthClosedAsync(date.Year, date.Month, ct);
                    var isAdjustedAction = isFinalized && isBankingDateClosed;
                    if (!isFinalized)
                    {
                        var guard = await GuardClosedPeriodAsync(date, ct);
                        if (guard != null)
                        {
                            throw new InvalidOperationException(guard.Message);
                        }
                    }
                    else if (isAdjustedAction)
                    {
                        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 500)
                        {
                            throw new InvalidOperationException("Please provide a valid reason for backdating banking into a closed period (5 to 500 characters).");
                        }
                        if (authorizationFile is not { Length: > 0 })
                        {
                            throw new InvalidOperationException("An approved authorization document (PDF, PNG, or JPEG) is required to record a closed-period banking date on a finalized collection.");
                        }
                    }
                    if (date == default)
                    {
                        throw new InvalidOperationException("Enter a valid transaction date.");
                    }
                    if (collection.ClearedDate.HasValue)
                    {
                        throw new InvalidOperationException("This collection has already cleared.");
                    }

                    string description;
                    var changes = new List<MsapFieldChange>();
                    switch (action)
                    {
                        case CollectionBankingAction.Deposit:
                            if (collection.DepositDate.HasValue)
                            {
                                throw new InvalidOperationException("This collection is already deposited.");
                            }
                            if (date < collection.ReturnedDate)
                            {
                                throw new InvalidOperationException("Redeposit date cannot precede the last return date.");
                            }
                            var bank = bankId.HasValue
                                ? await unitOfWork.BankAccount.GetAsync(b => b.BankAccountId == bankId.Value && b.Company == MsapConstants.Company_MMSI, ct) : null;
                            if (bank == null)
                            {
                                throw new InvalidOperationException("Select a valid MMSI bank account.");
                            }
                            if (collection.BankId != bank.BankAccountId)
                            {
                                changes.Add(new MsapFieldChange { Field = "Bank", Label = "Bank", Before = collection.BankAccountName ?? "-", After = bank.AccountName });
                            }
                            if (collection.DepositDate != date)
                            {
                                changes.Add(new MsapFieldChange { Field = "DepositDate", Label = "Deposit Date", Before = collection.DepositDate?.ToString("yyyy-MM-dd") ?? "-", After = date.ToString("yyyy-MM-dd") });
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
                            if (collection.ReturnedDate != date)
                            {
                                changes.Add(new MsapFieldChange { Field = "ReturnedDate", Label = "Returned Date", Before = collection.ReturnedDate?.ToString("yyyy-MM-dd") ?? "-", After = date.ToString("yyyy-MM-dd") });
                            }
                            if (collection.DepositDate != null)
                            {
                                changes.Add(new MsapFieldChange { Field = "DepositDate", Label = "Deposit Date", Before = collection.DepositDate.Value.ToString("yyyy-MM-dd"), After = "-" });
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
                            if (collection.ClearedDate != date)
                            {
                                changes.Add(new MsapFieldChange { Field = "ClearedDate", Label = "Cleared Date", Before = collection.ClearedDate?.ToString("yyyy-MM-dd") ?? "-", After = date.ToString("yyyy-MM-dd") });
                            }
                            collection.ClearedDate = date;
                            description = "Clear";
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported banking action.");
                    }

                    collection.EditedBy = username;
                    collection.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    if (isAdjustedAction)
                    {
                        string? uploadedAttachment = null;
                        if (authorizationFile is { Length: > 0 })
                        {
                            var extension = Path.GetExtension(authorizationFile.FileName).ToLowerInvariant();
                            uploadedAttachment = $"adjustment-auth-{Guid.NewGuid():N}{extension}";
                            await cloudStorageService!.UploadFileAsync(authorizationFile, uploadedAttachment);
                        }
                        if (collection.PeriodStatus == MsapPeriodStatus.Final)
                        {
                            collection.PeriodStatus = MsapPeriodStatus.Adjusted;
                            changes.Insert(0, new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted });
                        }
                        if (changes.Count > 0)
                        {
                            var log = new MsapAdjustmentLog
                            {
                                DocumentType = "Collection",
                                RecordId = collection.MsapCollectionId,
                                ReferenceNumber = collection.MsapCollectionNumber,
                                Reason = reason!.Trim(),
                                AttachmentUrl = uploadedAttachment,
                                ChangesJson = System.Text.Json.JsonSerializer.Serialize(changes),
                                EditedBy = username,
                                EditedDate = DateTimeHelper.GetCurrentPhilippineTime()
                            };
                            await unitOfWork.MsapAdjustmentLog.AddAsync(log, ct);
                        }
                    }
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

        public async Task<ServiceResult> RevertCollectionBankingStepAsync(int id, string reason, string username, CancellationToken ct, IFormFile? authorizationFile = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 500)
                {
                    return ServiceResult.Failure("Please provide a valid reason for reverting this banking step (5 to 500 characters).");
                }

                var existing = await unitOfWork.Collection.GetAsync(c => c.MsapCollectionId == id, ct);
                if (existing == null)
                {
                    return ServiceResult.Failure("Collection not found.");
                }
                if (existing.IsVoided)
                {
                    return ServiceResult.Failure("Cannot revert banking on a voided collection.");
                }

                if (existing.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted && authorizationFile is not { Length: > 0 })
                {
                    return ServiceResult.Failure("An approved authorization document (PDF, PNG, or JPEG) is required to revert a finalized-period collection.");
                }

                string? uploadedAttachment = null;
                if (authorizationFile is { Length: > 0 })
                {
                    if (!await OtherTugChargeService.IsSupportedDocumentAsync(authorizationFile, ct))
                    {
                        return ServiceResult.Failure("Upload a valid PDF, PNG, or JPEG authorization document of up to 10 MB.");
                    }
                    if (cloudStorageService == null)
                    {
                        return ServiceResult.Failure("Storage service is not configured for authorization documents.");
                    }
                    var extension = Path.GetExtension(authorizationFile.FileName).ToLowerInvariant();
                    uploadedAttachment = $"adjustment-auth-{Guid.NewGuid():N}{extension}";
                    await cloudStorageService.UploadFileAsync(authorizationFile, uploadedAttachment);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var collection = await unitOfWork.Collection.GetForUpdateAsync(id, ct)
                        ?? throw new InvalidOperationException("Collection not found.");
                    if (collection.IsVoided)
                    {
                        throw new InvalidOperationException("Cannot revert banking on a voided collection.");
                    }

                    if (collection.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted)
                    {
                        if (string.IsNullOrWhiteSpace(uploadedAttachment))
                        {
                            throw new InvalidOperationException("An approved authorization document (PDF, PNG, or JPEG) is required to revert a finalized-period collection.");
                        }
                    }
                    else
                    {
                        var guard = await GuardClosedPeriodAsync(collection.Date, ct);
                        if (guard != null)
                        {
                            throw new InvalidOperationException(guard.Message);
                        }
                    }

                    string stepDescription;
                    List<MsapFieldChange> changes = [];

                    if (collection.ClearedDate.HasValue)
                    {
                        var priorClearedDate = collection.ClearedDate.Value.ToString("yyyy-MM-dd");
                        stepDescription = $"Cleared ({priorClearedDate}) reverted to Deposited";
                        changes.Add(new MsapFieldChange { Field = "ClearedDate", Label = "Cleared Date", Before = priorClearedDate, After = "" });
                        collection.ClearedDate = null;
                    }
                    else if (collection.DepositDate.HasValue)
                    {
                        var priorDepositDate = collection.DepositDate.Value.ToString("yyyy-MM-dd");
                        var priorBankDetails = string.Join(" · ", new[] { collection.BankAccountNumber, collection.BankAccountName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        var priorBank = string.IsNullOrWhiteSpace(priorBankDetails) ? "-" : priorBankDetails;
                        stepDescription = $"Deposited ({priorDepositDate}, bank {priorBank}) reverted to {(collection.ReturnedDate.HasValue ? "Returned" : "Undeposited")}";
                        changes.Add(new MsapFieldChange { Field = "DepositDate", Label = "Deposit Date", Before = priorDepositDate, After = "" });
                        collection.DepositDate = null;
                        if (!collection.ReturnedDate.HasValue)
                        {
                            if (collection.BankId.HasValue || !string.IsNullOrWhiteSpace(collection.BankAccountName) || !string.IsNullOrWhiteSpace(collection.BankAccountNumber))
                            {
                                changes.Add(new MsapFieldChange { Field = "Bank", Label = "Bank", Before = priorBank, After = "-" });
                            }
                            collection.BankId = null;
                            collection.BankAccountName = null;
                            collection.BankAccountNumber = null;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Collection has no active deposit or clearing step to revert.");
                    }

                    collection.EditedBy = username;
                    collection.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    if (collection.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted)
                    {
                        if (collection.PeriodStatus == MsapPeriodStatus.Final)
                        {
                            collection.PeriodStatus = MsapPeriodStatus.Adjusted;
                            changes.Insert(0, new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted });
                        }

                        if (changes.Count > 0)
                        {
                            var log = new MsapAdjustmentLog
                            {
                                DocumentType = "Collection",
                                RecordId = collection.MsapCollectionId,
                                ReferenceNumber = collection.MsapCollectionNumber,
                                Reason = reason.Trim(),
                                AttachmentUrl = uploadedAttachment,
                                ChangesJson = System.Text.Json.JsonSerializer.Serialize(changes),
                                EditedBy = username,
                                EditedDate = DateTimeHelper.GetCurrentPhilippineTime()
                            };
                            await unitOfWork.MsapAdjustmentLog.AddAsync(log, ct);
                        }
                    }

                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                        $"Reverted banking step ({stepDescription}) for collection #{collection.MsapCollectionNumber}. Reason: {reason.Trim()}",
                        "Collection", collection.MsapCollectionId, collection.MsapCollectionNumber), ct);

                    await unitOfWork.SaveAsync(ct);
                }, ct);

                return ServiceResult.Success("Collection banking step reverted successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to revert collection banking step");
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        public async Task<ServiceResult> VoidCollectionAsync(int id, string reason, string username, CancellationToken ct, IFormFile? authorizationFile = null)
        {
            string? uploadedAttachment = null;
            try
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 500)
                {
                    return ServiceResult.Failure("Please provide a valid reason for voiding this collection (5 to 500 characters).");
                }

                var existing = await unitOfWork.Collection.GetAsync(c => c.MsapCollectionId == id, ct);
                if (existing == null)
                {
                    return ServiceResult.Failure("Collection not found.", ServiceResultStatus.NotFound);
                }
                if (existing.IsVoided)
                {
                    return ServiceResult.Failure("This collection is already voided.");
                }
                if (existing.DepositDate.HasValue || existing.ClearedDate.HasValue)
                {
                    return ServiceResult.Failure("Cannot void a deposited or cleared collection. Revert banking steps first.");
                }

                var isFinalized = existing.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
                if (isFinalized)
                {
                    if (authorizationFile is not { Length: > 0 })
                    {
                        return ServiceResult.Failure("An approved authorization document (PDF, PNG, or JPEG) is required to void a finalized-period collection.");
                    }
                }
                else
                {
                    var guard = await GuardClosedPeriodAsync(existing.Date, ct);
                    if (guard != null)
                    {
                        return guard;
                    }
                }

                if (authorizationFile is { Length: > 0 })
                {
                    if (!await OtherTugChargeService.IsSupportedDocumentAsync(authorizationFile, ct))
                    {
                        return ServiceResult.Failure("Upload a valid PDF, PNG, or JPEG authorization document of up to 10 MB.");
                    }
                    if (cloudStorageService == null)
                    {
                        return ServiceResult.Failure("Storage service is not configured for authorization documents.");
                    }
                    var extension = Path.GetExtension(authorizationFile.FileName).ToLowerInvariant();
                    uploadedAttachment = $"adjustment-auth-{Guid.NewGuid():N}{extension}";
                    await cloudStorageService.UploadFileAsync(authorizationFile, uploadedAttachment);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var collection = await unitOfWork.Collection.GetForUpdateAsync(id, ct)
                        ?? throw new InvalidOperationException("Collection not found.");
                    if (collection.IsVoided)
                    {
                        throw new InvalidOperationException("This collection is already voided.");
                    }
                    if (collection.DepositDate.HasValue || collection.ClearedDate.HasValue)
                    {
                        throw new InvalidOperationException("Cannot void a deposited or cleared collection. Revert banking steps first.");
                    }

                    var lockedFinalized = collection.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
                    if (lockedFinalized)
                    {
                        if (string.IsNullOrWhiteSpace(uploadedAttachment))
                        {
                            throw new InvalidOperationException("An approved authorization document (PDF, PNG, or JPEG) is required to void a finalized-period collection.");
                        }
                    }
                    else
                    {
                        var guard = await GuardClosedPeriodAsync(collection.Date, ct);
                        if (guard != null)
                        {
                            throw new InvalidOperationException(guard.Message);
                        }
                    }

                    var linkedBillingIds = (await unitOfWork.Billing.GetAllAsync(b => b.CollectionId == collection.MsapCollectionId, ct))
                        .Select(b => b.MsapBillingId)
                        .Order()
                        .ToList();
                    var releasedNumbers = new List<string>();
                    foreach (var billingId in linkedBillingIds)
                    {
                        var billing = await unitOfWork.Billing.GetForUpdateAsync(billingId, ct);
                        if (billing == null)
                        {
                            continue;
                        }
                        releasedNumbers.Add(billing.MsapBillingNumber);
                        billing.CollectionId = null;
                        billing.CollectionNumber = null;
                        billing.AmountPaid = 0m;
                        billing.Balance = billing.Amount;
                        billing.IsPaid = false;
                        billing.Status = MsapConstants.BillingStatus.ForCollection;
                    }

                    var now = DateTimeHelper.GetCurrentPhilippineTime();
                    var priorStatus = collection.PaymentStatus;
                    var trimmedReason = reason.Trim();
                    collection.VoidedBy = username;
                    collection.VoidedDate = now;
                    collection.CancellationRemarks = trimmedReason.Length > 255 ? trimmedReason[..255] : trimmedReason;
                    collection.EditedBy = username;
                    collection.EditedDate = now;

                    if (lockedFinalized)
                    {
                        var changes = new List<MsapFieldChange>();
                        if (collection.PeriodStatus == MsapPeriodStatus.Final)
                        {
                            collection.PeriodStatus = MsapPeriodStatus.Adjusted;
                            changes.Add(new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted });
                        }
                        changes.Add(new MsapFieldChange { Field = "Status", Label = "Status", Before = priorStatus, After = "Voided" });
                        changes.Add(new MsapFieldChange { Field = "Amount", Label = "Net Received", Before = collection.Amount.ToString("N2"), After = "0.00" });
                        if (releasedNumbers.Count > 0)
                        {
                            changes.Add(new MsapFieldChange { Field = "AllocatedBillings", Label = "Allocated Billings", Before = string.Join(", ", releasedNumbers), After = "Released to For Collection" });
                        }

                        var log = new MsapAdjustmentLog
                        {
                            DocumentType = "Collection",
                            RecordId = collection.MsapCollectionId,
                            ReferenceNumber = collection.MsapCollectionNumber,
                            Reason = trimmedReason,
                            AttachmentUrl = uploadedAttachment,
                            ChangesJson = System.Text.Json.JsonSerializer.Serialize(changes),
                            EditedBy = username,
                            EditedDate = now
                        };
                        await unitOfWork.MsapAdjustmentLog.AddAsync(log, ct);
                    }

                    var releasedSuffix = releasedNumbers.Count > 0 ? $" Released billings: {string.Join(", ", releasedNumbers)}." : string.Empty;
                    await unitOfWork.AuditTrail.AddAsync(new AuditTrail(username,
                        $"Voided collection #{collection.MsapCollectionNumber}.{releasedSuffix} Reason: {trimmedReason}",
                        "Collection", collection.MsapCollectionId, collection.MsapCollectionNumber), ct);

                    await unitOfWork.SaveAsync(ct);
                }, ct);

                uploadedAttachment = null;
                return ServiceResult.Success("Collection voided and linked billings released back to For Collection.");
            }
            catch (Exception ex)
            {
                if (uploadedAttachment != null && cloudStorageService != null)
                {
                    try { await cloudStorageService.DeleteFileAsync(uploadedAttachment); }
                    catch (Exception cleanupEx) { logger.LogWarning(cleanupEx, "Could not remove unused void authorization document {FileName}", uploadedAttachment); }
                }
                logger.LogError(ex, "Failed to void collection {CollectionId}", id);
                return ServiceResult.Failure(ExceptionHelper.GetErrorMessage(ex));
            }
        }

        public static Dictionary<int, (decimal Amount, decimal Ewt)> GetCollectedBillingAmounts(Collection collection, IEnumerable<Billing> billings)
        {
            // Use the receipt's recorded withholding, not the customer's current master-file settings.
            // shortcut: bills store gross paid only; persist per-bill tax if settled billing terms become editable.
            var settlements = billings.OrderBy(b => b.MsapBillingId).Select(b => new
            {
                b.MsapBillingId,
                Amounts = GetSettlementAmounts(b, collection.EWT > 0m, collection.WVAT > 0m, b.AmountPaid)
            }).ToList();
            var totalNet = settlements.Sum(b => b.Amounts.Net);
            var totalEwt = settlements.Sum(b => b.Amounts.Ewt);
            var cumulativeNet = 0m;
            var cumulativeEwt = 0m;
            var previousAmount = 0m;
            var previousEwt = 0m;
            var result = new Dictionary<int, (decimal Amount, decimal Ewt)>();
            foreach (var settlement in settlements)
            {
                cumulativeNet += totalNet > 0m ? settlement.Amounts.Net : 1m;
                cumulativeEwt += totalEwt > 0m ? settlement.Amounts.Ewt : 1m;
                var amount = Math.Round((collection.Amount * cumulativeNet) / (totalNet > 0m ? totalNet : settlements.Count), 2, MidpointRounding.AwayFromZero);
                var ewt = Math.Round((collection.EWT * cumulativeEwt) / (totalEwt > 0m ? totalEwt : settlements.Count), 2, MidpointRounding.AwayFromZero);
                result[settlement.MsapBillingId] = (amount - previousAmount, ewt - previousEwt);
                previousAmount = amount;
                previousEwt = ewt;
            }
            return result;
        }

        private static (decimal Ewt, decimal Wvat, decimal Net) GetSettlementAmounts(Billing billing, bool withHoldingTax, bool withHoldingVat, decimal gross)
        {
            var ewt = withHoldingTax && billing.BilledTo == MsapConstants.BilledToLocal
                ? Math.Round(billing.IsVatable ? (gross / VatMultiplier) * EwtRate : gross * EwtRate, 2) : 0;
            var wvat = withHoldingVat && billing.BilledTo == MsapConstants.BilledToLocal && billing.IsVatable
                ? Math.Round((gross / VatMultiplier) * WvatRate, 2) : 0;
            return (ewt, wvat, Math.Round((gross - ewt) - wvat, 2));
        }

        private async Task<Dictionary<int, decimal>> GetSettlementAllocationsAsync(CreateCollectionViewModel model, CancellationToken ct)
        {
            if (!model.IsUndocumented && string.IsNullOrWhiteSpace(model.MsapCollectionNumber))
            {
                throw new InvalidOperationException("Enter the collection / receipt number or mark it undocumented.");
            }
            if (model.Date == default || (model.CheckAmount > 0 &&
                (!model.CheckDate.HasValue || string.IsNullOrWhiteSpace(model.CheckNumber))))
            {
                throw new InvalidOperationException("Enter a collection date and, for checks, the check date and check number.");
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
            foreach (var id in ids.Distinct().Order())
            {
                locked[id] = await unitOfWork.Billing.GetForUpdateAsync(id, ct)
                    ?? throw new InvalidOperationException("Selected billing not found.");
            }
            var paymentBillings = model.BillingPayments.Select(p => locked[p.BillingId]).ToList();
            var blockersByBillingId = await GetBillingBlockersAsync(paymentBillings, ct);
            var allocations = new Dictionary<int, decimal>();
            var totalEwt = 0m;
            var totalWvat = 0m;
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
                if (!BillingService.CanPrint(billing))
                {
                    throw new InvalidOperationException("Only bills with current final approval can receive collection.");
                }
                var blocker = blockersByBillingId.GetValueOrDefault(billing.MsapBillingId);
                if (blocker != null)
                {
                    throw new InvalidOperationException(blocker);
                }
                var available = billing.Balance + (billing.CollectionId.HasValue ? billing.AmountPaid : 0);
                var due = GetSettlementAmounts(billing, customer.WithHoldingTax, customer.WithHoldingVat, available);
                if (available <= 0 || payment.AmountToPay <= 0 || payment.AmountToPay != due.Net)
                {
                    throw new InvalidOperationException($"Billing #{billing.MsapBillingNumber} must be paid in full at its remaining net balance ({due.Net:N2}). Partial payments are not supported.");
                }
                allocations[payment.BillingId] = available;
                totalEwt += due.Ewt;
                totalWvat += due.Wvat;
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
                        var billingIds = viewModel.BillingPayments.Select(p => p.BillingId).Distinct().ToList();
                        var billings = (await unitOfWork.Billing.GetAllAsync(b => billingIds.Contains(b.MsapBillingId), cancellationToken))
                            .ToDictionary(b => b.MsapBillingId);

                        foreach (var payment in viewModel.BillingPayments)
                        {
                            if (billings.TryGetValue(payment.BillingId, out var billing))
                            {
                                billing.CollectionId = model.MsapCollectionId;
                                billing.CollectionNumber = model.MsapCollectionNumber;
                                billing.AmountPaid += allocations[payment.BillingId];
                                billing.Balance = billing.Amount - billing.AmountPaid;
                                if (billing.Balance <= 0)
                                {
                                    billing.IsPaid = true;
                                    billing.Status = MsapConstants.BillingStatus.Collected;
                                }
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
            string? uploadedAttachment = null;
            try
            {
                var previewModel = viewModel.MsapCollectionId.HasValue
                    ? await unitOfWork.Collection.GetAsync(c => c.MsapCollectionId == viewModel.MsapCollectionId.Value, cancellationToken)
                    : null;
                if (previewModel == null)
                {
                    return ServiceResult.Failure("Collection not found.", ServiceResultStatus.NotFound);
                }
                if (previewModel.IsVoided)
                {
                    return ServiceResult.Failure("Cannot edit a voided collection.");
                }

                var isFinalOrAdjustedPreview = previewModel.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
                if (isFinalOrAdjustedPreview)
                {
                    if (string.IsNullOrWhiteSpace(viewModel.AdjustmentReason) || viewModel.AdjustmentReason.Trim().Length is < 5 or > 500)
                    {
                        return ServiceResult.Failure("Please provide a valid reason for editing a finalized-period collection (5 to 500 characters).");
                    }
                    if (viewModel.AuthorizationFile is not { Length: > 0 })
                    {
                        return ServiceResult.Failure("An approved authorization document (PDF, PNG, or JPEG) is required to edit a finalized-period collection.");
                    }
                    if (!await OtherTugChargeService.IsSupportedDocumentAsync(viewModel.AuthorizationFile, cancellationToken))
                    {
                        return ServiceResult.Failure("Upload a valid PDF, PNG, or JPEG authorization document of up to 10 MB.");
                    }
                    if (cloudStorageService == null)
                    {
                        return ServiceResult.Failure("Storage service is not configured for authorization documents.");
                    }
                    var extension = Path.GetExtension(viewModel.AuthorizationFile.FileName).ToLowerInvariant();
                    uploadedAttachment = $"adjustment-auth-{Guid.NewGuid():N}{extension}";
                    await cloudStorageService.UploadFileAsync(viewModel.AuthorizationFile, uploadedAttachment);
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var currentModel = viewModel.MsapCollectionId.HasValue
                        ? await unitOfWork.Collection.GetForUpdateAsync(viewModel.MsapCollectionId.Value, cancellationToken) : null;
                    if (currentModel == null)
                    {
                        throw new InvalidOperationException("Collection not found.");
                    }
                    if (currentModel.IsVoided)
                    {
                        throw new InvalidOperationException("Cannot edit a voided collection.");
                    }

                    var isFinalOrAdjusted = currentModel.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted;
                    if (!isFinalOrAdjusted)
                    {
                        var guard = await GuardClosedPeriodAsync(currentModel.Date, cancellationToken)
                            ?? await GuardClosedPeriodAsync(viewModel.Date, cancellationToken);
                        if (guard != null)
                        {
                            throw new InvalidOperationException(guard.Message);
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(viewModel.AdjustmentReason) || viewModel.AdjustmentReason.Trim().Length is < 5 or > 500)
                        {
                            throw new InvalidOperationException("Please provide a valid reason for editing a finalized-period collection (5 to 500 characters).");
                        }
                        if (string.IsNullOrWhiteSpace(uploadedAttachment))
                        {
                            throw new InvalidOperationException("An approved authorization document (PDF, PNG, or JPEG) is required to edit a finalized-period collection.");
                        }
                        if (viewModel.Date != currentModel.Date)
                        {
                            var newDateGuard = await GuardClosedPeriodAsync(viewModel.Date, cancellationToken);
                            if (newDateGuard != null)
                            {
                                throw new InvalidOperationException(newDateGuard.Message);
                            }
                        }
                    }

                    if (currentModel.CustomerId != viewModel.CustomerId)
                    {
                        throw new InvalidOperationException("Customer cannot be changed on an existing collection.");
                    }

                    if (currentModel.DepositDate.HasValue || currentModel.ClearedDate.HasValue)
                    {
                        throw new InvalidOperationException("Cannot edit a collection after recording a deposit or clearing. Revert the banking step first.");
                    }
                    if (currentModel.ReturnedDate.HasValue)
                    {
                        throw new InvalidOperationException("Cannot edit a collection with a returned check. Record a redeposit or void the receipt instead.");
                    }

                    var allocations = await GetSettlementAllocationsAsync(viewModel, cancellationToken);
                    var oldBillings = (await unitOfWork.Billing.GetAllAsync(b => b.CollectionId == currentModel.MsapCollectionId, cancellationToken)).ToList();
                    var oldBillingNumbers = string.Join(", ", oldBillings.Select(b => b.MsapBillingNumber).Order());
                    var newBillingIds = viewModel.BillingPayments?.Select(p => p.BillingId).Distinct().ToList() ?? [];
                    var newBillingsMap = (await unitOfWork.Billing.GetAllAsync(b => newBillingIds.Contains(b.MsapBillingId), cancellationToken))
                        .ToDictionary(b => b.MsapBillingId);
                    var newBillingNumbers = string.Join(", ", newBillingsMap.Values.Select(b => b.MsapBillingNumber).Order());

                    var oldCollectionNumber = currentModel.MsapCollectionNumber;
                    var newCollectionNumber = !viewModel.IsUndocumented
                        ? viewModel.MsapCollectionNumber ?? oldCollectionNumber
                        : oldCollectionNumber;
                    currentModel.MsapCollectionNumber = newCollectionNumber;

                    if (isFinalOrAdjusted)
                    {
                        var changes = new List<MsapFieldChange>();
                        if (currentModel.PeriodStatus == MsapPeriodStatus.Final)
                        {
                            changes.Add(new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted });
                        }
                        if (oldCollectionNumber != newCollectionNumber)
                        {
                            changes.Add(new MsapFieldChange { Field = "CollectionNumber", Label = "Collection Number", Before = oldCollectionNumber, After = newCollectionNumber });
                        }
                        if (currentModel.Date != viewModel.Date)
                        {
                            changes.Add(new MsapFieldChange { Field = "Date", Label = "Collection Date", Before = currentModel.Date.ToString("yyyy-MM-dd"), After = viewModel.Date.ToString("yyyy-MM-dd") });
                        }
                        if (currentModel.Amount != viewModel.Amount)
                        {
                            changes.Add(new MsapFieldChange { Field = "Amount", Label = "Amount", Before = currentModel.Amount.ToString("N2"), After = viewModel.Amount.ToString("N2") });
                        }
                        if (currentModel.EWT != viewModel.EWT)
                        {
                            changes.Add(new MsapFieldChange { Field = "EWT", Label = "EWT", Before = currentModel.EWT.ToString("N2"), After = viewModel.EWT.ToString("N2") });
                        }
                        if (currentModel.WVAT != viewModel.WVAT)
                        {
                            changes.Add(new MsapFieldChange { Field = "WVAT", Label = "WVAT", Before = currentModel.WVAT.ToString("N2"), After = viewModel.WVAT.ToString("N2") });
                        }
                        if ((currentModel.CheckNumber ?? string.Empty) != (viewModel.CheckNumber ?? string.Empty))
                        {
                            changes.Add(new MsapFieldChange { Field = "CheckNumber", Label = "Check Number", Before = currentModel.CheckNumber ?? "-", After = viewModel.CheckNumber ?? "-" });
                        }
                        if (!string.Equals(oldBillingNumbers, newBillingNumbers, StringComparison.Ordinal))
                        {
                            changes.Add(new MsapFieldChange { Field = "AllocatedBillings", Label = "Allocated Billings", Before = oldBillingNumbers, After = newBillingNumbers });
                        }

                        currentModel.PeriodStatus = MsapPeriodStatus.Adjusted;
                        if (changes.Count > 0)
                        {
                            var log = new MsapAdjustmentLog
                            {
                                DocumentType = "Collection",
                                RecordId = currentModel.MsapCollectionId,
                                ReferenceNumber = currentModel.MsapCollectionNumber,
                                Reason = viewModel.AdjustmentReason!.Trim(),
                                AttachmentUrl = uploadedAttachment,
                                ChangesJson = System.Text.Json.JsonSerializer.Serialize(changes),
                                EditedBy = username,
                                EditedDate = DateTimeHelper.GetCurrentPhilippineTime()
                            };
                            await unitOfWork.MsapAdjustmentLog.AddAsync(log, cancellationToken);
                        }
                    }

                    // Revert old allocations
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
                            if (newBillingsMap.TryGetValue(payment.BillingId, out var billing))
                            {
                                billing.CollectionId = currentModel.MsapCollectionId;
                                billing.CollectionNumber = newCollectionNumber;
                                billing.AmountPaid += allocations[payment.BillingId];
                                billing.Balance = billing.Amount - billing.AmountPaid;
                                if (billing.Balance <= 0)
                                {
                                    billing.IsPaid = true;
                                    billing.Status = MsapConstants.BillingStatus.Collected;
                                }
                            }
                        }
                    }

                    // Track changes for audit
                    var activity = oldCollectionNumber == newCollectionNumber
                        ? $"Edit collection #{newCollectionNumber}"
                        : $"Edit collection #{oldCollectionNumber} to #{newCollectionNumber}";
                    if (isFinalOrAdjusted && !string.IsNullOrWhiteSpace(viewModel.AdjustmentReason))
                    {
                        activity += $". Reason: {viewModel.AdjustmentReason.Trim()}";
                    }
                    var audit = new AuditTrail(username, activity, "Collection", currentModel.MsapCollectionId, newCollectionNumber);
                    await unitOfWork.AuditTrail.AddAsync(audit, cancellationToken);

                    // Update entity
                    currentModel.Date = viewModel.Date;
                    currentModel.CustomerId = viewModel.CustomerId;
                    currentModel.IsUndocumented = viewModel.IsUndocumented;
                    currentModel.ReferenceNo = viewModel.ReferenceNo?.Trim();
                    currentModel.Remarks = viewModel.Remarks?.Trim();
                    currentModel.CashAmount = viewModel.CashAmount;
                    currentModel.CheckAmount = viewModel.CheckAmount;
                    if (viewModel.CheckAmount > 0)
                    {
                        currentModel.CheckNumber = viewModel.CheckNumber?.Trim();
                        currentModel.CheckDate = viewModel.CheckDate;
                        if (!string.IsNullOrWhiteSpace(viewModel.CheckBank))
                        {
                            currentModel.CheckBank = viewModel.CheckBank.Trim();
                        }
                        if (!string.IsNullOrWhiteSpace(viewModel.CheckBranch))
                        {
                            currentModel.CheckBranch = viewModel.CheckBranch.Trim();
                        }
                    }
                    else
                    {
                        currentModel.CheckNumber = null;
                        currentModel.CheckDate = null;
                        currentModel.CheckBank = null;
                        currentModel.CheckBranch = null;
                    }
                    currentModel.Amount = viewModel.Amount;
                    currentModel.EWT = viewModel.EWT;
                    currentModel.WVAT = viewModel.WVAT;
                    currentModel.Total = viewModel.Amount + viewModel.EWT + viewModel.WVAT; // Total should be Gross (Cash + EWT + WVAT)

                    currentModel.EditedBy = username;
                    currentModel.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                uploadedAttachment = null;
                return ServiceResult.Success("Collection modified successfully");
            }
            catch (Exception ex)
            {
                if (uploadedAttachment != null && cloudStorageService != null)
                {
                    try { await cloudStorageService.DeleteFileAsync(uploadedAttachment); }
                    catch (Exception cleanupEx) { logger.LogWarning(cleanupEx, "Could not remove unused edit authorization document {FileName}", uploadedAttachment); }
                }
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

        public async Task<ServiceResult<object>> GetUncollectedBillingsForTableAsync(int customerId, int? collectionId, CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            try
            {
                var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == customerId, cancellationToken);
                if (customer == null)
                {
                    return ServiceResult<object>.Failure("Customer not found.");
                }

                var billings = (await unitOfWork.Billing.GetAllAsync(b => b.CustomerId == customerId
                    && (b.Balance > 0 || (collectionId.HasValue && b.CollectionId == collectionId)), cancellationToken)).ToList();
                var principals = (await unitOfWork.Principal.GetAllAsync(p => p.CustomerId == customerId, cancellationToken))
                    .ToDictionary(p => p.PrincipalId, p => p.PrincipalName);
                var jobIds = billings.Select(b => b.CustomerOrderSlipId ?? b.BasicBilling?.CustomerOrderSlipId)
                    .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
                var jobs = (await unitOfWork.CustomerOrderSlip.GetAllAsync(j => jobIds.Contains(j.CustomerOrderSlipId), cancellationToken))
                    .ToDictionary(j => j.CustomerOrderSlipId, j => j.CustomerOrderSlipNumber);
                var candidateBillings = billings
                    .Where(b => BillingService.CanPrint(b) && !(b.CollectionId.HasValue && (!collectionId.HasValue || b.CollectionId != collectionId)))
                    .ToList();
                var blockersByBillingId = await GetBillingBlockersAsync(candidateBillings, cancellationToken);
                var result = new List<object>();
                foreach (var b in billings.OrderBy(b => b.MsapBillingNumber))
                {
                    var selected = collectionId.HasValue && b.CollectionId == collectionId;
                    var available = b.Balance + (selected ? b.AmountPaid : 0);
                    var settlement = GetSettlementAmounts(b, customer.WithHoldingTax, customer.WithHoldingVat, available);
                    var blocker = !BillingService.CanPrint(b) ? "Awaiting current final approval"
                        : b.CollectionId.HasValue && !selected ? "Already allocated; review the existing receipt"
                        : blockersByBillingId.GetValueOrDefault(b.MsapBillingId);
                    result.Add(new
                    {
                        msapBillingId = b.MsapBillingId,
                        msapBillingNumber = b.MsapBillingNumber,
                        customerOrderSlipId = b.CustomerOrderSlipId ?? b.BasicBilling?.CustomerOrderSlipId,
                        cosNumber = jobs.GetValueOrDefault(b.CustomerOrderSlipId ?? b.BasicBilling?.CustomerOrderSlipId ?? 0),
                        component = b.BasicBillingId.HasValue ? "BAF" : b.BafBilling != null ? "Basic" : "Combined",
                        billedParty = b.PrincipalId.HasValue ? principals.GetValueOrDefault(b.PrincipalId.Value, "Principal") : "Customer",
                        date = b.Date,
                        isVatable = b.IsVatable,
                        amount = available,
                        balance = b.Balance,
                        ewt = settlement.Ewt,
                        wvat = settlement.Wvat,
                        net = settlement.Net,
                        isSelected = selected,
                        paymentAmount = selected ? GetSettlementAmounts(b, customer.WithHoldingTax, customer.WithHoldingVat, b.AmountPaid).Net : settlement.Net,
                        eligible = blocker == null && available > 0 && settlement.Net > 0,
                        reason = blocker
                    });
                }

                return ServiceResult<object>.Success(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get billings for table");
                return ServiceResult<object>.Failure($"Failed to get billings: {ExceptionHelper.GetErrorMessage(ex)}");
            }
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
                CheckNumber = viewModel.CheckAmount > 0 ? viewModel.CheckNumber?.Trim() : null,
                CheckDate = viewModel.CheckAmount > 0 ? viewModel.CheckDate : null,
                CheckBank = viewModel.CheckAmount > 0 ? viewModel.CheckBank?.Trim() : null,
                CheckBranch = viewModel.CheckAmount > 0 ? viewModel.CheckBranch?.Trim() : null,
                ReferenceNo = viewModel.ReferenceNo?.Trim(),
                Remarks = viewModel.Remarks?.Trim(),
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
                PeriodStatus = model.PeriodStatus,
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

        private async Task<Dictionary<int, string?>> GetBillingBlockersAsync(IReadOnlyCollection<Billing> billings, CancellationToken ct)
        {
            var blockers = new Dictionary<int, string?>();
            if (billings.Count == 0)
            {
                return blockers;
            }

            var billingIds = billings.Select(b => b.MsapBillingId).Distinct().ToList();
            var ticketsByBillingId = (await unitOfWork.DispatchTicket.GetAllAsync(
                t => t.BillingId.HasValue && billingIds.Contains(t.BillingId.Value), ct))
                .GroupBy(t => t.BillingId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(t => t.CustomerOrderSlipId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList());

            var allJobIds = billings
                .Select(b => b.CustomerOrderSlipId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Concat(ticketsByBillingId.Values.SelectMany(ids => ids))
                .Distinct()
                .ToList();

            if (allJobIds.Count == 0)
            {
                foreach (var billing in billings)
                {
                    blockers[billing.MsapBillingId] = null;
                }
                return blockers;
            }

            var progressByJobId = (await JobProgressCalculator.LoadForJobsAsync(unitOfWork, allJobIds, ct))
                .Where(p => p.CustomerOrderSlipId.HasValue)
                .ToDictionary(p => p.CustomerOrderSlipId!.Value);

            foreach (var billing in billings)
            {
                var serviceStage = billing.BasicBillingId.HasValue || billing.BafBilling != null
                    ? JobProgressCalculator.BillingStage
                    : JobProgressCalculator.CollectionStage;
                var billingJobIds = (ticketsByBillingId.GetValueOrDefault(billing.MsapBillingId) ?? [])
                    .Concat(billing.CustomerOrderSlipId.HasValue ? [billing.CustomerOrderSlipId.Value] : [])
                    .Distinct();
                string? blocker = null;
                foreach (var jobId in billingJobIds)
                {
                    if (!progressByJobId.TryGetValue(jobId, out var progress))
                    {
                        blocker = "Linked COS not found.";
                        break;
                    }
                    if (progress.Stopped || progress.Stage < serviceStage)
                    {
                        blocker = progress.Guidance;
                        break;
                    }
                }
                blockers[billing.MsapBillingId] = blocker;
            }

            return blockers;
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
