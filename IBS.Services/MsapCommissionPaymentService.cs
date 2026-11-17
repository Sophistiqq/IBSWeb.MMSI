using System.Text.Json;
using IBS.DataAccess.Data;
using IBS.DataAccess.Repository.Filpride;
using IBS.DataAccess.Repository.IRepository;
using IBS.Models.Enums;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.Books;
using IBS.Models.Filpride.ViewModels;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.Helpers;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IBS.Services
{
    public class MsapCommissionPaymentService
    {
        public const string ApAccount = MsapConstants.CommissionPayableAccount;
        public const string CashAccount = "101010100";

        private readonly ApplicationDbContext _db;
        private readonly IUnitOfWork _work;
        private readonly MsapCommissionPaymentRepository _sources;
        private readonly CheckVoucherDocumentationService _documentation;
        private readonly ICloudStorageService _storage;
        private readonly ILogger<MsapCommissionPaymentService> _logger;

        public MsapCommissionPaymentService(
            ApplicationDbContext db,
            IUnitOfWork work,
            CheckVoucherDocumentationService documentation,
            ICloudStorageService storage,
            ILogger<MsapCommissionPaymentService> logger)
        {
            _db = db;
            _work = work;
            _sources = new MsapCommissionPaymentRepository(db);
            _documentation = documentation;
            _storage = storage;
            _logger = logger;
        }

        private void RequireCompanyWorkspace()
        {
            if (_db.WorkspaceTugboatId.HasValue)
            {
                throw new InvalidOperationException("Select MMSI / All tugboats for company-level MSAP commission payments.");
            }
        }

        public async Task<bool> IsMsapCommissionVoucherAsync(int cvHeaderId, CancellationToken ct)
        {
            return await _db.FilprideMsapCommissionPayments
                .IgnoreQueryFilters(["TugboatWorkspace"])
                .AnyAsync(p => p.CheckVoucherHeaderId == cvHeaderId, ct);
        }

        public async Task<List<SelectListItem>> GetRepresentativesSelectListAsync(CancellationToken ct)
        {
            var reps = await _sources.RepresentativesAsync(ct);
            return reps
                .Where(r => r.IsActive)
                .Select(r => new SelectListItem
                {
                    Value = r.RepresentativeId.ToString(),
                    Text = $"{r.RepresentativeName} ({GetCategoryLabel(r.Category)})"
                })
                .ToList();
        }

        public async Task<MsapRepresentativeCommissionResponse> GetRepresentativeCommissionsAsync(
            int representativeId,
            int? cvId,
            CancellationToken ct)
        {
            RequireCompanyWorkspace();
            var rep = await _sources.RepresentativeAsync(representativeId, ct)
                ?? throw new InvalidOperationException("MSAP representative not found.");

            var profileError = PaymentProfileHelper.Validate(
                rep,
                rep.Address,
                rep.Tin,
                rep.VatType,
                rep.TaxType,
                rep.WithholdingTaxPercent,
                required: true,
                "representative");

            string? withholdingAccountNo = null;
            string? withholdingAccountTitle = null;
            if (profileError == null && rep.TaxType == MsapConstants.TaxType_WithTax && rep.WithholdingTaxPercent is > 0m)
            {
                withholdingAccountNo = WithholdingTaxHelper.GetAccountNumberByPercent(rep.WithholdingTaxPercent.Value);
                if (!string.IsNullOrEmpty(withholdingAccountNo))
                {
                    withholdingAccountTitle = await _db.FilprideChartOfAccounts
                        .AsNoTracking()
                        .Where(a => a.AccountNumber == withholdingAccountNo && !a.IsMain && !a.IsHidden)
                        .Select(a => a.AccountName)
                        .FirstOrDefaultAsync(ct) ?? "Expanded Withholding Tax";
                }
            }

            var reserved = (await _sources.ReservedItemsAsync(cvId, ct))
                .Select(x => (x.BillingId, x.Category))
                .ToHashSet();

            var selectedKeys = cvId.HasValue
                ? (await _db.FilprideMsapCommissionPayments
                    .AsNoTracking()
                    .Where(p => p.CheckVoucherHeaderId == cvId.Value && p.ReleasedAt == null)
                    .Select(p => new { p.MsapBillingId, p.Category })
                    .ToListAsync(ct))
                    .Select(x => (x.MsapBillingId, x.Category))
                    .ToHashSet()
                : [];

            var billings = await _sources.ApprovedBillingsForRepresentativeAsync(representativeId, ct);
            var items = new List<MsapCommissionBillableItemDto>();

            foreach (var billing in billings)
            {
                if (billing.AgencyIncentiveRepresentativeId == representativeId && billing.AgencyIncentiveAmount > 0m)
                {
                    var key = (billing.MsapBillingId, MsapConstants.RepresentativeCategory.AgencyIncentive);
                    if (!reserved.Contains(key))
                    {
                        items.Add(BuildItemDto(billing, MsapConstants.RepresentativeCategory.AgencyIncentive, billing.AgencyIncentiveAmount, rep, profileError, selectedKeys.Contains(key)));
                    }
                }

                if (billing.AgentCommissionRepresentativeId == representativeId && billing.AgentCommissionAmount > 0m)
                {
                    var key = (billing.MsapBillingId, MsapConstants.RepresentativeCategory.AgentCommission);
                    if (!reserved.Contains(key))
                    {
                        items.Add(BuildItemDto(billing, MsapConstants.RepresentativeCategory.AgentCommission, billing.AgentCommissionAmount, rep, profileError, selectedKeys.Contains(key)));
                    }
                }
            }

            return new MsapRepresentativeCommissionResponse
            {
                RepresentativeId = rep.RepresentativeId,
                RepresentativeName = rep.RepresentativeName,
                Address = rep.Address,
                Tin = rep.Tin,
                VatType = rep.VatType,
                TaxType = rep.TaxType,
                WithholdingTaxPercent = rep.WithholdingTaxPercent ?? 0m,
                ProfileError = profileError,
                WithholdingAccountNumber = withholdingAccountNo,
                WithholdingAccountTitle = withholdingAccountTitle,
                Items = items
            };
        }

        public async Task<int> CreateAsync(CommissionPaymentViewModel form, IFormFile? file, string actor, CancellationToken ct)
        {
            RequireCompanyWorkspace();
            if (string.IsNullOrWhiteSpace(actor))
            {
                throw new InvalidOperationException("An authenticated preparer is required.");
            }
            if (!form.RepresentativeId.HasValue || form.RepresentativeId.Value <= 0)
            {
                throw new InvalidOperationException("Select an MSAP representative.");
            }

            var requestedItems = NormalizeItems(form.MsapItems);
            string? uploaded = null;
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                await GuardPeriodAsync(form.TransactionDate, ct);
                if (!form.CheckNo.Contains("DM", StringComparison.OrdinalIgnoreCase))
                {
                    var duplicateCheck = await _db.FilprideCheckVoucherHeaders
                        .AnyAsync(cv => cv.CanceledBy == null
                            && cv.VoidedBy == null
                            && cv.CheckNo == form.CheckNo.Trim()
                            && cv.BankId == form.BankId, ct);
                    if (duplicateCheck)
                    {
                        throw new InvalidOperationException("Check No. already exists for this bank account.");
                    }
                }

                await _db.FilprideBankAccounts
                    .FromSqlInterpolated($"SELECT * FROM filpride_bank_accounts WHERE bank_account_id={form.BankId} FOR UPDATE")
                    .LoadAsync(ct);

                var bank = await _db.FilprideBankAccounts
                    .SingleOrDefaultAsync(b => b.BankAccountId == form.BankId && b.IsActive, ct)
                    ?? throw new InvalidOperationException("Select an active bank account.");

                var billingIds = requestedItems.Select(x => x.BillingId).Distinct().ToArray();
                var (rep, billings) = await _sources.SourceForUpdateAsync(form.RepresentativeId.Value, billingIds, ct);

                var profileError = PaymentProfileHelper.Validate(
                    rep, rep.Address, rep.Tin, rep.VatType, rep.TaxType, rep.WithholdingTaxPercent, required: true, "representative");
                if (profileError != null)
                {
                    throw new InvalidOperationException(profileError);
                }

                var reserved = (await _sources.ReservedItemsAsync(null, ct)).ToHashSet();
                var payments = BuildPaymentAllocations(requestedItems, billings, rep, reserved);

                var totalGross = payments.Sum(p => p.GrossAmount);
                var totalWithholding = payments.Sum(p => p.WithholdingAmount);
                var manualEntries = ExtractManualEntries(form);
                var manualDebits = manualEntries.Sum(e => e.Debit);
                var manualCredits = manualEntries.Sum(e => e.Credit);
                var cashInBank = totalGross - totalWithholding + manualDebits - manualCredits;

                if (totalGross <= 0m || totalWithholding >= totalGross || cashInBank <= 0m)
                {
                    throw new InvalidOperationException("The selected MSAP commission items and entries do not produce a valid positive Cash in Bank amount.");
                }

                await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20771010)", ct);
                var cvNo = await _work.FilprideCheckVoucher.GenerateCodeAsync(form.Type!, ct);

                var header = new FilprideCheckVoucherHeader
                {
                    CheckVoucherHeaderNo = cvNo,
                    Date = form.TransactionDate,
                    SupplierId = null,
                    Particulars = form.Particulars.Trim(),
                    SINo = MergeSiNumbers(form.SiNo, payments.Select(p => p.BillingNumber)),
                    BankId = bank.BankAccountId,
                    CheckNo = form.CheckNo.Trim(),
                    Category = "Trade",
                    Payee = form.Payee.Trim(),
                    CheckDate = form.CheckDate,
                    CheckAmount = cashInBank,
                    Total = cashInBank,
                    InvoiceAmount = totalGross,
                    CreatedBy = actor,
                    Type = form.Type,
                    CvType = nameof(CVType.Commission),
                    SupplierName = rep.RepresentativeName,
                    Address = rep.Address!,
                    Tin = rep.Tin!,
                    BankAccountName = bank.AccountName,
                    BankAccountNumber = bank.AccountNo,
                    OldCvNo = form.OldCVNo,
                    VatType = rep.VatType!,
                    TaxType = rep.TaxType!,
                    TaxPercent = rep.TaxType == MsapConstants.TaxType_WithTax ? rep.WithholdingTaxPercent ?? 0m : 0m,
                    Details = [],
                    MsapCommissionPayments = payments
                };

                CheckVoucherDocumentationService.Apply(header, form.Documentation);
                await PopulateVoucherDetailsAsync(header, totalGross, totalWithholding, cashInBank, manualEntries, bank, ct);

                if (file != null && file.Length > 0)
                {
                    uploaded = "MsapCommissionPayments/" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
                    header.SupportingFileSavedFileName = uploaded;
                    header.SupportingFileSavedUrl = await _storage.UploadFileAsync(file, uploaded);
                }

                await _db.FilprideCheckVoucherHeaders.AddAsync(header, ct);
                await _db.SaveChangesAsync(ct);

                await _db.FilprideAuditTrails.AddAsync(new FilprideAuditTrail(
                    actor,
                    $"Created MSAP Commission voucher {cvNo}; billings {string.Join(", ", payments.Select(p => $"{p.BillingNumber}:{p.Category}"))}",
                    "Check Voucher"), ct);
                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
                return header.CheckVoucherHeaderId;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                if (uploaded != null)
                {
                    try
                    {
                        await _storage.DeleteFileAsync(uploaded);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not remove failed MSAP commission voucher attachment {File}", uploaded);
                    }
                }
                throw;
            }
        }

        public async Task EditAsync(CommissionPaymentViewModel form, IFormFile? file, string actor, CancellationToken ct)
        {
            RequireCompanyWorkspace();
            if (string.IsNullOrWhiteSpace(actor))
            {
                throw new InvalidOperationException("An authenticated editor is required.");
            }
            if (!form.RepresentativeId.HasValue || form.RepresentativeId.Value <= 0)
            {
                throw new InvalidOperationException("Select an MSAP representative.");
            }

            var requestedItems = NormalizeItems(form.MsapItems);
            string? uploaded = null;
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                var header = await _db.FilprideCheckVoucherHeaders
                    .FromSqlInterpolated($"SELECT * FROM filpride_check_voucher_headers WHERE check_voucher_header_id={form.CvId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("Check voucher not found.");
                await _db.Entry(header).ReloadAsync(ct);

                if (header.CvType != nameof(CVType.Commission) || header.PostedBy != null || header.CanceledBy != null || header.VoidedBy != null)
                {
                    throw new InvalidOperationException("Only unposted MSAP commission vouchers can be edited.");
                }

                await GuardPeriodAsync(form.TransactionDate, ct);
                if (!form.CheckNo.Contains("DM", StringComparison.OrdinalIgnoreCase))
                {
                    var duplicateCheck = await _db.FilprideCheckVoucherHeaders
                        .AnyAsync(cv => cv.CheckVoucherHeaderId != header.CheckVoucherHeaderId
                            && cv.CanceledBy == null
                            && cv.VoidedBy == null
                            && cv.CheckNo == form.CheckNo.Trim()
                            && cv.BankId == form.BankId, ct);
                    if (duplicateCheck)
                    {
                        throw new InvalidOperationException("Check No. already exists for this bank account.");
                    }
                }

                await _db.FilprideBankAccounts
                    .FromSqlInterpolated($"SELECT * FROM filpride_bank_accounts WHERE bank_account_id={form.BankId} FOR UPDATE")
                    .LoadAsync(ct);

                var bank = await _db.FilprideBankAccounts
                    .SingleOrDefaultAsync(b => b.BankAccountId == form.BankId && b.IsActive, ct)
                    ?? throw new InvalidOperationException("Select an active bank account.");

                var billingIds = requestedItems.Select(x => x.BillingId).Distinct().ToArray();
                var (rep, billings) = await _sources.SourceForUpdateAsync(form.RepresentativeId.Value, billingIds, ct);

                var profileError = PaymentProfileHelper.Validate(
                    rep, rep.Address, rep.Tin, rep.VatType, rep.TaxType, rep.WithholdingTaxPercent, required: true, "representative");
                if (profileError != null)
                {
                    throw new InvalidOperationException(profileError);
                }

                var reserved = (await _sources.ReservedItemsAsync(header.CheckVoucherHeaderId, ct)).ToHashSet();
                var newPayments = BuildPaymentAllocations(requestedItems, billings, rep, reserved);

                var totalGross = newPayments.Sum(p => p.GrossAmount);
                var totalWithholding = newPayments.Sum(p => p.WithholdingAmount);
                var manualEntries = ExtractManualEntries(form);
                var manualDebits = manualEntries.Sum(e => e.Debit);
                var manualCredits = manualEntries.Sum(e => e.Credit);
                var cashInBank = totalGross - totalWithholding + manualDebits - manualCredits;

                if (totalGross <= 0m || totalWithholding >= totalGross || cashInBank <= 0m)
                {
                    throw new InvalidOperationException("The selected MSAP commission items and entries do not produce a valid positive Cash in Bank amount.");
                }

                var existingAllocations = await _db.FilprideMsapCommissionPayments
                    .Where(p => p.CheckVoucherHeaderId == header.CheckVoucherHeaderId)
                    .ToListAsync(ct);
                if (existingAllocations.Count == 0 || existingAllocations.Any(p => p.ReleasedAt != null))
                {
                    throw new InvalidOperationException("MSAP commission allocations for this voucher are missing or already released.");
                }

                var existingDetails = await _db.FilprideCheckVoucherDetails
                    .Where(d => d.CheckVoucherHeaderId == header.CheckVoucherHeaderId)
                    .ToListAsync(ct);

                _db.FilprideMsapCommissionPayments.RemoveRange(existingAllocations);
                _db.FilprideCheckVoucherDetails.RemoveRange(existingDetails);
                await _db.SaveChangesAsync(ct);

                header.Date = form.TransactionDate;
                header.SupplierId = null;
                header.Particulars = form.Particulars.Trim();
                header.SINo = MergeSiNumbers(form.SiNo, newPayments.Select(p => p.BillingNumber));
                header.BankId = bank.BankAccountId;
                header.CheckNo = form.CheckNo.Trim();
                header.OldCvNo = form.OldCVNo;
                header.Category = "Trade";
                header.Payee = form.Payee.Trim();
                header.CheckDate = form.CheckDate;
                header.CheckAmount = cashInBank;
                header.Total = cashInBank;
                header.InvoiceAmount = totalGross;
                header.EditedBy = actor;
                header.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                header.SupplierName = rep.RepresentativeName;
                header.Address = rep.Address!;
                header.Tin = rep.Tin!;
                header.BankAccountName = bank.AccountName;
                header.BankAccountNumber = bank.AccountNo;
                header.VatType = rep.VatType!;
                header.TaxType = rep.TaxType!;
                header.TaxPercent = rep.TaxType == MsapConstants.TaxType_WithTax ? rep.WithholdingTaxPercent ?? 0m : 0m;
                CheckVoucherDocumentationService.Apply(header, form.Documentation);

                header.Details ??= [];
                foreach (var payment in newPayments)
                {
                    payment.CheckVoucherHeaderId = header.CheckVoucherHeaderId;
                }
                await _db.FilprideMsapCommissionPayments.AddRangeAsync(newPayments, ct);
                await PopulateVoucherDetailsAsync(header, totalGross, totalWithholding, cashInBank, manualEntries, bank, ct);

                if (file != null && file.Length > 0)
                {
                    uploaded = "MsapCommissionPayments/" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
                    header.SupportingFileSavedFileName = uploaded;
                    header.SupportingFileSavedUrl = await _storage.UploadFileAsync(file, uploaded);
                }

                await _db.FilprideAuditTrails.AddAsync(new FilprideAuditTrail(
                    actor,
                    $"Updated MSAP Commission voucher {header.CheckVoucherHeaderNo}; billings {string.Join(", ", newPayments.Select(p => $"{p.BillingNumber}:{p.Category}"))}",
                    "Check Voucher"), ct);
                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                if (uploaded != null)
                {
                    try
                    {
                        await _storage.DeleteFileAsync(uploaded);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not remove failed MSAP commission voucher attachment {File}", uploaded);
                    }
                }
                throw;
            }
        }

        public async Task ValidateBeforePostAsync(FilprideCheckVoucherHeader header, List<FilprideCheckVoucherDetail> details, CancellationToken ct)
        {
            RequireCompanyWorkspace();
            var payments = await _db.FilprideMsapCommissionPayments
                .Where(p => p.CheckVoucherHeaderId == header.CheckVoucherHeaderId)
                .OrderBy(p => p.MsapBillingId)
                .ThenBy(p => p.Category)
                .ToListAsync(ct);

            if (payments.Count == 0)
            {
                return;
            }

            if (payments.Any(p => p.ReleasedAt != null))
            {
                throw new InvalidOperationException("This voucher's MSAP commission reservations have already been released.");
            }

            var repId = payments[0].RepresentativeId;
            var billingIds = payments.Select(p => p.MsapBillingId).Distinct().ToArray();
            var (rep, billings) = await _sources.SourceForUpdateAsync(repId, billingIds, ct);

            var profileError = PaymentProfileHelper.Validate(
                rep, rep.Address, rep.Tin, rep.VatType, rep.TaxType, rep.WithholdingTaxPercent, required: true, "representative");
            if (profileError != null)
            {
                throw new InvalidOperationException(profileError);
            }

            foreach (var payment in payments)
            {
                if (payment.RepresentativeId != repId || !billings.TryGetValue(payment.MsapBillingId, out var billing))
                {
                    throw new InvalidOperationException("MSAP billing or representative changed. Cancel and recreate this voucher.");
                }

                var amount = GetCategoryAmount(billing, payment.Category, repId);
                var (gross, _, _, withholding, _) = PaymentProfileHelper.ComputePayableBreakdown(
                    header.VatType ?? rep.VatType!,
                    header.TaxType ?? rep.TaxType!,
                    header.TaxPercent,
                    amount);

                if (Snapshot(billing, payment.Category, rep) != payment.SourceSnapshot
                    || gross != payment.GrossAmount
                    || withholding != payment.WithholdingAmount)
                {
                    throw new InvalidOperationException($"MSAP billing #{billing.MsapBillingNumber} changed after voucher creation. Edit or cancel this voucher before posting.");
                }
            }

            var totalGross = payments.Sum(p => p.GrossAmount);
            var totalWithholding = payments.Sum(p => p.WithholdingAmount);
            var apDebit = details.Where(d => d.AccountNo == ApAccount).Sum(d => d.Debit);
            var cashCredit = details.Where(d => d.AccountNo == CashAccount).Sum(d => d.Credit);
            var expectedEwtAccount = totalWithholding > 0m ? WithholdingTaxHelper.GetAccountNumberByPercent(header.TaxPercent) : null;
            var ewtCredit = expectedEwtAccount != null
                ? details.Where(d => d.AccountNo == expectedEwtAccount).Sum(d => d.Credit)
                : 0m;

            if (apDebit != totalGross
                || ewtCredit != totalWithholding
                || cashCredit != header.CheckAmount
                || details.Sum(d => d.Debit) != details.Sum(d => d.Credit))
            {
                throw new InvalidOperationException("Voucher entries no longer match the reserved MSAP commission amounts.");
            }
        }

        public async Task ReleaseAllocationsAsync(int cvHeaderId, DateTime releasedAt, CancellationToken ct)
        {
            var payments = await _db.FilprideMsapCommissionPayments
                .Where(p => p.CheckVoucherHeaderId == cvHeaderId && p.ReleasedAt == null)
                .ToListAsync(ct);

            foreach (var payment in payments)
            {
                payment.ReleasedAt = releasedAt;
            }
        }

        public async Task<string[]> GetCosNumbersAsync(IEnumerable<int> billingIds, CancellationToken ct)
        {
            var map = await _sources.GetCosNumbersByBillingIdAsync(billingIds, ct);
            return map.Values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct()
                .ToArray();
        }

        private static MsapCommissionBillableItemDto BuildItemDto(
            Billing billing,
            string category,
            decimal grossAmount,
            MsapRepresentative rep,
            string? profileError,
            bool isSelected)
        {
            decimal withholding = 0m;
            decimal net = grossAmount;
            if (profileError == null)
            {
                (_, _, _, withholding, net) = PaymentProfileHelper.ComputePayableBreakdown(
                    rep.VatType!,
                    rep.TaxType!,
                    rep.WithholdingTaxPercent ?? 0m,
                    grossAmount);
            }

            return new MsapCommissionBillableItemDto
            {
                BillingId = billing.MsapBillingId,
                BillingNumber = billing.MsapBillingNumber,
                Date = billing.Date.ToString("MMM dd, yyyy"),
                CosNumber = billing.COSNumber ?? string.Empty,
                CustomerName = billing.Customer?.CustomerName ?? string.Empty,
                Category = category,
                CategoryLabel = GetCategoryLabel(category),
                GrossAmount = grossAmount,
                WithholdingAmount = withholding,
                NetAmount = net,
                IsSelected = isSelected
            };
        }

        private static List<MsapCommissionItemInput> NormalizeItems(List<MsapCommissionItemInput>? items)
        {
            if (items == null || items.Count == 0)
            {
                throw new InvalidOperationException("Select at least one MSAP billing commission/incentive item.");
            }

            var normalized = items
                .Where(i => i.BillingId > 0 && !string.IsNullOrWhiteSpace(i.Category))
                .Select(i => new MsapCommissionItemInput { BillingId = i.BillingId, Category = i.Category.Trim() })
                .ToList();

            if (normalized.Count != items.Count
                || normalized.Any(i => i.Category is not (MsapConstants.RepresentativeCategory.AgencyIncentive or MsapConstants.RepresentativeCategory.AgentCommission))
                || normalized.Select(i => (i.BillingId, i.Category)).Distinct().Count() != normalized.Count)
            {
                throw new InvalidOperationException("Select valid, distinct MSAP billing commission/incentive items.");
            }

            return normalized;
        }

        private static List<FilprideMsapCommissionPayment> BuildPaymentAllocations(
            List<MsapCommissionItemInput> requestedItems,
            Dictionary<int, Billing> billings,
            MsapRepresentative rep,
            HashSet<(int BillingId, string Category)> reserved)
        {
            var payments = new List<FilprideMsapCommissionPayment>();
            foreach (var item in requestedItems)
            {
                if (reserved.Contains((item.BillingId, item.Category)))
                {
                    throw new InvalidOperationException("One or more selected MSAP billing items are already reserved by another Check Voucher.");
                }

                var billing = billings[item.BillingId];
                var amount = GetCategoryAmount(billing, item.Category, rep.RepresentativeId);
                var (gross, _, _, withholding, _) = PaymentProfileHelper.ComputePayableBreakdown(
                    rep.VatType!,
                    rep.TaxType!,
                    rep.WithholdingTaxPercent ?? 0m,
                    amount);

                payments.Add(new FilprideMsapCommissionPayment
                {
                    MsapBillingId = billing.MsapBillingId,
                    RepresentativeId = rep.RepresentativeId,
                    Category = item.Category,
                    BillingRevision = billing.Revision,
                    BillingNumber = billing.MsapBillingNumber,
                    SourceSnapshot = Snapshot(billing, item.Category, rep),
                    GrossAmount = gross,
                    WithholdingAmount = withholding
                });
            }

            return payments;
        }

        private static decimal GetCategoryAmount(Billing billing, string category, int representativeId)
        {
            if (category == MsapConstants.RepresentativeCategory.AgencyIncentive)
            {
                if (billing.AgencyIncentiveRepresentativeId != representativeId || billing.AgencyIncentiveAmount <= 0m)
                {
                    throw new InvalidOperationException($"Billing #{billing.MsapBillingNumber} has no Agency Incentive payable to this representative.");
                }
                return billing.AgencyIncentiveAmount;
            }

            if (category == MsapConstants.RepresentativeCategory.AgentCommission)
            {
                if (billing.AgentCommissionRepresentativeId != representativeId || billing.AgentCommissionAmount <= 0m)
                {
                    throw new InvalidOperationException($"Billing #{billing.MsapBillingNumber} has no Agent Commission payable to this representative.");
                }
                return billing.AgentCommissionAmount;
            }

            throw new InvalidOperationException("Unsupported commission category.");
        }

        public static string Snapshot(Billing billing, string category, MsapRepresentative rep)
        {
            var amount = category == MsapConstants.RepresentativeCategory.AgencyIncentive
                ? billing.AgencyIncentiveAmount
                : billing.AgentCommissionAmount;

            return JsonSerializer.Serialize(new
            {
                billing.MsapBillingId,
                billing.MsapBillingNumber,
                billing.Date,
                billing.Revision,
                billing.ApprovedRevision,
                billing.COSNumber,
                Category = category,
                rep.RepresentativeId,
                rep.RepresentativeName,
                rep.VatType,
                rep.TaxType,
                rep.WithholdingTaxPercent,
                Amount = amount
            });
        }

        private static List<CheckVoucherTradeAccountingEntryViewModel> ExtractManualEntries(CommissionPaymentViewModel form)
        {
            var manual = new List<CheckVoucherTradeAccountingEntryViewModel>();
            if (form.AccountNumber == null || form.AccountTitle == null || form.Debit == null || form.Credit == null)
            {
                return manual;
            }

            var count = Math.Min(Math.Min(form.AccountNumber.Length, form.AccountTitle.Length), Math.Min(form.Debit.Length, form.Credit.Length));
            for (var i = 0; i < count; i++)
            {
                var acct = form.AccountNumber[i]?.Trim() ?? string.Empty;
                var debit = form.Debit[i];
                var credit = form.Credit[i];
                if (string.IsNullOrEmpty(acct)
                    || (debit == 0m && credit == 0m)
                    || acct == ApAccount
                    || acct == CashAccount
                    || acct.StartsWith("201030", StringComparison.Ordinal))
                {
                    continue;
                }

                if (debit < 0m || credit < 0m || (debit > 0m && credit > 0m))
                {
                    throw new InvalidOperationException("Manual accounting entries must be non-negative on either Debit or Credit.");
                }

                manual.Add(new CheckVoucherTradeAccountingEntryViewModel
                {
                    AccountNumber = acct,
                    AccountTitle = form.AccountTitle[i]?.Trim() ?? string.Empty,
                    Debit = debit,
                    Credit = credit
                });
            }

            return manual;
        }

        private async Task PopulateVoucherDetailsAsync(
            FilprideCheckVoucherHeader header,
            decimal totalGross,
            decimal totalWithholding,
            decimal cashInBank,
            List<CheckVoucherTradeAccountingEntryViewModel> manualEntries,
            IBS.Models.Filpride.MasterFile.FilprideBankAccount bank,
            CancellationToken ct)
        {
            await AddDetailAsync(header, ApAccount, totalGross, 0m, null, null, null, ct);
            await AddDetailAsync(header, CashAccount, 0m, cashInBank, SubAccountType.BankAccount, bank.BankAccountId, $"{bank.AccountNo} {bank.AccountName}", ct);

            if (totalWithholding > 0m)
            {
                var ewtAccount = WithholdingTaxHelper.GetAccountNumberByPercent(header.TaxPercent)
                    ?? throw new InvalidOperationException($"No EWT account mapping found for tax percentage '{header.TaxPercent}'.");
                await AddDetailAsync(header, ewtAccount, 0m, totalWithholding, null, null, null, ct);
            }

            foreach (var entry in manualEntries)
            {
                await AddDetailAsync(header, entry.AccountNumber, entry.Debit, entry.Credit, null, null, null, ct);
            }
        }

        private async Task AddDetailAsync(
            FilprideCheckVoucherHeader header,
            string accountNo,
            decimal debit,
            decimal credit,
            SubAccountType? subAccountType,
            int? subAccountId,
            string? subAccountName,
            CancellationToken ct)
        {
            var title = await _db.FilprideChartOfAccounts
                .SingleOrDefaultAsync(a => a.AccountNumber == accountNo && !a.IsMain && !a.IsHidden, ct)
                ?? throw new InvalidOperationException("Required posting account is unavailable: " + accountNo);

            header.Details!.Add(new FilprideCheckVoucherDetail
            {
                AccountNo = accountNo,
                AccountName = title.AccountName,
                Debit = debit,
                Credit = credit,
                TransactionNo = header.CheckVoucherHeaderNo!,
                CheckVoucherHeaderId = header.CheckVoucherHeaderId,
                SubAccountType = subAccountType,
                SubAccountId = subAccountId,
                SubAccountName = subAccountName
            });
        }

        private static string[] MergeSiNumbers(string? userSiNo, IEnumerable<string> billingNumbers)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(userSiNo))
            {
                list.Add(userSiNo.Trim());
            }
            foreach (var num in billingNumbers.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                if (!list.Contains(num, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(num);
                }
            }
            return list.ToArray();
        }

        private async Task GuardPeriodAsync(DateOnly date, CancellationToken ct)
        {
            if (date == default || await _work.IsPeriodPostedAsync(Module.CheckVoucher, date, ct))
            {
                throw new InvalidOperationException("A valid transaction date in an open accounting period is required.");
            }
        }

        private static string GetCategoryLabel(string category)
        {
            return category == MsapConstants.RepresentativeCategory.AgencyIncentive
                ? "Agency Incentive"
                : "Agent Commission";
        }
    }

    public class MsapRepresentativeCommissionResponse
    {
        public int RepresentativeId { get; set; }
        public string RepresentativeName { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? Tin { get; set; }
        public string? VatType { get; set; }
        public string? TaxType { get; set; }
        public decimal WithholdingTaxPercent { get; set; }
        public string? ProfileError { get; set; }
        public string? WithholdingAccountNumber { get; set; }
        public string? WithholdingAccountTitle { get; set; }
        public List<MsapCommissionBillableItemDto> Items { get; set; } = [];
    }

    public class MsapCommissionBillableItemDto
    {
        public int BillingId { get; set; }
        public string BillingNumber { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string CosNumber { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public decimal GrossAmount { get; set; }
        public decimal WithholdingAmount { get; set; }
        public decimal NetAmount { get; set; }
        public bool IsSelected { get; set; }
    }
}
