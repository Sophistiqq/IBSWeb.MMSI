using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using IBS.DataAccess.Data;
using IBS.DataAccess.Repository.Filpride;
using IBS.DataAccess.Repository.IRepository;
using IBS.Models.Enums;
using IBS.Models.Filpride.Books;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.ViewModels;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IBS.Services
{
    public class TugboatPaymentService
    {
        public const string ApAccount = "201020500";
        public const string CashAccount = "101010100";
        private readonly ApplicationDbContext _db;
        private readonly IUnitOfWork _work;
        private readonly TugboatPaymentRepository _sources;
        private readonly CheckVoucherDocumentationService _documentation;
        private readonly ICloudStorageService _storage;
        private readonly ILogger<TugboatPaymentService> _logger;

        public TugboatPaymentService(ApplicationDbContext db, IUnitOfWork work, CheckVoucherDocumentationService documentation,
            ICloudStorageService storage, ILogger<TugboatPaymentService> logger)
        {
            _db = db;
            _work = work;
            _sources = new TugboatPaymentRepository(db);
            _documentation = documentation;
            _storage = storage;
            _logger = logger;
        }

        private void RequireCompanyWorkspace()
        {
            if (_db.WorkspaceTugboatId.HasValue)
            {
                throw new InvalidOperationException("Select MMSI / All tugboats for company-level AP Other Tugs payments.");
            }
        }

        public static (decimal Gross, decimal Withholding) Calculate(TugboatOwner owner, OtherTugCharge charge)
        {
            string? error = MSAP.TugboatOwnerService.ValidatePaymentProfile(owner, required: true);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }
            return CalculateAmounts(owner.VatType!, owner.TaxType!, owner.WithholdingTaxPercent ?? 0m, charge);
        }

        private static (decimal Gross, decimal Withholding) CalculateAmounts(string vatType, string taxType, decimal withholdingRate, OtherTugCharge charge)
        {
            decimal amount = charge.Lines.Sum(l => l.Amount);
            bool vatable = vatType == "Vatable";
            decimal gross = Math.Round(vatable && !charge.IncludesVat ? amount * 1.12m : amount, 2, MidpointRounding.AwayFromZero);
            decimal basis = vatable ? gross / 1.12m : gross;
            decimal withholding = taxType == "Withholding Tax"
                ? Math.Round(basis * withholdingRate, 2, MidpointRounding.AwayFromZero) : 0m;
            if (gross <= 0 || gross > 99999999999999.99m || withholding >= gross)
            {
                throw new InvalidOperationException("The provider bill must have a positive, valid payable amount. Confirmed zero costs need no payment.");
            }
            return (gross, withholding);
        }

        public async Task PrepareAsync(TugboatPaymentViewModel model, CancellationToken ct)
        {
            RequireCompanyWorkspace();
            List<TugboatOwner> owners = await _sources.OwnersAsync(ct);
            model.Owners = owners.Select(o => new SelectListItem(o.TugboatOwnerName, o.TugboatOwnerId.ToString())).ToList();
            model.Owner = owners.FirstOrDefault(o => o.TugboatOwnerId == model.OwnerId);
            model.OwnerProfileError = model.Owner == null ? null : MSAP.TugboatOwnerService.ValidatePaymentProfile(model.Owner, required: true);
            model.WithholdingAccountNumber = model.OwnerProfileError == null && model.Owner?.TaxType == "Withholding Tax"
                ? WithholdingTaxHelper.GetAccountNumberByPercent(model.Owner.WithholdingTaxPercent!.Value) : null;
            List<int> reserved = await _sources.ReservedAllocationIdsAsync(ct);
            model.Bills = (await _sources.ChargesAsync(model.OwnerId, ct)).Select(charge =>
            {
                List<TugboatPaymentLine> lines = PaymentLines(charge);
                if (model.Owner != null && model.OwnerProfileError == null && charge.Lines.Sum(l => l.Amount) > 0)
                {
                    Dictionary<int, (decimal Gross, decimal Withholding)> amounts = AllocationAmounts(charge, Calculate(model.Owner, charge));
                    foreach (TugboatPaymentLine line in lines)
                    {
                        (line.Gross, line.Withholding) = amounts[line.AllocationId];
                    }
                }
                lines = lines.Where(line => !reserved.Contains(line.AllocationId)).ToList();
                return new TugboatPaymentBillViewModel
                {
                    ChargeId = charge.OtherTugChargeId,
                    ProviderReference = charge.ProviderReference,
                    IncludesVat = charge.IncludesVat,
                    Gross = lines.Sum(line => line.Gross ?? 0m),
                    Withholding = lines.Sum(line => line.Withholding ?? 0m),
                    Lines = lines
                };
            }).ToList();
            model.CustomerOrderSlips = model.Bills.SelectMany(b => b.Lines).Where(l => l.CosId.HasValue)
                .DistinctBy(l => l.CosId).Select(l => new SelectListItem(l.CosNumber, l.CosId!.Value.ToString())).ToList();
            if (model.ChargeId > 0 && model.AllocationIds.Length == 0)
            {
                model.AllocationIds = model.Bills.Where(b => b.ChargeId == model.ChargeId).SelectMany(b => b.Lines)
                    .Where(line => line.Gross > 0).Select(line => line.AllocationId).ToArray();
            }
            List<TugboatPaymentLine> selected = model.Bills.SelectMany(b => b.Lines)
                .Where(line => model.AllocationIds.Contains(line.AllocationId)).ToList();
            if (model.CosIds.Length == 0)
            {
                model.CosIds = selected.Where(l => l.CosId.HasValue).Select(l => l.CosId!.Value).Distinct().ToArray();
            }
            model.Gross = selected.Sum(line => line.Gross ?? 0m);
            model.Withholding = selected.Sum(line => line.Withholding ?? 0m);
            model.Banks = await _db.FilprideBankAccounts.AsNoTracking().Where(b => b.IsActive)
                .OrderBy(b => b.AccountName).Select(b => new SelectListItem(b.AccountName + " · " + b.AccountNo, b.BankAccountId.ToString())).ToListAsync(ct);
            await _documentation.PrepareAsync(model.Documentation, model.Type, null, ct);
        }

        private static Dictionary<int, (decimal Gross, decimal Withholding)> AllocationAmounts(OtherTugCharge charge, (decimal Gross, decimal Withholding) totals)
        {
            (decimal gross, decimal withholding) = totals;
            decimal total = charge.Lines.Sum(line => line.Amount);
            decimal cumulative = 0m;
            decimal allocatedGross = 0m;
            decimal allocatedWithholding = 0m;
            var amounts = new Dictionary<int, (decimal Gross, decimal Withholding)>();
            foreach (OtherTugChargeLine line in charge.Lines.OrderBy(line => line.OtherTugChargeLineId))
            {
                // Cumulative rounding keeps separate ticket payments equal to the complete bill's tax totals.
                cumulative += line.Amount;
                decimal nextGross = Math.Round(gross * cumulative / total, 2, MidpointRounding.AwayFromZero);
                decimal nextWithholding = Math.Round(withholding * cumulative / total, 2, MidpointRounding.AwayFromZero);
                amounts.Add(line.OtherTugChargeLineId, (nextGross - allocatedGross, nextWithholding - allocatedWithholding));
                allocatedGross = nextGross;
                allocatedWithholding = nextWithholding;
            }
            return amounts;
        }

        public static string Snapshot(OtherTugCharge charge)
        {
            return JsonSerializer.Serialize(new
            {
                charge.OtherTugChargeId,
                charge.TugboatOwnerId,
                charge.ProviderReference,
                charge.ProviderName,
                charge.DocumentDate,
                charge.IncludesVat,
                charge.Revision,
                charge.ConfirmedAt,
                charge.ConfirmedByUserId,
                Lines = PaymentLines(charge)
            });
        }

        private static List<TugboatPaymentLine> PaymentLines(OtherTugCharge charge)
        {
            return charge.Lines.OrderBy(l => l.OtherTugChargeLineId).Select(l => new TugboatPaymentLine
            {
                AllocationId = l.OtherTugChargeLineId,
                TicketId = l.DispatchTicketId,
                TugboatId = l.Ticket.TugBoatId,
                CosId = l.Ticket.CustomerOrderSlipId,
                CosNumber = l.Ticket.CustomerOrderSlip?.CustomerOrderSlipNumber,
                TicketNumber = l.Ticket.DispatchNumber,
                TugboatName = l.Ticket.Tugboat.TugboatName,
                Amount = l.Amount
            }).ToList();
        }

        public static List<TugboatPaymentLine> Lines(FilprideTugboatPayment payment)
        {
            using var json = JsonDocument.Parse(payment.SourceSnapshot);
            HashSet<int> selected = payment.Allocations.Select(a => a.OtherTugChargeLineId).ToHashSet();
            return json.RootElement.GetProperty("Lines").Deserialize<List<TugboatPaymentLine>>()!
                .Where(line => selected.Contains(line.AllocationId)).ToList();
        }

        private async Task<string> ValidateLiabilityAsync(int id, string payee, string reference, decimal gross, DateOnly paymentDate, CancellationToken ct)
        {
            var journal = await _db.FilprideJournalVoucherHeaders
                .FromSqlInterpolated($"SELECT * FROM filpride_journal_voucher_headers WHERE journal_voucher_header_id={id} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new InvalidOperationException("Select a posted AP liability journal.");
            await _db.Entry(journal).ReloadAsync(ct);
            if (journal.WorkspaceTugboatId != null || journal.AutoReverseNextMonth || journal.CVId != null || journal.PostedBy == null || journal.ApprovedBy == null
                || journal.CanceledBy != null || journal.VoidedBy != null || journal.Date > paymentDate
                || journal.Payee != payee || journal.References != reference)
            {
                throw new InvalidOperationException("Use an approved, posted company-level AP journal dated on/before payment, with the owner's payee name and provider reference.");
            }
            var entries = await _db.FilprideGeneralLedgerBooks.AsNoTracking().Where(g => g.Reference == journal.JournalVoucherHeaderNo)
                .OrderBy(g => g.AccountNo).ThenBy(g => g.Debit).ThenBy(g => g.Credit)
                .Select(g => new { g.Date, g.AccountNo, g.Debit, g.Credit, g.WorkspaceTugboatId }).ToListAsync(ct);
            if (entries.Count == 0 || entries.Any(g => g.Date != journal.Date || g.WorkspaceTugboatId != null || g.Debit < 0 || g.Credit < 0)
                || entries.Sum(g => g.Debit) != gross || entries.Sum(g => g.Credit) != gross
                || entries.Where(g => g.AccountNo == ApAccount).Sum(g => g.Credit - g.Debit) != gross
                || entries.Any(g => g.AccountNo != ApAccount && g.Credit != 0))
            {
                throw new InvalidOperationException($"The posted journal must recognize exactly this bill's gross liability in AP - Trade - Other Tugs ({ApAccount}). Withholding is applied by the payment voucher.");
            }
            return JsonSerializer.Serialize(new { journal.Date, journal.Payee, journal.References, Entries = entries });
        }

        private async Task GuardPeriodAsync(DateOnly date, CancellationToken ct)
        {
            if (date == default || await _work.IsPeriodPostedAsync(Module.CheckVoucher, date, ct))
            {
                throw new InvalidOperationException("A valid payment date in an open accounting period is required.");
            }
        }

        private async Task AddDetailAsync(FilprideCheckVoucherHeader header, string account, decimal debit, decimal credit, CancellationToken ct)
        {
            var title = await _db.FilprideChartOfAccounts.SingleOrDefaultAsync(a => a.AccountNumber == account && !a.IsMain && !a.IsHidden, ct)
                ?? throw new InvalidOperationException("Required posting account is unavailable: " + account);
            header.Details!.Add(new FilprideCheckVoucherDetail
            {
                AccountNo = account,
                AccountName = title.AccountName,
                Debit = debit,
                Credit = credit,
                TransactionNo = header.CheckVoucherHeaderNo!,
                SubAccountType = account == CashAccount ? SubAccountType.BankAccount : null,
                SubAccountId = account == CashAccount ? header.BankId : null,
                SubAccountName = account == CashAccount ? header.BankAccountName : null
            });
        }

        public async Task<int> CreateAsync(TugboatPaymentViewModel form, IFormFile? file, string actor, CancellationToken ct)
        {
            RequireCompanyWorkspace();
            Validator.ValidateObject(form, new ValidationContext(form), true);
            if (form.CheckDate == default || string.IsNullOrWhiteSpace(actor))
            {
                throw new InvalidOperationException("Check date and an authenticated preparer are required.");
            }
            if (file != null && !await MSAP.OtherTugChargeService.IsSupportedDocumentAsync(file, ct))
            {
                throw new InvalidOperationException("Attach a valid PDF/JPEG/PNG document up to 10 MB.");
            }
            if (form.AllocationIds.Length == 0 || form.AllocationIds.Any(id => id <= 0)
                || form.AllocationIds.Distinct().Count() != form.AllocationIds.Length)
            {
                throw new InvalidOperationException("Select valid, distinct tickets for payment.");
            }
            string? uploaded = null;
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await GuardPeriodAsync(form.Date, ct);
                string? error = await _documentation.ValidateAndNormalizeAsync(form.Type, form.Documentation, null, ct);
                if (error != null)
                {
                    throw new InvalidOperationException(error);
                }
                List<int> chargeIds = await _sources.ChargeIdsAsync(form.AllocationIds, ct);
                var payments = new List<FilprideTugboatPayment>();
                TugboatOwner? owner = null;
                foreach (int chargeId in chargeIds)
                {
                    var source = await _sources.SourceAsync(chargeId, true, ct);
                    if (source.Owner.TugboatOwnerId != form.OwnerId)
                    {
                        throw new InvalidOperationException("Select tickets belonging to the same tugboat owner.");
                    }
                    owner = source.Owner;
                    List<OtherTugChargeLine> selected = source.Charge.Lines.Where(line => form.AllocationIds.Contains(line.OtherTugChargeLineId)).ToList();
                    Dictionary<int, (decimal Gross, decimal Withholding)> amounts = AllocationAmounts(source.Charge, Calculate(owner, source.Charge));
                    if (selected.Any(line => line.Amount <= 0 || amounts[line.OtherTugChargeLineId].Gross <= 0)
                        || await _db.FilprideTugboatPaymentAllocations.AnyAsync(a => form.AllocationIds.Contains(a.OtherTugChargeLineId) && a.ReleasedAt == null, ct))
                    {
                        throw new InvalidOperationException("A selected ticket has no payable amount or is already reserved by another voucher.");
                    }
                    payments.Add(new FilprideTugboatPayment
                    {
                        OtherTugChargeId = chargeId,
                        TugboatOwnerId = form.OwnerId,
                        ChargeRevision = source.Charge.Revision,
                        ProviderReference = source.Charge.ProviderReference,
                        SourceSnapshot = Snapshot(source.Charge),
                        GrossAmount = selected.Sum(line => amounts[line.OtherTugChargeLineId].Gross),
                        WithholdingAmount = selected.Sum(line => amounts[line.OtherTugChargeLineId].Withholding),
                        Allocations = selected.Select(line => new FilprideTugboatPaymentAllocation { OtherTugChargeLineId = line.OtherTugChargeLineId }).ToList()
                    });
                }
                if (owner == null || payments.Sum(payment => payment.Allocations.Count) != form.AllocationIds.Length)
                {
                    throw new InvalidOperationException("Some selected tickets are no longer available. Reload the owner and select the tickets again.");
                }
                decimal gross = payments.Sum(payment => payment.GrossAmount);
                decimal withholding = payments.Sum(payment => payment.WithholdingAmount);
                if (gross > 99999999999999.99m || withholding >= gross)
                {
                    throw new InvalidOperationException("The selected tickets exceed the valid payment amount.");
                }
                await _db.FilprideBankAccounts.FromSqlInterpolated($"SELECT * FROM filpride_bank_accounts WHERE bank_account_id={form.BankId} FOR UPDATE").LoadAsync(ct);
                if (await _db.FilprideCheckVoucherHeaders.AnyAsync(v => v.BankId == form.BankId && v.CheckNo == form.CheckNo.Trim()
                    && v.CanceledBy == null && v.VoidedBy == null, ct))
                {
                    throw new InvalidOperationException("This bank check number is already used by another active voucher.");
                }
                var bank = await _db.FilprideBankAccounts.SingleOrDefaultAsync(b => b.BankAccountId == form.BankId && b.IsActive, ct)
                    ?? throw new InvalidOperationException("Select an active bank account.");
                await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20771009)", ct);
                string number = await _work.FilprideCheckVoucher.GenerateCodeAsync(form.Type, ct);
                var voucher = new FilprideCheckVoucherHeader
                {
                    CvType = nameof(CVType.OtherTugs),
                    Category = "Trade",
                    Type = form.Type,
                    CheckVoucherHeaderNo = number,
                    OldCvNo = form.OldCVNo,
                    Date = form.Date,
                    CheckDate = form.CheckDate,
                    CheckNo = form.CheckNo.Trim(),
                    Particulars = form.Particulars.Trim(),
                    Payee = owner.TugboatOwnerName,
                    SupplierName = owner.TugboatOwnerName,
                    Address = owner.Address!,
                    Tin = owner.Tin!,
                    VatType = owner.VatType!,
                    TaxType = owner.TaxType!,
                    TaxPercent = owner.TaxType == "Withholding Tax" ? owner.WithholdingTaxPercent!.Value : 0m,
                    BankId = bank.BankAccountId,
                    BankAccountName = bank.AccountName,
                    BankAccountNumber = bank.AccountNo,
                    Total = gross - withholding,
                    CheckAmount = gross - withholding,
                    InvoiceAmount = gross,
                    SINo = payments.Select(payment => payment.ProviderReference).Distinct().ToArray(),
                    CreatedBy = actor,
                    Details = [],
                    TugboatPayments = payments
                };
                CheckVoucherDocumentationService.Apply(voucher, form.Documentation);
                // shortcut: initial AP recognition is deferred by user decision until Finance resolves IBS-8.
                await AddDetailAsync(voucher, ApAccount, gross, 0, ct);
                await AddDetailAsync(voucher, CashAccount, 0, voucher.CheckAmount, ct);
                if (withholding > 0)
                {
                    await AddDetailAsync(voucher, WithholdingTaxHelper.GetAccountNumberByPercent(voucher.TaxPercent)!, 0, withholding, ct);
                }
                _db.FilprideCheckVoucherHeaders.Add(voucher);
                if (file != null)
                {
                    uploaded = "OtherTugPayments/" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
                    voucher.SupportingFileSavedFileName = uploaded;
                    voucher.SupportingFileSavedUrl = await _storage.UploadFileAsync(file, uploaded);
                }
                await _db.SaveChangesAsync(ct);
                await _db.FilprideAuditTrails.AddAsync(new FilprideAuditTrail(actor, $"Created AP Other Tugs voucher {number}; tickets {string.Join(",", form.AllocationIds.Order())}; direct payment; initial recognition deferred", "Check Voucher"), ct);
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return voucher.CheckVoucherHeaderId;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                if (uploaded != null)
                {
                    try
                    { await _storage.DeleteFileAsync(uploaded); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not remove failed AP Other Tugs attachment {File}", uploaded); }
                }
                throw;
            }
        }

        public async Task<FilprideCheckVoucherHeader?> GetAsync(int id, CancellationToken ct)
        {
            return await _db.FilprideCheckVoucherHeaders.Include(v => v.TugboatPayments).ThenInclude(p => p.Allocations).Include(v => v.Details)
                .SingleOrDefaultAsync(v => v.CheckVoucherHeaderId == id && v.CvType == nameof(CVType.OtherTugs), ct);
        }

        public async Task<bool> ChangeStatusAsync(int id, string action, string actor, string? reason, CancellationToken ct)
        {
            RequireCompanyWorkspace();
            if (string.IsNullOrWhiteSpace(actor))
            {
                throw new InvalidOperationException("An authenticated operator is required.");
            }
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var locked = await _db.FilprideCheckVoucherHeaders.FromSqlInterpolated($"SELECT * FROM filpride_check_voucher_headers WHERE check_voucher_header_id={id} FOR UPDATE")
                    .SingleOrDefaultAsync(ct) ?? throw new InvalidOperationException("Voucher not found.");
                await _db.Entry(locked).ReloadAsync(ct);
                var voucher = await GetAsync(id, ct) ?? throw new InvalidOperationException("AP Other Tugs voucher not found.");
                bool wasPrinted = voucher.IsPrinted;
                List<FilprideTugboatPayment> payments = voucher.TugboatPayments;
                if (payments.Count == 0 || payments.Any(payment => payment.Allocations.Count == 0))
                {
                    throw new InvalidOperationException("Payment source links are missing.");
                }
                if (payments.Any(payment => payment.ReleasedAt != null || payment.Allocations.Any(a => a.ReleasedAt != null))
                    || voucher.CanceledBy != null || voucher.VoidedBy != null)
                {
                    throw new InvalidOperationException("This voucher has already been cancelled or voided. Create a new payment.");
                }
                if (action == "Print")
                {
                    if (voucher.PostedBy == null)
                    {
                        throw new InvalidOperationException("Post the payment before printing its voucher.");
                    }
                    string copy = voucher.IsPrinted ? "reprinted" : "original";
                    _db.FilprideAuditTrails.Add(new FilprideAuditTrail(actor, $"Printed {copy} AP Other Tugs voucher {voucher.CheckVoucherHeaderNo}", "Check Voucher"));
                    voucher.IsPrinted = true;
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return wasPrinted;
                }
                await GuardPeriodAsync(voucher.Date, ct);
                if (voucher.DcrDate != null || voucher.IsPaid || voucher.AmountPaid != 0)
                {
                    throw new InvalidOperationException("Resolve the voucher's Treasury settlement before changing its status.");
                }
                bool posted = voucher.PostedBy != null;
                if (action == "Post")
                {
                    if (posted)
                    { throw new InvalidOperationException("Voucher is already posted."); }
                    foreach (FilprideTugboatPayment payment in payments.OrderBy(payment => payment.OtherTugChargeId))
                    {
                        var source = await _sources.SourceAsync(payment.OtherTugChargeId, true, ct);
                        Dictionary<int, (decimal Gross, decimal Withholding)> amounts = AllocationAmounts(source.Charge, CalculateAmounts(voucher.VatType, voucher.TaxType, voucher.TaxPercent, source.Charge));
                        if (source.Owner.TugboatOwnerId != payment.TugboatOwnerId || payment.TugboatOwnerId != payments[0].TugboatOwnerId
                            || Snapshot(source.Charge) != payment.SourceSnapshot
                            || payment.Allocations.Any(a => !amounts.ContainsKey(a.OtherTugChargeLineId))
                            || payment.Allocations.Sum(a => amounts[a.OtherTugChargeLineId].Gross) != payment.GrossAmount
                            || payment.Allocations.Sum(a => amounts[a.OtherTugChargeLineId].Withholding) != payment.WithholdingAmount)
                        {
                            throw new InvalidOperationException("Provider source changed. Cancel and recreate this voucher.");
                        }
                        if (payment.LiabilityJournalVoucherId.HasValue)
                        {
                            string liability = await ValidateLiabilityAsync(payment.LiabilityJournalVoucherId.Value, voucher.Payee!, payment.ProviderReference, payment.GrossAmount, voucher.Date, ct);
                            if (liability != payment.LiabilitySnapshot)
                            { throw new InvalidOperationException("The linked AP journal changed. Cancel and recreate this voucher."); }
                        }
                    }
                    decimal gross = payments.Sum(payment => payment.GrossAmount);
                    decimal withholding = payments.Sum(payment => payment.WithholdingAmount);
                    var bank = await _db.FilprideBankAccounts.SingleOrDefaultAsync(b => b.BankAccountId == voucher.BankId && b.IsActive, ct);
                    if (bank == null || bank.AccountNo != voucher.BankAccountNumber)
                    { throw new InvalidOperationException("The bank account changed or is inactive."); }
                    var expected = new Dictionary<string, (decimal Debit, decimal Credit)>
                    {
                        [ApAccount] = (gross, 0),
                        [CashAccount] = (0, gross - withholding)
                    };
                    if (withholding > 0)
                    { expected.Add(WithholdingTaxHelper.GetAccountNumberByPercent(voucher.TaxPercent)!, (0, withholding)); }
                    if (voucher.Details!.Count != expected.Count || voucher.Details.Select(d => d.AccountNo).Distinct().Count() != expected.Count || voucher.Total != gross - withholding
                        || voucher.CheckAmount != voucher.Total || voucher.InvoiceAmount != gross
                        || voucher.Details.Any(d => d.AccountNo == CashAccount
                            ? d.SubAccountType != SubAccountType.BankAccount || d.SubAccountId != voucher.BankId
                            : d.SubAccountType != null || d.SubAccountId != null)
                        || voucher.Details.Any(d => d.IsDisplayEntry || !expected.TryGetValue(d.AccountNo, out var value) || value != (d.Debit, d.Credit))
                        || await _db.FilprideGeneralLedgerBooks.AnyAsync(g => g.Reference == voucher.CheckVoucherHeaderNo, ct))
                    { throw new InvalidOperationException("Voucher entries no longer match the reserved provider payment."); }
                    voucher.PostedBy = actor;
                    voucher.PostedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    voucher.Status = nameof(Status.Posted);
                    await _work.FilprideCheckVoucher.PostAsync(voucher, voucher.Details, ct);
                }
                else if (action == "Unpost")
                {
                    if (!posted)
                    { throw new InvalidOperationException("Only posted vouchers can be unposted."); }
                    await _db.FilprideGeneralLedgerBooks.Where(g => g.Reference == voucher.CheckVoucherHeaderNo).ExecuteDeleteAsync(ct);
                    voucher.PostedBy = null;
                    voucher.PostedDate = null;
                    voucher.Status = nameof(CheckVoucherPaymentStatus.ForPosting);
                }
                else if (action == "Cancel" || action == "Void")
                {
                    if (action == "Cancel" && posted || action == "Void" && !posted)
                    { throw new InvalidOperationException("Cancel an unposted draft, or void a posted voucher."); }
                    if (string.IsNullOrWhiteSpace(reason))
                    { throw new InvalidOperationException("Enter a correction reason."); }
                    DateTime releasedAt = DateTimeHelper.GetCurrentPhilippineTime();
                    foreach (FilprideTugboatPayment payment in payments)
                    {
                        payment.ReleasedAt = releasedAt;
                        foreach (FilprideTugboatPaymentAllocation allocation in payment.Allocations)
                        {
                            allocation.ReleasedAt = releasedAt;
                        }
                    }
                    if (posted)
                    {
                        if (await _work.IsPeriodPostedAsync(Module.CheckVoucher, DateOnly.FromDateTime(releasedAt), ct))
                        { throw new InvalidOperationException("The reversal date falls in a closed period."); }
                        await _work.GeneralLedger.ReverseEntries(voucher.CheckVoucherHeaderNo, ct);
                        voucher.VoidedBy = actor;
                        voucher.VoidedDate = releasedAt;
                        voucher.PostedBy = null;
                        voucher.Status = nameof(Status.Voided);
                    }
                    else
                    {
                        voucher.CanceledBy = actor;
                        voucher.CanceledDate = releasedAt;
                        voucher.Status = nameof(Status.Canceled);
                    }
                    voucher.CancellationRemarks = reason.Trim();
                }
                else
                { throw new InvalidOperationException("Unknown payment action."); }
                _db.FilprideAuditTrails.Add(new FilprideAuditTrail(actor, $"{action} AP Other Tugs voucher {voucher.CheckVoucherHeaderNo}: {reason}", "Check Voucher"));
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return wasPrinted;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                throw;
            }
        }
    }
}
