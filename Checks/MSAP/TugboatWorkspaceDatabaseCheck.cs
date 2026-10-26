using System.Diagnostics;
using System.Reflection;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.Repository;
using IBS.Models;
using IBS.Models.Enums;
using IBS.Models.Filpride;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.MasterFile;
using IBS.Services;
using IBS.Utility;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkspaceAssertions = Checks.MSAP.TugboatWorkspaceCheck;

namespace Checks.MSAP
{
    internal static class TugboatWorkspaceDatabaseCheck
    {
        public static async Task<string> RunAsync()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("IBSWeb"))
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
                .AddUserSecrets(Assembly.Load("IBSWeb")).AddEnvironmentVariables().Build();
            var source = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("MMSIConnection"));
            WorkspaceAssertions.Check(source.Host is "localhost" or "127.0.0.1", "Workspace integration checks require a local source database.");
            var target = new NpgsqlConnectionStringBuilder(source.ConnectionString)
            {
                Database = "ibs_workspace_check_" + Guid.NewGuid().ToString("N")[..12],
                Pooling = false
            };
            var serverSettings = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres", Pooling = false };
            await using var server = new NpgsqlConnection(serverSettings.ConnectionString);
            await server.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{target.Database}\"", server))
            {
                await create.ExecuteNonQueryAsync();
            }
            Console.WriteLine($"Workspace test platform: {target.Database} (retained for inspection; source is read-only).");
            string dump = Path.Combine(Path.GetTempPath(), target.Database + ".dump");
            try
            {
                await RunPostgresToolAsync("pg_dump", source, ["--format=custom", "--file", dump]);
                await RunPostgresToolAsync("pg_restore", target, ["--no-owner", "--no-privileges", "--dbname", target.Database!, dump]);
            }
            finally
            {
                if (File.Exists(dump))
                {
                    File.Delete(dump);
                }
            }

            var workspace = new TugboatWorkspace();
            await using var services = new ServiceCollection().AddLogging().AddSingleton(workspace)
                .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(target.ConnectionString))
                .AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                .Services.BuildServiceProvider();
            await using var db = services.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            WorkspaceAssertions.Check(!db.Database.HasPendingModelChanges(), "Migrated workspace schema does not match the model.");
            string[] migrations = db.Database.GetMigrations().ToArray();
            try
            {
                await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            }
            catch (PostgresException ex) when (ex.SqlState == "P0001" && ex.MessageText.Contains("selected-ticket vouchers", StringComparison.Ordinal))
            {
                WorkspaceAssertions.Check((await db.Database.GetAppliedMigrationsAsync()).Contains(migrations[^1]),
                    "Rejected selected-ticket rollback removed the migration history.");
            }
            await db.Database.MigrateAsync();
            WorkspaceAssertions.Check(!db.Database.HasPendingModelChanges(), "Rollback/reapply changed the model.");
            await using var msap = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(target.ConnectionString, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options);
            var selector = new TugboatWorkspaceService(msap, new EphemeralDataProtectionProvider());
            int[] tugs = (await selector.GetOptionsAsync(default)).Take(2).Select(x => int.Parse(x.Value)).ToArray();
            WorkspaceAssertions.Check(tugs.Length == 2, "Import at least two owned tugboats before cloning the test platform.");
            int? external = await msap.MsapTugboats.Where(t => !t.IsCompanyOwned).Select(t => (int?)t.TugboatId).FirstOrDefaultAsync();
            if (external.HasValue)
            {
                WorkspaceAssertions.Check(await selector.GetNameAsync(external, default) == null, "An external tugboat can be selected as an owned workspace.");
            }

            var expense = new FilprideChartOfAccount
            {
                AccountNumber = "5W" + Guid.NewGuid().ToString("N")[..8], AccountName = "Workspace check expense",
                Level = 4, NormalBalance = "Debit", FinancialStatementType = "PnL"
            };
            var cash = new FilprideChartOfAccount
            {
                AccountNumber = "1W" + Guid.NewGuid().ToString("N")[..8], AccountName = "Workspace check cash",
                Level = 4, NormalBalance = "Debit", FinancialStatementType = "BalanceSheet"
            };
            db.AddRange(expense, cash);
            await db.SaveChangesAsync();
            var work = new UnitOfWork(db);
            var firstMonth = new DateOnly(2035, 1, 1);
            var first = await CreateVoucherAsync(tugs[0], 100m, firstMonth);
            var second = await CreateVoucherAsync(tugs[1], 200m, firstMonth);
            var company = await CreateVoucherAsync(null, 50m, firstMonth);
            string[] references = [first.CheckVoucherHeaderNo!, second.CheckVoucherHeaderNo!, company.CheckVoucherHeaderNo!];

            workspace.Select(tugs[0], "First tugboat");
            db.ChangeTracker.Clear();
            WorkspaceAssertions.Check(await db.FilprideCheckVoucherHeaders.CountAsync(v => references.Contains(v.CheckVoucherHeaderNo!)) == 1,
                "Source list leaked another workspace.");
            WorkspaceAssertions.Check(!await db.FilprideCheckVoucherHeaders.AnyAsync(v => v.CheckVoucherHeaderId == second.CheckVoucherHeaderId),
                "A direct-ID lookup leaked another workspace.");
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.Where(x => references.Contains(x.Reference) && x.AccountId == expense.AccountId)
                .SumAsync(x => x.Debit) == 100m, "GL query leaked company or another tugboat.");
            WorkspaceAssertions.Check(await work.FilprideCheckVoucher.GenerateCodeAsync("Documented") != company.CheckVoucherHeaderNo,
                "Document numbering ignored another workspace's latest voucher.");

            // FindAsync can return an attached object; write guards must check persisted ownership.
            var wrong = await db.FilprideCheckVoucherHeaders.IgnoreQueryFilters([TugboatWorkspace.FilterName])
                .SingleAsync(x => x.CheckVoucherHeaderId == second.CheckVoucherHeaderId);
            wrong!.Particulars = "Attempted cross-workspace update";
            await ExpectRejectedAsync(() => db.SaveChangesAsync(), "Cross-workspace update was accepted.");
            db.ChangeTracker.Clear();
            var owned = await db.FilprideCheckVoucherHeaders.SingleAsync(x => x.CheckVoucherHeaderId == first.CheckVoucherHeaderId);
            owned.WorkspaceTugboatId = tugs[1];
            owned.Particulars = "Allowed description edit";
            await db.SaveChangesAsync();
            WorkspaceAssertions.Check(owned.WorkspaceTugboatId == tugs[0], "Editing moved an existing voucher.");
            var mixed = new FilprideJournalVoucherHeader
            {
                CVId = second.CheckVoucherHeaderId, Particulars = "Mixed workspace", JVReason = "Check"
            };
            db.Add(mixed);
            await ExpectRejectedAsync(() => db.SaveChangesAsync(), "Cross-workspace source relationship was accepted.");
            db.ChangeTracker.Clear();

            workspace.Select(null, "MMSI");
            await work.FilprideCheckVoucher.RebuildGeneralLedgerAsync(firstMonth, firstMonth.AddMonths(1));
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.Where(x => references.Contains(x.Reference) && x.AccountId == expense.AccountId)
                .SumAsync(x => x.Debit) == 350m, "Consolidated rebuild changed or lost tugboat activity.");
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.Where(x => x.Reference == first.CheckVoucherHeaderNo)
                .AllAsync(x => x.WorkspaceTugboatId == tugs[0]), "Consolidated rebuild reassigned source ownership.");
            workspace.Select(tugs[0], "First tugboat");
            await work.FilprideCheckVoucher.RebuildGeneralLedgerAsync(firstMonth, firstMonth.AddMonths(1));
            workspace.Select(null, "MMSI");
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.Where(x => x.Reference == second.CheckVoucherHeaderNo).CountAsync() == 2,
                "Scoped rebuild deleted another tugboat's entries.");

            var close = new MonthlyClosureService(db, NullLogger<MonthlyClosureService>.Instance, work, workspace);
            foreach (int month in new[] { 1, 2 })
            {
                db.PostedPeriods.Add(new PostedPeriod { Month = month, Year = 2035, IsPosted = true, Module = "CheckVoucher", PostedBy = "Workspace check" });
            }
            await db.SaveChangesAsync();
            await close.CloseAsync(firstMonth, "Workspace check");
            WorkspaceAssertions.Check(await db.FilprideGlPeriodBalances.Where(x => x.AccountId == expense.AccountId && x.PeriodStartDate == firstMonth)
                .Select(x => x.EndingBalance).SingleAsync() == 350m, "Consolidated snapshot double-counted tugboat snapshots.");
            workspace.Select(tugs[0], "First tugboat");
            WorkspaceAssertions.Check(await db.FilprideGlPeriodBalances.Where(x => x.AccountId == expense.AccountId && x.PeriodStartDate == firstMonth)
                .Select(x => x.EndingBalance).SingleAsync() == 100m, "Tugboat snapshot includes company activity.");
            await ExpectRejectedAsync(() => close.CloseAsync(firstMonth, "Check"), "A tugboat workspace can close the company period.");
            await ExpectRejectedAsync(() => close.OpenAsync(firstMonth, "Check"), "A tugboat workspace can reopen the company period.");
            var february = await CreateVoucherAsync(tugs[0], 25m, firstMonth.AddMonths(1));
            workspace.Select(null, "MMSI");
            await close.CloseAsync(firstMonth.AddMonths(1), "Workspace check");
            workspace.Select(tugs[0], "First tugboat");
            WorkspaceAssertions.Check(await db.FilprideGlPeriodBalances.Where(x => x.AccountId == expense.AccountId && x.PeriodStartDate == firstMonth.AddMonths(1))
                .Select(x => x.BeginningBalance).SingleAsync() == 100m, "Balance carry-forward crossed workspaces.");
            workspace.Select(null, "MMSI");
            await close.OpenAsync(firstMonth, "Workspace check");
            WorkspaceAssertions.Check(!await db.FilprideGlPeriodBalances.IgnoreQueryFilters([TugboatWorkspace.BalanceFilterName])
                .AnyAsync(x => x.IsValid && x.PeriodStartDate >= firstMonth), "Reopening left valid tugboat snapshots.");
            WorkspaceAssertions.Check(!await db.FilprideGlSubAccountBalances.IgnoreQueryFilters([TugboatWorkspace.BalanceFilterName])
                .AnyAsync(x => x.IsValid && x.PeriodStartDate >= firstMonth), "Reopening left valid tugboat subaccount snapshots.");
            WorkspaceAssertions.Check(!await db.FilprideMonthlyNibits.IgnoreQueryFilters([TugboatWorkspace.BalanceFilterName])
                .AnyAsync(x => x.IsValid && x.Year >= 2035), "Reopening left valid tugboat income snapshots.");

            workspace.Select(tugs[0], "First tugboat");
            await work.GeneralLedger.ReverseEntries(february.CheckVoucherHeaderNo);
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.Where(x => x.Reference == february.CheckVoucherHeaderNo)
                .AllAsync(x => x.WorkspaceTugboatId == tugs[0]), "Reversal lost workspace ownership.");
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.Where(x => x.Reference == february.CheckVoucherHeaderNo)
                .SumAsync(x => x.Debit - x.Credit) == 0m, "Reversal is unbalanced.");

            string jobReference = "Workspace recurring " + Guid.NewGuid().ToString("N")[..8];
            string journalCode = await work.FilprideJournalVoucher.GenerateCodeAsync("Documented");
            var sourceJournal = new FilprideJournalVoucherHeader
            {
                JournalVoucherHeaderNo = journalCode,
                Particulars = jobReference, JVReason = "Workspace check", Payee = "Workspace check", Type = "Documented", PostedBy = "Workspace check", CreatedBy = "Workspace check",
                Date = firstMonth, AutoReverseNextMonth = true, Details =
                [
                    new FilprideJournalVoucherDetail { AccountNo = expense.AccountNumber, AccountName = expense.AccountName, Debit = 10m, TransactionNo = journalCode },
                    new FilprideJournalVoucherDetail { AccountNo = cash.AccountNumber, AccountName = cash.AccountName, Credit = 10m, TransactionNo = journalCode }
                ]
            };
            db.Add(sourceJournal);
            await db.SaveChangesAsync();
            db.JvAmortizationSettings.Add(new JvAmortizationSetting
            {
                JvHeader = sourceJournal, IsActive = true, OccurrenceTotal = 1, OccurrenceRemaining = 1,
                StartDate = DateOnly.FromDateTime(DateTime.Today), EndDate = DateOnly.FromDateTime(DateTime.Today)
            });
            await db.SaveChangesAsync();
            workspace.Select(null, "MMSI");
            await close.CloseAsync(firstMonth, "Workspace check");
            WorkspaceAssertions.Check(await db.FilprideJournalVoucherHeaders
                .Where(x => x.Particulars == jobReference && x.CreatedBy == "SYSTEM GENERATED")
                .AllAsync(x => x.WorkspaceTugboatId == tugs[0]), "Recurring journal lost its source ownership.");
            WorkspaceAssertions.Check(await db.FilprideJournalVoucherHeaders.CountAsync(x => x.Particulars == jobReference) == 2,
                "Monthly closure did not create its recurring journal.");
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.CountAsync(x => x.Reference == sourceJournal.JournalVoucherHeaderNo
                && x.Date == firstMonth.AddMonths(1)) == 2, "Monthly closure did not create the next-period reversal.");
            WorkspaceAssertions.Check(await db.FilprideGeneralLedgerBooks.Where(x => x.Reference == sourceJournal.JournalVoucherHeaderNo)
                .AllAsync(x => x.WorkspaceTugboatId == tugs[0]), "Automatic journal reversal lost its source ownership.");

            workspace.Select(null, "MMSI");
            Console.WriteLine("PASS: cloned local data, source/detail isolation, shared numbering, write guards, posting/rebuild/reversal, company/tugboat reconciliation, close/reopen, balance carry-forward and migration rollback/reapply.");
            Console.WriteLine($"Retained test database: {target.Database}");
            return target.Database!;

            async Task<FilprideCheckVoucherHeader> CreateVoucherAsync(int? tugboatId, decimal amount, DateOnly date)
            {
                workspace.Select(tugboatId, tugboatId.HasValue ? "Check tugboat" : "MMSI");
                string code = await work.FilprideCheckVoucher.GenerateCodeAsync("Documented");
                var voucher = new FilprideCheckVoucherHeader
                {
                    WorkspaceTugboatId = -1, CheckVoucherHeaderNo = code, Category = "Trade", Type = "Documented",
                    Date = date, Particulars = "Workspace fixture", Payee = "Workspace fixture", Address = "Check", Tin = "Check",
                    VatType = "Exempt", TaxType = "Exempt", Details =
                    [
                        new FilprideCheckVoucherDetail { TransactionNo = code, AccountNo = expense.AccountNumber, AccountName = expense.AccountName, Debit = amount, SubAccountType = SubAccountType.Tugboat, SubAccountId = 777, SubAccountName = "Fixture subaccount" },
                        new FilprideCheckVoucherDetail { TransactionNo = code, AccountNo = cash.AccountNumber, AccountName = cash.AccountName, Credit = amount }
                    ]
                };
                if (!tugboatId.HasValue)
                {
                    voucher.WorkspaceTugboatId = null;
                }
                db.Add(voucher);
                await db.SaveChangesAsync();
                WorkspaceAssertions.Check(voucher.WorkspaceTugboatId == tugboatId, "New source ownership was not assigned by the server.");
                voucher.PostedBy = "Workspace check";
                await work.FilprideCheckVoucher.PostAsync(voucher, voucher.Details);
                return voucher;
            }
        }

        private static async Task ExpectRejectedAsync(Func<Task> action, string message)
        {
            try
            {
                await action();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        private static async Task RunPostgresToolAsync(string name, NpgsqlConnectionStringBuilder settings, string[] arguments)
        {
            var start = new ProcessStartInfo(name) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            start.Environment["PGHOST"] = settings.Host;
            start.Environment["PGPORT"] = settings.Port.ToString();
            start.Environment["PGUSER"] = settings.Username;
            start.Environment["PGPASSWORD"] = settings.Password;
            start.Environment["PGDATABASE"] = settings.Database;
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {name}.");
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            WorkspaceAssertions.Check(process.ExitCode == 0, $"{name} failed: {error}");
        }
    }
}
