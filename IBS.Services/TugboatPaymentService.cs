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
        public const string ApAccount = "201010100";
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
            decimal amount = charge.Lines.Sum(l => l.Amount);
            bool vatable = owner.VatType == "Vatable";
            decimal gross = Math.Round(vatable && !charge.IncludesVat ? amount * 1.12m : amount, 2, MidpointRounding.AwayFromZero);
            decimal basis = vatable ? gross / 1.12m : gross;
            decimal withholding = owner.TaxType == "Withholding Tax"
                ? Math.Round(basis * owner.WithholdingTaxPercent!.Value, 2, MidpointRounding.AwayFromZero) : 0m;
            if (gross <= 0 || gross > 99999999999999.99m || withholding >= gross)
            {
                throw new InvalidOperationException("The provider bill must have a positive, valid payable amount. Confirmed zero costs need no payment.");
            }
            return (gross, withholding);
        }

        public async Task PrepareAsync(TugboatPaymentViewModel model, CancellationToken ct)
        {
            RequireCompanyWorkspace();
            model.Owners = (await _sources.OwnersAsync(ct)).Select(o => new SelectListItem(o.TugboatOwnerName, o.TugboatOwnerId.ToString())).ToList();
            model.Charges = (await _sources.ChargesAsync(model.OwnerId, ct))
                .Select(c => new SelectListItem($"{c.ProviderReference} · {c.DocumentDate:dd MMM yyyy}", c.OtherTugChargeId.ToString())).ToList();
            model.Banks = await _db.FilprideBankAccounts.AsNoTracking().Where(b => b.IsActive)
                .OrderBy(b => b.AccountName).Select(b => new SelectListItem(b.AccountName + " · " + b.AccountNo, b.BankAccountId.ToString())).ToListAsync(ct);
            if (model.ChargeId > 0)
            {
                var source = await _sources.SourceAsync(model.ChargeId, false, ct);
                if (source.Owner.TugboatOwnerId != model.OwnerId)
                {
                    throw new InvalidOperationException("Select a provider bill belonging to this owner.");
                }
                model.Owner = source.Owner;
                model.Charge = source.Charge;
                (model.Gross, model.Withholding) = Calculate(source.Owner, source.Charge);
                List<int> reserved = await _db.FilprideTugboatPayments.Where(p => p.ReleasedAt == null).Select(p => p.LiabilityJournalVoucherId).ToListAsync(ct);
                model.Journals = await _db.FilprideJournalVoucherHeaders.AsNoTracking()
                    .Where(j => j.WorkspaceTugboatId == null && !j.AutoReverseNextMonth && j.CVId == null && j.PostedBy != null && j.ApprovedBy != null
                        && j.CanceledBy == null && j.VoidedBy == null && j.Payee == source.Owner.TugboatOwnerName
                        && j.References == source.Charge.ProviderReference && !reserved.Contains(j.JournalVoucherHeaderId))
                    .OrderBy(j => j.Date).Select(j => new SelectListItem(j.JournalVoucherHeaderNo, j.JournalVoucherHeaderId.ToString())).ToListAsync(ct);
            }
            await _documentation.PrepareAsync(model.Documentation, model.Type, null, ct);
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
                Lines = charge.Lines.OrderBy(l => l.OtherTugChargeLineId).Select(l => new TugboatPaymentLine
                {
                    AllocationId = l.OtherTugChargeLineId,
                    TicketId = l.DispatchTicketId,
                    TugboatId = l.Ticket.TugBoatId,
                    CosId = l.Ticket.CustomerOrderSlipId,
                    CosNumber = l.Ticket.CustomerOrderSlip?.CustomerOrderSlipNumber,
                    TicketNumber = l.Ticket.DispatchNumber,
                    TugboatName = l.Ticket.Tugboat.TugboatName,
                    Amount = l.Amount
                })
            });
        }

        public static List<TugboatPaymentLine> Lines(FilprideTugboatPayment payment)
        {
            using var json = JsonDocument.Parse(payment.SourceSnapshot);
            return json.RootElement.GetProperty("Lines").Deserialize<List<TugboatPaymentLine>>()!;
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
                throw new InvalidOperationException("The posted journal must recognize exactly this bill's gross AP liability. Withholding is applied by the payment voucher.");
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
                var source = await _sources.SourceAsync(form.ChargeId, true, ct);
                if (source.Owner.TugboatOwnerId != form.OwnerId || await _db.FilprideTugboatPayments.AnyAsync(p => p.OtherTugChargeId == form.ChargeId && p.ReleasedAt == null, ct))
                {
                    throw new InvalidOperationException("The bill is unavailable for this owner or already reserved by another voucher.");
                }
                var amounts = Calculate(source.Owner, source.Charge);
                string liability = await ValidateLiabilityAsync(form.LiabilityJournalId, source.Owner.TugboatOwnerName, source.Charge.ProviderReference, amounts.Gross, form.Date, ct);
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
                    Date = form.Date,
                    CheckDate = form.CheckDate,
                    CheckNo = form.CheckNo.Trim(),
                    Particulars = form.Particulars.Trim(),
                    Payee = source.Owner.TugboatOwnerName,
                    SupplierName = source.Owner.TugboatOwnerName,
                    Address = source.Owner.Address!,
                    Tin = source.Owner.Tin!,
                    VatType = source.Owner.VatType!,
                    TaxType = source.Owner.TaxType!,
                    TaxPercent = source.Owner.TaxType == "Withholding Tax" ? source.Owner.WithholdingTaxPercent!.Value : 0m,
                    BankId = bank.BankAccountId,
                    BankAccountName = bank.AccountName,
                    BankAccountNumber = bank.AccountNo,
                    Total = amounts.Gross - amounts.Withholding,
                    CheckAmount = amounts.Gross - amounts.Withholding,
                    InvoiceAmount = amounts.Gross,
                    SINo = [source.Charge.ProviderReference],
                    CreatedBy = actor,
                    Details = []
                };
                CheckVoucherDocumentationService.Apply(voucher, form.Documentation);
                await AddDetailAsync(voucher, ApAccount, amounts.Gross, 0, ct);
                await AddDetailAsync(voucher, CashAccount, 0, voucher.CheckAmount, ct);
                if (amounts.Withholding > 0)
                {
                    await AddDetailAsync(voucher, WithholdingTaxHelper.GetAccountNumberByPercent(voucher.TaxPercent)!, 0, amounts.Withholding, ct);
                }
                var payment = new FilprideTugboatPayment
                {
                    Voucher = voucher,
                    OtherTugChargeId = source.Charge.OtherTugChargeId,
                    TugboatOwnerId = form.OwnerId,
                    LiabilityJournalVoucherId = form.LiabilityJournalId,
                    ChargeRevision = source.Charge.Revision,
                    ProviderReference = source.Charge.ProviderReference,
                    SourceSnapshot = Snapshot(source.Charge),
                    LiabilitySnapshot = liability,
                    GrossAmount = amounts.Gross,
                    WithholdingAmount = amounts.Withholding
                };
                _db.FilprideTugboatPayments.Add(payment);
                if (file != null)
                {
                    uploaded = "OtherTugPayments/" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
                    voucher.SupportingFileSavedFileName = uploaded;
                    voucher.SupportingFileSavedUrl = await _storage.UploadFileAsync(file, uploaded);
                }
                await _db.SaveChangesAsync(ct);
                await _db.FilprideAuditTrails.AddAsync(new FilprideAuditTrail(actor, $"Created AP Other Tugs voucher {number}; provider bill {payment.OtherTugChargeId}; liability journal {form.LiabilityJournalId}", "Check Voucher"), ct);
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
            return await _db.FilprideCheckVoucherHeaders.Include(v => v.TugboatPayment).Include(v => v.Details)
                .SingleOrDefaultAsync(v => v.CheckVoucherHeaderId == id && v.CvType == nameof(CVType.OtherTugs), ct);
        }

        public async Task ChangeStatusAsync(int id, string action, string actor, string? reason, CancellationToken ct)
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
                var payment = voucher.TugboatPayment ?? throw new InvalidOperationException("Payment source link is missing.");
                if (payment.ReleasedAt != null || voucher.CanceledBy != null || voucher.VoidedBy != null)
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
                    return;
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
                    var source = await _sources.SourceAsync(payment.OtherTugChargeId, true, ct);
                    if (source.Owner.TugboatOwnerId != payment.TugboatOwnerId || Snapshot(source.Charge) != payment.SourceSnapshot)
                    {
                        throw new InvalidOperationException("Provider source changed. Cancel and recreate this voucher.");
                    }
                    string liability = await ValidateLiabilityAsync(payment.LiabilityJournalVoucherId, voucher.Payee!, payment.ProviderReference, payment.GrossAmount, voucher.Date, ct);
                    if (liability != payment.LiabilitySnapshot)
                    { throw new InvalidOperationException("The linked AP journal changed. Cancel and recreate this voucher."); }
                    var bank = await _db.FilprideBankAccounts.SingleOrDefaultAsync(b => b.BankAccountId == voucher.BankId && b.IsActive, ct);
                    if (bank == null || bank.AccountNo != voucher.BankAccountNumber)
                    { throw new InvalidOperationException("The bank account changed or is inactive."); }
                    var expected = new Dictionary<string, (decimal Debit, decimal Credit)>
                    {
                        [ApAccount] = (payment.GrossAmount, 0),
                        [CashAccount] = (0, payment.GrossAmount - payment.WithholdingAmount)
                    };
                    if (payment.WithholdingAmount > 0)
                    { expected.Add(WithholdingTaxHelper.GetAccountNumberByPercent(voucher.TaxPercent)!, (0, payment.WithholdingAmount)); }
                    if (voucher.Details!.Count != expected.Count || voucher.Details.Select(d => d.AccountNo).Distinct().Count() != expected.Count || voucher.Total != payment.GrossAmount - payment.WithholdingAmount
                        || voucher.CheckAmount != voucher.Total || voucher.InvoiceAmount != payment.GrossAmount
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
                    payment.ReleasedAt = DateTimeHelper.GetCurrentPhilippineTime();
                    if (posted)
                    {
                        if (await _work.IsPeriodPostedAsync(Module.CheckVoucher, DateOnly.FromDateTime(payment.ReleasedAt.Value), ct))
                        { throw new InvalidOperationException("The reversal date falls in a closed period."); }
                        await _work.GeneralLedger.ReverseEntries(voucher.CheckVoucherHeaderNo, ct);
                        voucher.VoidedBy = actor;
                        voucher.VoidedDate = payment.ReleasedAt;
                        voucher.PostedBy = null;
                        voucher.Status = nameof(Status.Voided);
                    }
                    else
                    {
                        voucher.CanceledBy = actor;
                        voucher.CanceledDate = payment.ReleasedAt;
                        voucher.Status = nameof(Status.Canceled);
                    }
                    voucher.CancellationRemarks = reason.Trim();
                }
                else
                { throw new InvalidOperationException("Unknown payment action."); }
                _db.FilprideAuditTrails.Add(new FilprideAuditTrail(actor, $"{action} AP Other Tugs voucher {voucher.CheckVoucherHeaderNo}: {reason}", "Check Voucher"));
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
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
