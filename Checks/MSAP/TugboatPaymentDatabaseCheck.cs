using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.Repository;
using IBS.Models;
using IBS.Models.Filpride;
using IBS.Models.Enums;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.MasterFile;
using IBS.Models.Filpride.ViewModels;
using IBS.Models.MSAP;
using IBS.Services;
using IBS.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IBSWeb.Areas.Filpride.Controllers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Checks.MSAP
{
    internal static class TugboatPaymentDatabaseCheck
    {
        public static async Task RunAsync()
        {
            string connection = TugboatOwnerCheck.TestConnection();
            await using var source = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql(connection).Options);
            await using var services = new ServiceCollection().AddLogging().AddSingleton<TugboatWorkspace>()
                .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection))
                .AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                .Services.BuildServiceProvider();
            var options = services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
            await using var db = Open();
            await db.Database.MigrateAsync();
            Check(!db.Database.HasPendingModelChanges(), "Payment migration differs from the model.");
            var ticket = await source.MsapDispatchTickets.Include(t => t.ExpectedServiceEntry)
                .Where(t => t.BillingId != null && t.VesselScheduleServiceEntryId != null)
                .OrderBy(t => t.DispatchTicketId).FirstAsync();
            var tug = await source.MsapTugboats.FirstAsync(t => !t.IsCompanyOwned && t.TugboatOwnerId != null);
            var owner = await source.MsapTugboatOwners.SingleAsync(o => o.TugboatOwnerId == tug.TugboatOwnerId);
            owner.Address = "Disposable payment fixture";
            owner.Tin = "000-000-000-00000";
            owner.VatType = "Vatable";
            owner.TaxType = "Withholding Tax";
            owner.WithholdingTaxPercent = 0.02m;
            ticket.TugBoatId = tug.TugboatId;
            ticket.ExpectedServiceEntry!.TugboatId = tug.TugboatId;
            string reference = "PAY-" + Guid.NewGuid().ToString("N");
            var charge = new OtherTugCharge
            {
                TugboatOwnerId = owner.TugboatOwnerId,
                ProviderName = owner.TugboatOwnerName,
                ProviderReference = reference,
                DocumentDate = DateOnly.FromDateTime(DateTime.Today),
                IncludesVat = true,
                PreparedByName = "Payment check",
                PreparedByUserId = "payment-check",
                UpdatedAt = DateTime.Today,
                ConfirmedAt = DateTime.Today,
                ConfirmedByName = "Payment check",
                ConfirmedByUserId = "payment-check",
                Lines = [new OtherTugChargeLine { DispatchTicketId = ticket.DispatchTicketId, Amount = 1120m }]
            };
            source.Add(charge);
            await source.SaveChangesAsync();
            Check(TugboatPaymentService.Calculate(owner, charge) == (1120m, 20m), "VAT-inclusive withholding is wrong.");
            charge.IncludesVat = false;
            charge.Lines[0].Amount = 1000m;
            Check(TugboatPaymentService.Calculate(owner, charge) == (1120m, 20m), "VAT-exclusive withholding is wrong.");
            charge.IncludesVat = true;
            charge.Lines[0].Amount = 1120m;
            source.ChangeTracker.Clear();
            var bank = await db.FilprideBankAccounts.FirstAsync(b => b.IsActive);
            var baseline = new Dictionary<string, string>
            {
                ["201020500"] = "AP - Trade - Other Tugs",
                ["201020600"] = "AP - Trade - SBMA",
                ["201020700"] = "AP - Trade - Commission",
                ["201020800"] = "AP - Trade - Incentive",
                ["201020900"] = "AP - Trade - Fuel",
                ["201021000"] = "AP - Trade - Freight",
                ["201021100"] = "AP - Trade - Trading Fee",
                ["201021200"] = "AP - Trade - Lubes"
            };
            var accounts = await db.FilprideChartOfAccounts.AsNoTracking()
                .Where(a => baseline.Keys.Contains(a.AccountNumber)).ToListAsync();
            Check(accounts.Count == baseline.Count && accounts.All(a => a.AccountName == baseline[a.AccountNumber]
                && !a.IsMain && !a.HasChildren && !a.IsHidden && a.AccountType == "Liabilities"
                && a.NormalBalance == "Credit" && a.FinancialStatementType == "BalanceSheet"),
                "The screenshot baseline requires the eight dedicated AP Trade liability accounts created in the dev chart.");
            var payableAccount = accounts.Single(a => a.AccountNumber == "201020500");
            Check(TugboatPaymentService.ApAccount == payableAccount.AccountNumber && !payableAccount.IsMain && !payableAccount.HasChildren
                && payableAccount.AccountType == "Liabilities" && payableAccount.NormalBalance == "Credit"
                && payableAccount.FinancialStatementType == "BalanceSheet",
                "AP Other Tugs must use the dedicated 201020500 payable, not the generic trade control or a P&L account.");
            // Clone-only COGS classification; production mapping remains a Finance decision.
            var costRoot = await db.FilprideChartOfAccounts.SingleAsync(a => a.AccountNumber == "500000000");
            List<string> costBranches = await db.FilprideChartOfAccounts.IgnoreQueryFilters(["MasterVisibility"])
                .Where(a => a.ParentAccountId == costRoot.AccountId).Select(a => a.AccountNumber).ToListAsync();
            long lastCostBranch = costBranches.Select(long.Parse).Max();
            var serviceCosts = CostAccount("MMSI service costs (test)", (lastCostBranch + 1000000).ToString(), costRoot);
            var externalTugs = CostAccount("External tug services (test)", (lastCostBranch + 1010000).ToString(), serviceCosts);
            var expense = CostAccount("Services by Other Tugs (test)", (lastCostBranch + 1010100).ToString(), externalTugs);
            db.Add(serviceCosts);
            db.Add(externalTugs);
            db.Add(expense);
            await db.SaveChangesAsync();
            var journal = new FilprideJournalVoucherHeader
            {
                JournalVoucherHeaderNo = "J" + Guid.NewGuid().ToString("N")[..12],
                Date = charge.DocumentDate,
                Payee = owner.TugboatOwnerName,
                References = reference,
                Particulars = "Fixture gross AP recognition",
                JVReason = "Isolated database check",
                Type = "Documented",
                CreatedBy = "Payment check",
                ApprovedBy = "Payment approver",
                PostedBy = "Payment poster",
                Details = []
            };
            journal.Details.Add(new FilprideJournalVoucherDetail { AccountNo = expense.AccountNumber, Debit = 1000m });
            journal.Details.Add(new FilprideJournalVoucherDetail { AccountNo = "101060200", Debit = 120m });
            journal.Details.Add(new FilprideJournalVoucherDetail { AccountNo = TugboatPaymentService.ApAccount, Credit = 1120m });
            db.Add(journal);
            await db.SaveChangesAsync();
            await new UnitOfWork(db).FilprideJournalVoucher.PostAsync(journal, journal.Details);
            var storage = new Storage();
            var service = Service(db, storage);
            var form = Form();
            var prepared = Form();
            await service.PrepareAsync(prepared, default);
            Check(prepared.Owner?.TugboatOwnerId == owner.TugboatOwnerId && prepared.Journals.Any(j => j.Value == journal.JournalVoucherHeaderId.ToString()),
                "Payment selection did not load its owner, bill and matching posted AP journal.");
            form.LiabilityJournalId = int.MaxValue;
            await RejectAsync(() => service.CreateAsync(form, null, "Payment check", default), "Missing liability was accepted.");
            form = Form();
            var genericTrade = await db.FilprideChartOfAccounts.SingleAsync(a => a.AccountNumber == "201010100");
            var wrongControl = await db.FilprideGeneralLedgerBooks.SingleAsync(g => g.Reference == journal.JournalVoucherHeaderNo && g.Credit == 1120m);
            wrongControl.AccountNo = genericTrade.AccountNumber;
            wrongControl.AccountId = genericTrade.AccountId;
            wrongControl.AccountTitle = genericTrade.AccountName;
            await db.SaveChangesAsync();
            var wrongSelection = Form();
            await service.PrepareAsync(wrongSelection, default);
            Check(!wrongSelection.Journals.Any(j => j.Value == journal.JournalVoucherHeaderId.ToString()),
                "A journal credited to generic AP Trade was offered for an Other Tugs payment.");
            await RejectAsync(() => service.CreateAsync(form, null, "Payment check", default), "Generic AP Trade was accepted instead of 201020500.");
            await db.FilprideGeneralLedgerBooks.Where(g => g.Reference == journal.JournalVoucherHeaderNo && g.Credit == 1120m)
                .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.AccountNo, payableAccount.AccountNumber)
                    .SetProperty(g => g.AccountId, payableAccount.AccountId).SetProperty(g => g.AccountTitle, payableAccount.AccountName));
            var originalName = journal.Payee;
            journal.Payee = "Wrong owner";
            db.Update(journal);
            await db.SaveChangesAsync();
            await RejectAsync(() => service.CreateAsync(form, null, "Payment check", default), "Wrong-payee liability was accepted.");
            journal.Payee = originalName;
            db.Update(journal);
            await db.SaveChangesAsync();
            storage.Fail = true;
            byte[] document = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nfixture document");
            await using (var stream = new MemoryStream(document))
            {
                var file = new FormFile(stream, 0, stream.Length, "file", "fixture.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
                await RejectAsync(() => service.CreateAsync(form, file, "Payment check", default), "Failed upload left a payment.");
            }
            Check(storage.Deleted && !await db.FilprideTugboatPayments.AnyAsync(p => p.OtherTugChargeId == charge.OtherTugChargeId),
                "Failed upload did not compensate and release the reservation.");
            storage.Fail = false;
            int id = await service.CreateAsync(form, null, "Payment check", default);
            var saved = (await service.GetAsync(id, default))!;
            Check(saved.SupplierId == null && saved.Payee == owner.TugboatOwnerName && saved.CheckAmount == 1100m
                && saved.TugboatPayment!.GrossAmount == 1120m && saved.Details!.Count == 3
                && saved.Details.All(d => !d.IsDisplayEntry), "Payment snapshot or real accounting entries are incorrect.");
            await RejectAsync(() => service.CreateAsync(Form(), null, "Payment check", default), "Duplicate bill reservation was accepted.");
            await RejectSqlAsync($"UPDATE msap.mmsi_other_tug_charges SET provider_reference='changed' WHERE other_tug_charge_id={charge.OtherTugChargeId}");
            await RejectSqlAsync($"UPDATE msap.mmsi_other_tug_charge_lines SET amount=1 WHERE other_tug_charge_id={charge.OtherTugChargeId}");
            await RejectSqlAsync($"UPDATE filpride_journal_voucher_headers SET posted_by=NULL WHERE journal_voucher_header_id={journal.JournalVoucherHeaderId}");
            await RejectSqlAsync($"UPDATE filpride_journal_voucher_details SET debit=1 WHERE journal_voucher_header_id={journal.JournalVoucherHeaderId}");
            // Owner maintenance must not recalculate a saved voucher.
            var updatedOwner = await source.MsapTugboatOwners.SingleAsync(o => o.TugboatOwnerId == owner.TugboatOwnerId);
            updatedOwner.WithholdingTaxPercent = 0.01m;
            await source.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Print", "Payment check", null, default), "A draft voucher could be printed as posted.");
            var inactive = await db.FilprideBankAccounts.SingleAsync(b => b.BankAccountId == bank.BankAccountId);
            inactive.IsActive = false;
            await db.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "Inactive bank was accepted at posting.");
            inactive.IsActive = true;
            db.Update(inactive);
            await db.SaveChangesAsync();
            var period = new PostedPeriod { Module = nameof(Module.CheckVoucher), Month = form.Date.Month, Year = form.Date.Year, IsPosted = true, PostedBy = "Payment check" };
            db.PostedPeriods.Add(period);
            await db.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "A closed payment period was accepted.");
            db.PostedPeriods.Remove(period);
            await db.SaveChangesAsync();
            var altered = await db.FilprideCheckVoucherDetails.SingleAsync(d => d.CheckVoucherHeaderId == id && d.AccountNo == TugboatPaymentService.ApAccount);
            altered.Debit = 1m;
            await db.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "Altered payment entries were accepted.");
            altered.Debit = 1120m;
            db.Update(altered);
            await db.SaveChangesAsync();
            await service.ChangeStatusAsync(id, "Post", "Payment check", null, default);
            await CheckLedgerAsync(saved.CheckVoucherHeaderNo!, 3, 1120m);
            await new UnitOfWork(db).FilprideCheckVoucher.RebuildGeneralLedgerAsync(form.Date, form.Date.AddDays(1));
            await CheckLedgerAsync(saved.CheckVoucherHeaderNo!, 3, 1120m);
            await service.ChangeStatusAsync(id, "Print", "Payment check", null, default);
            Check((await service.GetAsync(id, default))!.IsPrinted, "Printing did not preserve its audit state.");
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "Repeated posting was accepted.");
            await RejectAsync(() => service.ChangeStatusAsync(id, "Cancel", "Payment check", "Correction", default), "A posted voucher could be cancelled without reversal.");
            await service.ChangeStatusAsync(id, "Unpost", "Payment check", null, default);
            Check(!await db.FilprideGeneralLedgerBooks.AnyAsync(g => g.Reference == saved.CheckVoucherHeaderNo)
                && (await service.GetAsync(id, default))!.TugboatPayment!.ReleasedAt == null, "Unposting released a bill or retained entries.");
            await RejectAsync(() => service.CreateAsync(Form(), null, "Payment check", default), "Unposting allowed a duplicate reservation.");
            await service.ChangeStatusAsync(id, "Post", "Payment check", null, default);
            await service.ChangeStatusAsync(id, "Void", "Payment check", "Correction fixture", default);
            await CheckLedgerAsync(saved.CheckVoucherHeaderNo!, 6, 2240m);
            Check(await db.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == id && p.ReleasedAt != null), "Voiding did not release the bill.");
            updatedOwner.WithholdingTaxPercent = 0.02m;
            await source.SaveChangesAsync();
            int replacement = await service.CreateAsync(Form(), null, "Payment check", default);
            await service.ChangeStatusAsync(replacement, "Cancel", "Payment check", "Correction fixture", default);
            Check(await db.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == replacement && p.ReleasedAt != null), "Cancellation retained a reservation.");
            var races = await Task.WhenAll(RaceAsync(), RaceAsync());
            Check(races.Count(x => x) == 1, "Concurrent drafts did not produce exactly one reservation.");
            int active = await db.FilprideTugboatPayments.Where(p => p.OtherTugChargeId == charge.OtherTugChargeId && p.ReleasedAt == null)
                .Select(p => p.CheckVoucherHeaderId).SingleAsync();
            var work = new UnitOfWork(db);
            var documentation = new CheckVoucherDocumentationService(db);
            var nonTrade = new CheckVoucherNonTradePaymentController(work, null!, db, storage,
                NullLogger<CheckVoucherNonTradePaymentController>.Instance, null!, documentation);
            var invoice = new CheckVoucherNonTradeInvoiceController(work, null!, db, storage,
                NullLogger<CheckVoucherNonTradeInvoiceController>.Instance, null!, documentation);
            var payroll = new CheckVoucherNonTradePayrollInvoiceController(work, null!, db, storage,
                NullLogger<CheckVoucherNonTradeInvoiceController>.Instance, null!, documentation);
            Check(await nonTrade.Post(active, default) is NotFoundResult
                && await nonTrade.Cancel(active, "Forged non-trade request", default) is NotFoundResult
                && await invoice.Post(active, null, default) is NotFoundResult
                && await invoice.Cancel(active, "Forged invoice request", default) is NotFoundResult
                && await payroll.Post(active, null, default) is NotFoundResult
                && await payroll.Cancel(active, "Forged payroll request", default) is NotFoundResult,
                "A non-trade action could bypass the AP Other Tugs lifecycle.");
            Check(await db.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == active && p.ReleasedAt == null)
                && (await service.GetAsync(active, default))!.CanceledBy == null,
                "A rejected non-trade request changed the provider reservation.");
            var workspace = new TugboatWorkspace();
            workspace.Select(await source.MsapTugboats.Where(t => t.IsCompanyOwned).Select(t => t.TugboatId).FirstAsync(), "Check tugboat");
            await using (var scoped = new ApplicationDbContext(options, workspace))
            {
                Check(!await scoped.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == active), "Company payment leaked into a tugboat workspace.");
                await RejectAsync(() => Service(scoped, storage).CreateAsync(Form(), null, "Payment check", default), "Company-level provider AP entered a tugboat workspace.");
            }
            Console.WriteLine($"PASS: AP Other Tugs gross/VAT/EWT, posted liability validation, upload rollback, source locks, duplicate/concurrent reservations, immutable snapshots, post/unpost/void/cancel, dedicated 201020500 selection and generic AP rejection, named service-cost/input-VAT recognition without duplicate P&L expense, AP reconciliation and workspace isolation. Clone fixture charge={charge.OtherTugChargeId}, draft CV={active}.");

            static FilprideChartOfAccount CostAccount(string name, string number, FilprideChartOfAccount parent)
            {
                parent.HasChildren = true;
                return new FilprideChartOfAccount
                {
                    AccountName = name, AccountNumber = number, ParentAccount = parent, Level = parent.Level + 1,
                    AccountType = parent.AccountType, NormalBalance = parent.NormalBalance,
                    FinancialStatementType = parent.FinancialStatementType, CreatedBy = "Payment check"
                };
            }

            ApplicationDbContext Open()
            {
                return new ApplicationDbContext(options);
            }
            TugboatPaymentViewModel Form()
            {
                return new TugboatPaymentViewModel
                {
                    OwnerId = owner.TugboatOwnerId,
                    ChargeId = charge.OtherTugChargeId,
                    LiabilityJournalId = journal.JournalVoucherHeaderId,
                    BankId = bank.BankAccountId,
                    CheckNo = "T" + Guid.NewGuid().ToString("N"),
                    Date = charge.DocumentDate,
                    CheckDate = charge.DocumentDate,
                    Particulars = "Isolated AP Other Tugs settlement check"
                };
            }
            async Task<bool> RaceAsync()
            {
                await using var competing = Open();
                try
                { await Service(competing, storage).CreateAsync(Form(), null, "Payment check", default); return true; }
                catch (InvalidOperationException) { return false; }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { return false; }
            }
            async Task RejectSqlAsync(string sql)
            {
                await using var isolated = Open();
                try
                { await isolated.Database.ExecuteSqlRawAsync(sql); }
                catch (PostgresException ex) when (ex.SqlState == "P0001") { return; }
                throw new InvalidOperationException("Reserved source mutation was accepted: " + sql);
            }
            async Task CheckLedgerAsync(string number, int count, decimal total)
            {
                var rows = await db.FilprideGeneralLedgerBooks.AsNoTracking().Where(g => g.Reference == number).ToListAsync();
                Check(rows.Count == count && rows.Sum(g => g.Debit) == total && rows.Sum(g => g.Credit) == total
                    && rows.All(g => g.CounterpartyType == CounterpartyType.TugboatOwner && g.CounterpartyId == owner.TugboatOwnerId),
                    "Payment ledger is unbalanced, duplicated or lost owner identity.");
                Check(await db.FilprideGeneralLedgerBooks.Where(g => g.Reference == journal.JournalVoucherHeaderNo && g.AccountId == expense.AccountId)
                        .SumAsync(g => g.Debit - g.Credit) == 1000m
                    && await db.FilprideGeneralLedgerBooks.Where(g => g.Reference == journal.JournalVoucherHeaderNo && g.AccountNo == "101060200")
                        .SumAsync(g => g.Debit - g.Credit) == 120m
                    && !await db.FilprideGeneralLedgerBooks.AnyAsync(g => g.Reference == number && g.Account.FinancialStatementType == "PnL"),
                    "Settlement/rebuild/reversal duplicated the service cost or treated recoverable input VAT as a P&L charge.");
                decimal payable = await db.FilprideGeneralLedgerBooks.Where(g => g.AccountNo == TugboatPaymentService.ApAccount
                        && (g.Reference == journal.JournalVoucherHeaderNo || g.Reference == number))
                    .SumAsync(g => g.Credit - g.Debit);
                Check(payable == (count == 3 ? 0m : 1120m), "Payment/reversal did not reconcile to the initially recognized provider liability.");
            }
        }

        private static TugboatPaymentService Service(ApplicationDbContext db, Storage storage)
        {
            return new TugboatPaymentService(db, new UnitOfWork(db), new CheckVoucherDocumentationService(db), storage,
                NullLogger<TugboatPaymentService>.Instance);
        }

        private static async Task RejectAsync(Func<Task> action, string message)
        {
            try
            { await action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(message);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            { throw new InvalidOperationException(message); }
        }

        private sealed class Storage: ICloudStorageService
        {
            public bool Fail { get; set; }
            public bool Deleted { get; private set; }
            public Task<string> UploadFileAsync(IFormFile fileToUpload, string fileNameToSave)
            {
                if (Fail)
                { throw new InvalidOperationException("Fixture upload failure."); }
                return Task.FromResult("fixture://payment");
            }
            public Task DeleteFileAsync(string fileNameToDelete) { Deleted = true; return Task.CompletedTask; }
            public Task<string> GetSignedUrlAsync(string fileNameToRead, int timeOutInMinutes = 30) { return Task.FromResult("fixture://payment"); }
            public Task<Stream> DownloadFileAsync(string fileNameToDownload) { throw new NotSupportedException(); }
            public Task<IFormFile?> GetFileAsFormFile(string fileName) { return Task.FromResult<IFormFile?>(null); }
        }
    }
}
