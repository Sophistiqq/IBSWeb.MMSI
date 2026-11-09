using System.Reflection;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.DataAccess.MSAP.Repository.Msap;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Constants;
using IBSWeb.Areas.MSAP.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OfficeOpenXml;

namespace Checks.MSAP
{
    internal static class SalesReportDatabaseCheck
    {
        public static async Task RunAsync()
        {
            ExcelPackage.License.SetNonCommercialPersonal("MSAP development checks");
            var connection = TugboatOwnerCheck.TestConnection();
            var options = new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(connection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options;
            await using var db = new MsapDbContext(options);
            await db.Database.MigrateAsync();
            Check(!db.Database.HasPendingModelChanges(), "Report migration differs from the model.");

            var january = new DateOnly(1902, 1, 1);
            var february = january.AddMonths(1);
            var januaryEnd = february.AddDays(-1);
            var februaryEnd = february.AddMonths(1).AddDays(-1);
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                Check(!await db.MsapDispatchTickets.AnyAsync(t => t.Date.Year == january.Year),
                    "Fixture period already contains data; use a fresh clone.");
                var customer = await db.Customers.FirstAsync(c => c.Company == MsapConstants.Company_MMSI);
                var vessel = await db.MsapVessels.FirstAsync();
                var terminal = await db.MsapTerminals.FirstAsync();
                var service = await db.MsapServices.FirstAsync();
                var owned = await db.MsapTugboats.FirstAsync(t => t.IsCompanyOwned);
                var external = await db.MsapTugboats.FirstAsync(t => !t.IsCompanyOwned && t.TugboatOwnerId != null);
                var reference = Guid.NewGuid().ToString("N")[..6];
                var receipt = new Collection
                {
                    MsapCollectionNumber = "R" + reference, Date = january.AddDays(20), CustomerId = customer.CustomerId,
                    Amount = 2508m, CashAmount = 2508m, EWT = 45.60m, Total = 2553.60m, Company = MsapConstants.Company_MMSI
                };
                var basic = Bill("B", 1008m);
                var baf = Bill("F", 201.60m);
                baf.BasicBilling = basic;
                var other = Bill("O", 1344m);
                var first = Ticket("FIRST", january.AddDays(14));
                first.CustomerOrderSlip = Cos("A");
                first.Billing = basic;
                first.DispatchBillingAmount = 1120m;
                first.DispatchNetRevenue = 1008m;
                first.DispatchDiscount = 112m;
                first.BAFBillingAmount = 224m;
                first.BAFNetRevenue = 201.60m;
                first.BAFDiscount = 22.40m;
                first.TotalBilling = first.TotalNetRevenue = 1209.60m;
                var second = Ticket("SECOND", february.AddDays(14));
                second.CustomerOrderSlip = Cos("B");
                second.Billing = other;
                second.DispatchBillingAmount = second.DispatchNetRevenue = 1120m;
                second.BAFBillingAmount = second.BAFNetRevenue = 224m;
                second.TotalBilling = second.TotalNetRevenue = 1344m;
                var pending = Ticket("PENDING", january.AddDays(15));
                pending.TugBoatId = external.TugboatId;
                pending.Vessel = new Vessel { VesselNumber = reference[..4], VesselName = "Foreign report fixture", VesselType = "FOREIGN" };
                pending.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                pending.DispatchBillingAmount = pending.DispatchNetRevenue = 150000m;
                pending.TotalBilling = pending.TotalNetRevenue = 150000m;
                pending.ApOtherTugsIncludesVat = false;
                db.AddRange(first, second, pending, baf);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                await using var shared = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
                var tempDataProvider = DispatchProxy.Create<ITempDataProvider, CollectionCheck.CheckProxy>();
                ((CollectionCheck.CheckProxy)(object)tempDataProvider).Handler = (_, _) => new Dictionary<string, object>();
                var controller = new MaritimeReportController(new UnitOfWork(db, shared))
                {
                    TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider)
                };
                var januaryBytes = await ExportAsync(january, januaryEnd);
                using (var package = new ExcelPackage(new MemoryStream(januaryBytes)))
                {
                    var sheet = package.Workbook.Worksheets[0];
                    AssertAmounts(sheet, first.DispatchNumber, 1209.60m, 1080m, 1188m, 21.60m);
                    AssertAmounts(sheet, second.DispatchNumber, 1344m, 1200m, 1320m, 24m);
                    var row = FindRow(sheet, pending.DispatchNumber);
                    Check(sheet.Cells[row, 33].Text == "Awaiting AP Confirmation"
                        && sheet.Cells[row, 16, row, 32].All(c => c.Value == null), "Pending provider cost leaked financial amounts.");
                    var total = Enumerable.Range(7, sheet.Dimension.End.Row - 6).Single(r => sheet.Cells[r, 1].Text == "TOTAL");
                    Check(sheet.Cells[total, 17].GetValue<decimal>() == 2553.60m && sheet.Cells[total, 18].GetValue<decimal>() == 2280m
                        && sheet.Cells[total, 27].GetValue<decimal>() == receipt.Amount && sheet.Cells[total, 26].GetValue<decimal>() == receipt.EWT,
                        "AR totals do not reconcile to the receipt and discounted bills.");
                }
                var dispatch = await controller.DispatchTicketSummary(january, januaryEnd, default);
                Check(dispatch is FileContentResult, $"Dispatch export failed: {controller.TempData["error"]}");
                using (var package = new ExcelPackage(new MemoryStream(((FileContentResult)dispatch).FileContents)))
                {
                    var sheet = package.Workbook.Worksheets[0];
                    var row = FindRow(sheet, first.DispatchNumber);
                    Check(sheet.Cells[row, 34].GetValue<decimal>() == 1188m && sheet.Cells[row, 35].GetValue<decimal>() == 21.60m,
                        "Dispatch subset reallocated the receipt from a COS outside the service-date filter.");
                    Check(!sheet.Cells.Any(c => c.Text == second.DispatchNumber), "Dispatch service-date filtering included February.");
                }
                var forBilling = await controller.DispatchForBilling(january, januaryEnd, default);
                Check(forBilling is FileContentResult, $"For-billing export failed: {controller.TempData["error"]}");
                using (var package = new ExcelPackage(new MemoryStream(((FileContentResult)forBilling).FileContents)))
                {
                    var sheet = package.Workbook.Worksheets[0];
                    Check(sheet.Cells[FindRow(sheet, pending.DispatchNumber), 22].GetValue<decimal>() == 150000m
                        && !sheet.Cells.Any(c => c.Text == first.DispatchNumber), "For-billing export amounts or status filtering differ.");
                }
                Console.WriteLine("PASS: persisted mixed-discount, split Basic/BAF and multi-COS receipts reconcile in real AR/dispatch XLSX exports, including a filtered-out COS.");

                var storedPending = await db.MsapDispatchTickets.SingleAsync(t => t.DispatchTicketId == pending.DispatchTicketId);
                storedPending.ApOtherTugs = 100000m;
                storedPending.OtherTugChargeLine = new OtherTugChargeLine
                {
                    Amount = 100000m,
                    Charge = new OtherTugCharge
                    {
                        TugboatOwnerId = external.TugboatOwnerId!.Value, ProviderReference = reference,
                        DocumentDate = februaryEnd, ConfirmedAt = februaryEnd.ToDateTime(new TimeOnly(23, 59, 59)),
                        ConfirmedByUserId = "check", ConfirmedByName = "Check"
                    }
                };
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                using (var package = new ExcelPackage(new MemoryStream(await ExportAsync(january, januaryEnd))))
                {
                    var sheet = package.Workbook.Worksheets[0];
                    var row = FindRow(sheet, pending.DispatchNumber);
                    Check(sheet.Cells[row, 33].Text == "Deferred to confirmation month"
                        && sheet.Cells[row, 16, row, 32].All(c => c.Value == null), "Confirmation backfilled January financial amounts.");
                }
                using (var package = new ExcelPackage(new MemoryStream(await ExportAsync(february, februaryEnd))))
                {
                    var sheet = package.Workbook.Worksheets[0];
                    var row = FindRow(sheet, pending.DispatchNumber);
                    Check(sheet.Cells[row, 17].GetValue<decimal>() == 150000m
                        && sheet.Cells[row, 33].GetValue<decimal>() == 100000m
                        && sheet.Cells[row, 35].GetValue<decimal>() == 50000m
                        && sheet.Cells[row, 15].Value == null, "Confirmation-month amounts or service counts differ.");
                    Check(!sheet.Cells.Any(c => c.Text == second.DispatchNumber), "Owned ticket was recognized again in its service month.");
                }
                await transaction.RollbackAsync();
                Console.WriteLine("PASS: PostgreSQL confirmation cutoff and owned billing-date selection; fixture transaction rolled back.");

                Billing Bill(string suffix, decimal amount)
                {
                    return new Billing
                    {
                        MsapBillingNumber = suffix + reference, Date = january, Year = january.Year,
                        Status = MsapConstants.BillingStatus.Collected, BilledTo = MsapConstants.BilledToLocal,
                        CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = terminal.PortId, TerminalId = terminal.TerminalId,
                        Amount = amount, AmountPaid = amount, IsPaid = true, IsVatable = true, IsVatInclusive = true,
                        PrintWht = true, Company = MsapConstants.Company_MMSI, Collection = receipt
                    };
                }

                CustomerOrderSlip Cos(string suffix)
                {
                    return new CustomerOrderSlip
                    {
                        CustomerOrderSlipNumber = suffix + reference, Date = january, Status = "Open",
                        CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = terminal.PortId, TerminalId = terminal.TerminalId
                    };
                }

                DispatchTicket Ticket(string suffix, DateOnly date)
                {
                    return new DispatchTicket
                    {
                        DispatchNumber = suffix + reference, Date = date, CreatedBy = "Check", TotalHours = 2m,
                        CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = terminal.PortId, TerminalId = terminal.TerminalId,
                        ServiceId = service.ServiceId, TugBoatId = owned.TugboatId, Status = MsapConstants.DispatchTicketStatus.Billed
                    };
                }

                async Task<byte[]> ExportAsync(DateOnly from, DateOnly to)
                {
                    var result = await controller.SalesSummary(from, to, default);
                    Check(result is FileContentResult, $"Sales export failed: {controller.TempData["error"]}");
                    return ((FileContentResult)result).FileContents;
                }
            }
        }

        private static int FindRow(ExcelWorksheet sheet, string dispatchNumber)
        {
            return Enumerable.Range(7, sheet.Dimension.End.Row - 6).Single(row => sheet.Cells[row, 2].Text == dispatchNumber);
        }

        private static void AssertAmounts(ExcelWorksheet sheet, string number, decimal gross, decimal net, decimal cash, decimal ewt)
        {
            var row = FindRow(sheet, number);
            Check(sheet.Cells[row, 17].GetValue<decimal>() == gross && sheet.Cells[row, 18].GetValue<decimal>() == net
                && sheet.Cells[row, 27].GetValue<decimal>() == cash && sheet.Cells[row, 26].GetValue<decimal>() == ewt,
                $"Persisted export totals differ for {number}.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
