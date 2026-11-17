using System.Security.Claims;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.Repository;
using IBS.Models;
using IBS.Models.Enums;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.MasterFile;
using IBS.Models.Filpride.ViewModels;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility;
using IBS.Utility.Helpers;
using IBS.Utility.MSAP.Constants;
using IBSWeb.Areas.Filpride.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Checks.MSAP
{
    internal static class MsapCommissionPaymentDatabaseCheck
    {
        public static async Task RunAsync()
        {
            string connection = TugboatOwnerCheck.TestConnection();
            await using var source = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(connection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options);
            await using var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<TugboatWorkspace>()
                .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection))
                .AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                .Services.BuildServiceProvider();
            var options = services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
            await using var db = Open();

            await source.Database.MigrateAsync();
            await db.Database.MigrateAsync();
            Check(!source.Database.HasPendingModelChanges(), "MSAP representative migration differs from the model.");
            Check(!db.Database.HasPendingModelChanges(), "MSAP commission payment migration differs from the model.");

            // Check rollback & reapply of both new migrations cleanly
            string[] msapMigrations = source.Database.GetMigrations().ToArray();
            int repProfileIdx = Array.FindIndex(msapMigrations, x => x.EndsWith("_AddRepresentativePaymentProfile", StringComparison.Ordinal));
            Check(repProfileIdx > 0, "AddRepresentativePaymentProfile migration not found.");

            string[] pubMigrations = db.Database.GetMigrations().ToArray();
            int commPayIdx = Array.FindIndex(pubMigrations, x => x.EndsWith("_AddMsapCommissionPayments", StringComparison.Ordinal));
            Check(commPayIdx > 0, "AddMsapCommissionPayments migration not found.");

            await db.GetService<IMigrator>().MigrateAsync(pubMigrations[commPayIdx - 1]);
            await source.GetService<IMigrator>().MigrateAsync(msapMigrations[repProfileIdx - 1]);
            await source.Database.MigrateAsync();
            await db.Database.MigrateAsync();

            // Check COA accounts in clone (seed provisional 503010500 if missing, and report)
            bool hadExpenseAccount = await db.FilprideChartOfAccounts.IgnoreQueryFilters()
                .AnyAsync(a => a.AccountNumber == MsapConstants.CommissionExpenseAccount);
            Console.WriteLine($"DIAGNOSTIC: ChartOfAccount {MsapConstants.CommissionExpenseAccount} present in source clone: {hadExpenseAccount}");
            if (!hadExpenseAccount)
            {
                db.FilprideChartOfAccounts.Add(new FilprideChartOfAccount
                {
                    AccountNumber = MsapConstants.CommissionExpenseAccount,
                    AccountName = "COGS - MSAP Commission / Incentive (Provisional)",
                    AccountType = "Expense",
                    NormalBalance = "Debit",
                    Level = 4,
                    FinancialStatementType = "PnL",
                    CreatedBy = "Commission check"
                });
                await db.SaveChangesAsync();
            }

            var apAccount = await db.FilprideChartOfAccounts.AsNoTracking()
                .SingleOrDefaultAsync(a => a.AccountNumber == MsapCommissionPaymentService.ApAccount);
            Check(apAccount != null && !apAccount.IsMain && !apAccount.HasChildren && !apAccount.IsHidden
                && apAccount.AccountType == "Liabilities" && apAccount.NormalBalance == "Credit",
                "MSAP Commission payable must use 201010200 Liabilities account.");

            // Validate PaymentProfileHelper
            var testRep = new MsapRepresentative { RepresentativeName = "Check Rep" };
            Check(PaymentProfileHelper.Validate(testRep, testRep.Address, testRep.Tin, testRep.VatType, testRep.TaxType, testRep.WithholdingTaxPercent, required: false, "representative") == null,
                "Optional representative profile validation rejected blank profile.");
            Check(PaymentProfileHelper.Validate(testRep, testRep.Address, testRep.Tin, testRep.VatType, testRep.TaxType, testRep.WithholdingTaxPercent, required: true, "representative") != null,
                "Required representative profile validation accepted incomplete profile.");

            var (grossCalc, baseCalc, vatCalc, ewtCalc, netCalc) = PaymentProfileHelper.ComputePayableBreakdown(
                MsapConstants.VatType_Vatable, MsapConstants.TaxType_WithTax, 0.02m, 1120m);
            Check(grossCalc == 1120m && baseCalc == 1000m && vatCalc == 120m && ewtCalc == 20m && netCalc == 1100m,
                "Gross/VAT-inclusive EWT calculation mismatch.");

            // Create disposable representative, approved billing, and test full lifecycle
            var actor = await source.Users.FirstAsync(u => u.IsActive && !string.IsNullOrEmpty(u.Name));
            var rep = new MsapRepresentative
            {
                RepresentativeName = "Check Agent " + Guid.NewGuid().ToString("N")[..6],
                Category = CommissionCalculator.CategoryAgentCommission,
                RateMode = CommissionCalculator.ModePercentage,
                RateValue = 10m,
                CalculationBase = CommissionCalculator.BaseGross,
                Address = "Pasig City",
                Tin = "000-111-222-00000",
                VatType = MsapConstants.VatType_Vatable,
                TaxType = MsapConstants.TaxType_WithTax,
                WithholdingTaxPercent = 0.02m,
                IsActive = true,
                CreatedBy = actor.UserName,
                CreatedDate = DateTime.Now
            };
            source.MsapRepresentatives.Add(rep);
            await source.SaveChangesAsync();

            var customer = await source.Customers.FirstAsync(c => c.Company == MsapConstants.Company_MMSI && c.IsActive);
            var vessel = await source.MsapVessels.FirstAsync();
            var port = await source.MsapPorts.FirstAsync();
            var terminal = await source.MsapTerminals.FirstAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var billingNo = "MC" + Guid.NewGuid().ToString("N")[..8];

            var billing = new Billing
            {
                MsapBillingNumber = billingNo,
                Date = today,
                Status = MsapConstants.BillingStatus.Reviewed,
                Revision = 1,
                ReviewedRevision = 1,
                ReviewedByUserId = actor.Id,
                ReviewedAt = DateTime.Now,
                BilledTo = MsapConstants.BilledToLocal,
                CustomerId = customer.CustomerId,
                VesselId = vessel.VesselId,
                PortId = port.PortId,
                TerminalId = terminal.TerminalId,
                COSNumber = "COS-CHK-" + Guid.NewGuid().ToString("N")[..6],
                Amount = 11200m,
                Balance = 11200m,
                DispatchAmount = 11200m,
                IsVatable = true,
                DueDate = today,
                Year = today.Year,
                Company = MsapConstants.Company_MMSI,
                AgentCommissionRepresentativeId = rep.RepresentativeId,
                AgentCommissionName = rep.RepresentativeName,
                AgentCommissionMode = CommissionCalculator.ModePercentage,
                AgentCommissionBase = CommissionCalculator.BaseGross,
                AgentCommissionRate = 10m,
                AgentCommissionAmount = 1120m,
                CreatedBy = actor.UserName ?? "check",
                CreatedDate = DateTime.Now
            };
            source.MsapBillings.Add(billing);
            await source.SaveChangesAsync();

            var msapWork = new IBS.DataAccess.MSAP.Repository.UnitOfWork(source, db);
            var cosService = new CustomerOrderSlipService(msapWork, NullLogger<CustomerOrderSlipService>.Instance);
            var billingService = new BillingService(msapWork, cosService, NullLogger<BillingService>.Instance, new AllAccess());

            var approveResult = await billingService.PostBillingAsync(billing.MsapBillingId, actor.UserName ?? "check", default, actor.Id, 1);
            Check(approveResult.IsSuccess, approveResult.Message ?? "Billing approval failed.");

            var recognizedGl = await db.FilprideGeneralLedgerBooks.AsNoTracking()
                .Where(g => g.Reference == billingNo && g.CounterpartyType == CounterpartyType.Representative)
                .ToListAsync();
            Check(recognizedGl.Count == 3
                && recognizedGl.Single(g => g.AccountNo == MsapConstants.CommissionExpenseAccount).Debit == 1000m
                && recognizedGl.Single(g => g.AccountNo == MsapConstants.InputVatAccount).Debit == 120m
                && recognizedGl.Single(g => g.AccountNo == MsapCommissionPaymentService.ApAccount).Credit == 1120m,
                "MSAP billing approval GL recognition did not match 1000 Expense + 120 Input VAT = 1120 AP.");

            // Idempotency check
            await msapWork.Billing.RecognizeCommissionLiabilityAsync(billing, actor.UserName ?? "check", default);
            Check(await db.FilprideGeneralLedgerBooks.AsNoTracking()
                .CountAsync(g => g.Reference == billingNo && g.CounterpartyType == CounterpartyType.Representative) == 3,
                "Repeated liability recognition duplicated GL entries.");

            var storage = new Storage();
            var paymentService = Service(db, storage);

            var repComm = await paymentService.GetRepresentativeCommissionsAsync(rep.RepresentativeId, null, default);
            Check(repComm.ProfileError == null && repComm.WithholdingAccountNumber == "201030220"
                && repComm.Items.Count == 1
                && repComm.Items[0].GrossAmount == 1120m && repComm.Items[0].WithholdingAmount == 20m && repComm.Items[0].NetAmount == 1100m,
                "GetRepresentativeCommissionsAsync returned unexpected items or tax amounts.");

            var bank = await db.FilprideBankAccounts.FirstAsync(b => b.IsActive);
            var form = new CommissionPaymentViewModel
            {
                Source = CommissionPaymentViewModel.SourceMsapBilling,
                RepresentativeId = rep.RepresentativeId,
                Payee = rep.RepresentativeName,
                SupplierAddress = rep.Address!,
                SupplierTinNo = rep.Tin!,
                TransactionDate = today,
                BankId = bank.BankAccountId,
                CheckNo = "MC" + Guid.NewGuid().ToString("N")[..10],
                CheckDate = today,
                Particulars = "MSAP Agent Commission settlement check",
                OldCVNo = "LEGACY-MSAP-COMM",
                Type = nameof(DocumentType.Documented),
                MsapItems = [new MsapCommissionItemInput { BillingId = billing.MsapBillingId, Category = CommissionCalculator.CategoryAgentCommission }],
                AccountNumber = [MsapCommissionPaymentService.ApAccount, MsapCommissionPaymentService.CashAccount],
                AccountTitle = ["AP - Commision Payable", "Cash in Bank"],
                Debit = [1120m, 0m],
                Credit = [0m, 1100m]
            };

            int cvId = await paymentService.CreateAsync(form, null, actor.UserName ?? "check", default);

            // Concurrent / duplicate reservation must be blocked
            await RejectAsync(() => paymentService.CreateAsync(form, null, actor.UserName ?? "check", default),
                "Duplicate MSAP commission reservation was allowed.");

            // Reserved billing must no longer show up
            var afterReserve = await paymentService.GetRepresentativeCommissionsAsync(rep.RepresentativeId, null, default);
            Check(afterReserve.Items.Count == 0, "Reserved MSAP commission item remained selectable.");

            // Reopen must be blocked while active CV exists
            var blockedReopen = await billingService.ReopenBillingForRevisionAsync(billing.MsapBillingId, "Test reopen block", actor.UserName ?? "check", actor.Id, default);
            Check(!blockedReopen.IsSuccess, "ReopenBillingForRevisionAsync succeeded while active Check Voucher existed.");

            // DB trigger must allow AR collection status updates while blocking commission edits
            await using (var isolated = Open())
            {
                int rowsUpdated = await isolated.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE msap.mmsi_billings SET status = {MsapConstants.BillingStatus.Collected}, amount_paid = {11200m}, balance = {0m}, is_paid = {true} WHERE \"RECID\" = {billing.MsapBillingId}");
                Check(rowsUpdated == 1, "DB trigger unexpectedly blocked customer AR collection update.");

                bool tamperedBlocked = false;
                try
                {
                    await isolated.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE msap.mmsi_billings SET agent_commission_amount = {999m} WHERE \"RECID\" = {billing.MsapBillingId}");
                }
                catch (PostgresException ex) when (ex.SqlState == "P0001")
                {
                    tamperedBlocked = true;
                }
                Check(tamperedBlocked, "DB trigger failed to block commission mutation while active CV existed.");
            }

            // Edit draft
            form.CvId = cvId;
            form.Particulars = "Edited MSAP Agent Commission settlement check";
            await paymentService.EditAsync(form, null, actor.UserName ?? "check", default);

            // Post, Rebuild GL, Unpost, Post, Void via CheckVoucherTradeController
            var trade = new CheckVoucherTradeController(
                new UnitOfWork(db),
                null!,
                db,
                storage,
                NullLogger<CheckVoucherTradeController>.Instance,
                new SubAccountResolver(db),
                new CheckVoucherDocumentationService(db),
                new TugboatPaymentService(db, new UnitOfWork(db), new CheckVoucherDocumentationService(db), storage, NullLogger<TugboatPaymentService>.Instance),
                paymentService);
            trade.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.GivenName, "Commission Check")], "Test"))
                }
            };
            trade.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                trade.ControllerContext.HttpContext,
                new TempDataProvider());

            await trade.Post(cvId, null, default);
            var postedCv = await db.FilprideCheckVoucherHeaders.SingleAsync(v => v.CheckVoucherHeaderId == cvId);
            Check(postedCv.PostedBy != null, "MSAP commission CV failed to post.");

            var cvGl = await db.FilprideGeneralLedgerBooks.AsNoTracking()
                .Where(g => g.Reference == postedCv.CheckVoucherHeaderNo)
                .ToListAsync();
            Check(cvGl.Count == 3
                && cvGl.Sum(g => g.Debit) == 1120m
                && cvGl.Sum(g => g.Credit) == 1120m
                && cvGl.All(g => g.CounterpartyType == CounterpartyType.Representative && g.CounterpartyId == rep.RepresentativeId),
                "Posted CV GL entries unbalanced or missing Representative counterparty.");

            await new UnitOfWork(db).FilprideCheckVoucher.RebuildGeneralLedgerAsync(today, today.AddDays(1));
            var rebuiltGl = await db.FilprideGeneralLedgerBooks.AsNoTracking()
                .Where(g => g.Reference == postedCv.CheckVoucherHeaderNo)
                .ToListAsync();
            Check(rebuiltGl.Count == 3 && rebuiltGl.Sum(g => g.Debit) == 1120m && rebuiltGl.Sum(g => g.Credit) == 1120m
                && rebuiltGl.All(g => g.CounterpartyType == CounterpartyType.Representative && g.CounterpartyId == rep.RepresentativeId),
                "RebuildGeneralLedgerAsync lost MSAP commission CV entries or counterparty.");

            await trade.Unpost(cvId, default);
            Check(await db.FilprideMsapCommissionPayments.AnyAsync(p => p.CheckVoucherHeaderId == cvId && p.ReleasedAt == null),
                "Unposting unexpectedly released reservation.");

            await trade.Post(cvId, null, default);
            await trade.Void(cvId, default);
            Check(await db.FilprideMsapCommissionPayments.AllAsync(p => p.CheckVoucherHeaderId == cvId && p.ReleasedAt != null),
                "Voiding did not release MSAP commission reservation.");

            // After voiding, item becomes selectable again
            var afterVoid = await paymentService.GetRepresentativeCommissionsAsync(rep.RepresentativeId, null, default);
            Check(afterVoid.Items.Count == 1, "Voided MSAP commission item did not become available again.");

            // Verify active CV lookup clears after voiding and GL reversal removes recognized entries
            Check(await msapWork.Billing.GetActiveCommissionVoucherNumberAsync(billing.MsapBillingId, default) == null,
                "Voided CV still reported as active commission voucher.");
            await msapWork.Billing.ReverseCommissionLiabilityAsync(billing, default);
            Check(!await db.FilprideGeneralLedgerBooks.AsNoTracking()
                .AnyAsync(g => g.Reference == billingNo && g.CounterpartyType == CounterpartyType.Representative),
                "ReverseCommissionLiabilityAsync did not remove MSAP commission GL entries.");

            Console.WriteLine($"PASS: MSAP Commission payment profile, migration upgrade/rollback, GL recognition, trigger/reopen guards, CV create/edit/post/rebuild/unpost/void, and GL reversal verified on clone.");

            ApplicationDbContext Open() => new(options);
        }

        private static MsapCommissionPaymentService Service(ApplicationDbContext db, Storage storage)
        {
            return new MsapCommissionPaymentService(
                db,
                new UnitOfWork(db),
                new CheckVoucherDocumentationService(db),
                storage,
                NullLogger<MsapCommissionPaymentService>.Instance);
        }

        private static async Task RejectAsync(Func<Task> action, string message)
        {
            try
            {
                await action();
            }
            catch (InvalidOperationException) { return; }
            catch (ArgumentException) { return; }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { return; }
            throw new InvalidOperationException(message);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class AllAccess : IAccessControlService
        {
            public Task<bool> HasAccessAsync(string userId, params ProcedureEnum[] procedures) => Task.FromResult(true);
            public Task<bool> HasAnyAccessAsync(string userId, params ProcedureEnum[] procedures) => Task.FromResult(true);
        }

        private sealed class TempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private sealed class Storage : IBS.Services.ICloudStorageService
        {
            public Task<string> UploadFileAsync(IFormFile fileToUpload, string fileNameToSave) => Task.FromResult("fixture://comm");
            public Task DeleteFileAsync(string fileNameToDelete) => Task.CompletedTask;
            public Task<string> GetSignedUrlAsync(string fileNameToRead, int timeOutInMinutes = 30) => Task.FromResult("fixture://comm");
            public Task<Stream> DownloadFileAsync(string fileNameToDownload) => throw new NotSupportedException();
            public Task<IFormFile?> GetFileAsFormFile(string fileName) => Task.FromResult<IFormFile?>(null);
        }
    }
}
