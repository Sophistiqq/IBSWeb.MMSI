using System.Security.Claims;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.Models;
using IBS.Models.Filpride.Books;
using IBS.Models.Filpride.MasterFile;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Checks.MSAP
{
    internal static class DatabaseCheck
    {
        public static async Task RunAsync(ICloudStorageService storage, ITempDataDictionaryFactory tempData, bool approvalsOnly = false,
            bool customerDiscountOnly = false)
        {
            var sourceConfig = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("../MSAP/IBSWeb"))
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true).Build();
            var connection = Environment.GetEnvironmentVariable("MSAP_CHECK_SERVER_CONNECTION")
                ?? sourceConfig.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Set MSAP_CHECK_SERVER_CONNECTION for the isolated database check.");
            var settings = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres", Pooling = false };
            await using var server = new NpgsqlConnection(settings.ConnectionString);
            await server.OpenAsync();
            var database = "msap_isolation_check_" + Guid.NewGuid().ToString("N");
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", server))
            {
                await create.ExecuteNonQueryAsync();
            }

            try
            {
                settings.Database = database;
                using var migrationServices = new ServiceCollection().AddLogging()
                    .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(settings.ConnectionString))
                    .AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                    .Services.BuildServiceProvider();
                await using var original = migrationServices.GetRequiredService<ApplicationDbContext>();
                await original.Database.MigrateAsync();
                var originalAudit = new FilprideAuditTrail("check", "Base entry with overlapping reference", "Job Order");
                original.FilprideAuditTrails.Add(originalAudit);
                original.Users.Add(new ApplicationUser { Id = "shared-check", UserName = "shared-check", Name = "Shared User", Department = "MIS" });
                original.FilprideTerms.AddRange(
                    new FilprideTerms { TermsCode = "COD" },
                    new FilprideTerms { TermsCode = "CHECK17", NumberOfDays = 17 },
                    new FilprideTerms { TermsCode = "M15", NumberOfDays = 15, NumberOfMonths = 1 });
                await original.SaveChangesAsync();
                var before = await SchemaAsync(settings.ConnectionString);
                await using var module = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                    .UseNpgsql(settings.ConnectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options);
                await module.Database.MigrateAsync(module.Database.GetMigrations().First());
                await module.Database.ExecuteSqlRawAsync("INSERT INTO msap.msap_ports (port_number, port_name, has_sbma) VALUES ('ZZZ', 'Prefix migration check', false)");
                await module.Database.MigrateAsync();
                Check(await module.MsapPorts.AnyAsync(port => port.PortNumber == "ZZZ" && port.PortName == "Prefix migration check"),
                    "Table prefix migration lost existing data.");
                Console.WriteLine("PASS: mmsi table renames preserve existing MSAP records.");
                var migrationAccess = new UserAccess
                {
                    UserId = "approval-migration-check",
                    CanCreateJobOrder = true,
                    CanApproveSchedule = true,
                    CanApproveRateUpload = true,
                    CanApproveFinalBilling = true
                };
                module.MsapUserAccesses.Add(migrationAccess);
                await module.SaveChangesAsync();
                IMigrator migrator = module.GetService<IMigrator>();
                await migrator.MigrateAsync("20261012011514_AddCollectionCheckReturn");
                await migrator.MigrateAsync();
                await module.Entry(migrationAccess).ReloadAsync();
                Check(migrationAccess.CanCreateJobOrder && !migrationAccess.CanApproveSchedule
                    && !migrationAccess.CanApproveRateUpload && !migrationAccess.CanApproveFinalBilling,
                    "Approval migration changed existing grants or enabled approval by default.");
                module.MsapUserAccesses.Remove(migrationAccess);
                await module.SaveChangesAsync();
                Console.WriteLine("PASS: approval migration upgrade/downgrade preserves existing grants and defaults all new approvals to false.");
                using var seedServices = new ServiceCollection().AddSingleton(module).BuildServiceProvider();
                await DbSeeder.SeedAsync(seedServices);
                await DbSeeder.SeedAsync(seedServices);
                Check(await original.Users.CountAsync() == 1 && await original.Roles.CountAsync() == 0, "MSAP seeding changed shared accounts or roles.");
                Check(await SchemaAsync(settings.ConnectionString) == before, "MSAP migration changed public schema.");
                Check(await module.Users.AnyAsync(u => u.Id == "shared-check"), "MSAP cannot read the existing user directory.");

                using var identityServices = new ServiceCollection().AddLogging().AddSingleton(original)
                    .AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                    .Services.BuildServiceProvider();
                var users = identityServices.GetRequiredService<UserManager<ApplicationUser>>();
                var dashboardWork = new UnitOfWork(module, original);
                Check(!await module.Terms.AnyAsync(), "MSAP seeded an independent payment terms list.");
                List<SelectListItem> paymentTerms = await dashboardWork.Terms.GetFilprideTermsListAsyncByCode();
                Check(paymentTerms.Select(term => term.Value).SequenceEqual(new[] { "CHECK17", "COD", "M15" }),
                    "MSAP term selection does not use Filpride terms.");
                Check(await dashboardWork.Terms.ComputeDueDateAsync("M15", new DateOnly(2026, 10, 6)) == new DateOnly(2026, 11, 15),
                    "Shared month-based due-date calculation changed.");
                await RoleCheck.RunAsync(users, identityServices.GetRequiredService<RoleManager<IdentityRole>>(), dashboardWork);
                if (customerDiscountOnly)
                {
                    await CustomerDiscountCheck.RunAsync(module, dashboardWork, users, tempData);
                    return;
                }
                if (approvalsOnly)
                {
                    await ApprovalEvidenceCheck.RunAsync(module);
                    return;
                }
                var roles = new RoleService(users);
                var access = new AccessControlService(users, new UserAccessService(dashboardWork, users, roles, NullLogger<UserAccessService>.Instance));
                var dashboard = new IBSWeb.Areas.MSAP.Controllers.HomeController(users, module, access,
                    new VesselScheduleService(dashboardWork, new JobOrderService(dashboardWork, NullLogger<JobOrderService>.Instance), NullLogger<VesselScheduleService>.Instance),
                    NullLogger<IBSWeb.Areas.MSAP.Controllers.HomeController>.Instance);
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "shared-check"), new Claim("Company", "Filpride")], "check");
                dashboard.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
                var dashboardModel = (DashboardCountViewModel)((ViewResult)await dashboard.Index(default)).Model!;
                Check(dashboardModel.ShowDashboard && dashboardModel.DataError == null && dashboardModel.ScheduleError == null,
                    "Host company claim hides the MSAP dashboard or its empty states fail.");
                Check(!dashboardModel.CanViewFinance && !dashboardModel.CanCreateBilling, "Dashboard bypassed MSAP permissions.");
                identity.AddClaim(new Claim(ClaimTypes.Role, "PortCoordinator"));
                Check(((DashboardCountViewModel)((ViewResult)await dashboard.Index(default)).Model!).ShowDashboard,
                    "Filpride PortCoordinator role hides the MSAP dashboard.");
                Console.WriteLine("PASS: shared login opens MSAP dashboard; PortCoordinator does not hide it.");

                var customer = new Customer { CustomerCode = "CHECK01", CustomerName = "Check Customer", CustomerAddress = "Check Address", CustomerTin = "000-000-000-00000", CustomerTerms = "CHECK17", CustomerType = "Regular", VatType = "Vatable", ZipCode = "1000", Company = "MMSI" };
                var vessel = new Vessel { VesselNumber = "0001", VesselName = "Check Vessel", VesselType = "Cargo" };
                var port = new Port { PortNumber = "001", PortName = "Check Port" };
                var terminal = new Terminal { TerminalNumber = "001", TerminalName = "Check Terminal", Port = port };
                var tug = new Tugboat { TugboatNumber = "001", TugboatName = "Check Tug", Port = port, IsCompanyOwned = true };
                var maritimeService = new Service { ServiceNumber = "001", ServiceName = "Check Service" };
                module.AddRange(customer, vessel, port, terminal, tug, maritimeService);
                await module.SaveChangesAsync();
                var job = new JobOrder { Date = new DateOnly(2026, 10, 6), CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId };
                var work = new UnitOfWork(module, original);
                var service = new JobOrderService(work, NullLogger<JobOrderService>.Instance);
                var result = await service.CreateJobOrderAsync(job, "check", default);
                Check(result.IsSuccess, result.Message ?? "Job Order creation failed.");
                module.AuditTrails.Add(new AuditTrail("check", "Different document with same ID", "Billing", job.JobOrderId, "BILL-CHECK"));
                await module.SaveChangesAsync();
                var audits = new AuditTrailService(work);
                var own = (await audits.GetAuditTrailsByEntityAsync("Job Order", job.JobOrderId, default)).ToList();
                Check(own.Count == 1 && own[0].RecordId == job.JobOrderId && own[0].ReferenceNumber == job.JobOrderNumber, "Job Order audit lost its ID or reference.");
                var timeline = (await audits.GetJobOrderTimelineAsync(job.JobOrderId, default)).ToList();
                Check(timeline.Count == 1 && timeline[0].DocumentType == "Job Order", "MSAP timeline mixed records with overlapping IDs.");

                var dispatchService = new DispatchTicketService(work, storage, NullLogger<DispatchTicketService>.Instance);
                var dispatch = await dispatchService.CreateDispatchTicketAsync(new DispatchTicketViewModel
                {
                    JobOrderId = job.JobOrderId, DispatchNumber = "D-CHECK", TugBoatId = tug.TugboatId, ServiceId = maritimeService.ServiceId,
                    DateLeft = job.Date, TimeLeft = new TimeOnly(8, 0), DateArrived = job.Date, TimeArrived = new TimeOnly(9, 0)
                }, null, null, "check", default);
                Check(dispatch.IsSuccess, dispatch.Message ?? "Dispatch creation failed.");
                var ticket = await module.MsapDispatchTickets.SingleAsync();
                Check((await audits.GetAuditTrailsByEntityAsync("Dispatch Ticket", ticket.DispatchTicketId, default)).Count() == 1, "Dispatch creation audit lost its ID.");
                ticket.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                ticket.DispatchNetRevenue = ticket.TotalNetRevenue = 100m;
                await module.SaveChangesAsync();
                var billingService = new BillingService(work, service, NullLogger<BillingService>.Instance);
                var bill = new Billing
                {
                    JobOrderId = job.JobOrderId, Date = job.Date, CustomerId = customer.CustomerId, BilledTo = "CHECK01",
                    IsUndocumented = true, IsVatable = true, IsVatInclusive = false,
                    ToBillDispatchTickets = [ticket.DispatchTicketId.ToString()]
                };
                var createdBill = await billingService.CreateBillingAsync(bill, "check", "MMSI", default);
                Check(createdBill.IsSuccess, createdBill.Message ?? "Billing creation failed.");
                Check(bill.Terms == "CHECK17" && bill.DueDate == job.Date.AddDays(17),
                    "MSAP billing did not use the shared Filpride term with an empty local terms table.");
                Check(bill.Amount == 112m && bill.Balance == 112m, "VAT billing totals changed.");
                Check((await audits.GetAuditTrailsByEntityAsync("Billing", bill.MsapBillingId, default)).Any(a => a.ReferenceNumber == bill.MsapBillingNumber), "Billing creation audit lost its ID.");
                var posted = await billingService.PostBillingAsync(bill.MsapBillingId, "check", default);
                Check(posted.IsSuccess, posted.Message ?? "Billing posting failed.");
                var collectionService = new CollectionService(work, NullLogger<CollectionService>.Instance);
                var collection = await collectionService.CreateCollectionAsync(new CreateCollectionViewModel
                {
                    Date = job.Date, CustomerId = customer.CustomerId, MsapCollectionNumber = "C-CHECK", Amount = 112m, CashAmount = 112m,
                    BillingPayments = [new BillingPaymentViewModel { BillingId = bill.MsapBillingId, AmountToPay = 112m }]
                }, "check", default);
                Check(collection.IsSuccess, collection.Message ?? "Collection failed.");
                Check(bill.Status == MsapConstants.BillingStatus.Collected && bill.Balance == 0m, "Full collection did not settle the billing.");
                Check((await audits.GetJobOrderTimelineAsync(job.JobOrderId, default)).Any(a => a.DocumentType == "Collection" && a.RecordId == collection.Data), "Collection is missing from the MSAP timeline.");
                Check(await original.FilprideAuditTrails.CountAsync() == 1 && await original.FilprideAuditTrails.AnyAsync(a => a.Id == originalAudit.Id), "Base audits were changed.");
                var splitTicket = new DispatchTicket
                {
                    JobOrderId = job.JobOrderId, CreatedBy = "check", DispatchNumber = "D-SPLIT", TugBoatId = tug.TugboatId,
                    CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId,
                    ServiceId = maritimeService.ServiceId, Status = MsapConstants.DispatchTicketStatus.ForBilling,
                    DispatchNetRevenue = 100m, BAFNetRevenue = 10m, TotalNetRevenue = 110m
                };
                module.MsapDispatchTickets.Add(splitTicket);
                await module.SaveChangesAsync();
                ServiceResult<(int DispatchBillingId, int BafBillingId)> split = await billingService.CreatePhilCebSplitAsync(new Billing
                {
                    MsapBillingNumber = "SPLIT01", Date = job.Date, CustomerId = customer.CustomerId,
                    JobOrderId = job.JobOrderId, VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId,
                    BilledTo = "CHECK01", ToBillDispatchTickets = [splitTicket.DispatchTicketId.ToString()]
                }, "BAF-CHECK", "check", "MMSI", default);
                Check(split.IsSuccess, split.Message ?? "Split billing creation failed.");
                Check(await module.MsapBillings.CountAsync(b =>
                    (b.MsapBillingNumber == "SPLIT01" || b.MsapBillingNumber == "BAF-CHECK")
                    && b.Terms == "CHECK17" && b.DueDate == job.Date.AddDays(17)) == 2,
                    "Split billing did not use shared Filpride terms.");
                Console.WriteLine("PASS: shared Filpride term selection, month-based due dates and regular/split billing with no local terms.");
                var auditCount = await module.AuditTrails.CountAsync();
                await module.Database.MigrateAsync();
                Check(await module.AuditTrails.CountAsync() == auditCount, "Repeating MSAP migration lost audits.");
                Check(await SchemaAsync(settings.ConnectionString) == before, "MSAP workflow changed public schema.");
                Console.WriteLine("PASS: isolated PostgreSQL migration/re-run, unchanged public schema/base audits, shared users, Job Order/dispatch/billing/posting/collection, VAT totals, record IDs and timeline isolation.");

                module.ChangeTracker.Clear();
                var importHttp = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
                var importer = new IBSWeb.Areas.MSAP.Controllers.MsapImportController(module,
                    NullLogger<IBSWeb.Areas.MSAP.Controllers.MsapImportController>.Instance)
                {
                    ControllerContext = new ControllerContext { HttpContext = importHttp },
                    TempData = tempData.GetTempData(importHttp)
                };
                using var csv = new MemoryStream("recid,number\n"u8.ToArray());
                var importFiles = new List<IFormFile>
                {
                    new FormFile(csv, 0, csv.Length, "bulkFiles", "billing.csv"),
                    new FormFile(csv, 0, csv.Length, "bulkFiles", "dispatch.csv")
                };
                await importer.Index(null, null, null, null, null, null, null, null,
                    null, null, null, null, null, null, null, importFiles);
                Check(!importer.TempData.ContainsKey("error") && importer.TempData.ContainsKey("success"),
                    $"MSAP import/sequence SQL failed: {importer.TempData["error"]}");
                await importer.Reset();
                Check(!importer.TempData.ContainsKey("error") && !await module.MsapJobOrders.AnyAsync() && !await module.Customers.AnyAsync(),
                    $"MSAP reset SQL failed: {importer.TempData["error"]}");
                Check(await SchemaAsync(settings.ConnectionString) == before && await original.Users.CountAsync() == 1
                    && await original.FilprideAuditTrails.CountAsync() == 1, "MSAP import/reset changed the host schema, users or audits.");
                Console.WriteLine("PASS: import clearing/sequence updates and reset use only the msap schema.");
            }
            finally
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", server);
                await drop.ExecuteNonQueryAsync();
            }
        }

        private static async Task<string> SchemaAsync(string connection)
        {
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT string_agg(table_name || ':' || column_name || ':' || data_type || ':' || coalesce(column_default, '') || ':' || is_nullable, ',' ORDER BY table_name, ordinal_position) FROM information_schema.columns WHERE table_schema = 'public'", db);
            return (string)(await command.ExecuteScalarAsync())!;
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
