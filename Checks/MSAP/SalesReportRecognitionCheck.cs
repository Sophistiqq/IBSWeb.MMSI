using System.Linq.Expressions;
using System.Reflection;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using IBSWeb.Areas.MSAP.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace Checks.MSAP
{
    internal static class SalesReportRecognitionCheck
    {
        public static async Task RunAsync()
        {
            ExcelPackage.License.SetNonCommercialPersonal("MSAP development checks");
            var may = new DateOnly(2026, 5, 1);
            var june = may.AddMonths(1);
            var owner = new TugboatOwner { TugboatOwnerName = "Provider" };
            var customer = new Customer { CustomerName = "Customer" };
            var ticket = new DispatchTicket
            {
                DispatchTicketId = 1, DispatchNumber = "DEFERRED-1", CustomerId = 1, Date = may,
                Status = MsapConstants.DispatchTicketStatus.ForBilling, TotalHours = 2m,
                Tugboat = new Tugboat { TugboatName = "External tug", TugboatOwner = owner, IsCompanyOwned = false },
                Customer = customer, Vessel = new Vessel { VesselName = "Vessel", VesselType = "FOREIGN" },
                Service = new Service { ServiceName = "TENDING" },
                DispatchNetRevenue = 150000m, DispatchBillingAmount = 150000m,
                ApOtherTugsIncludesVat = false
            };
            var tickets = new List<DispatchTicket> { ticket };
            var savedReports = new Dictionary<(DateOnly, DateOnly), byte[]>();
            var reportReads = 0;
            var report = Proxy<IReportRepository>((method, arguments) =>
            {
                var from = (DateOnly)arguments![0]!;
                var to = (DateOnly)arguments[1]!;
                switch (method.Name)
                {
                    case nameof(IReportRepository.GetFinalSalesReportAsync):
                        return Task.FromResult(savedReports.GetValueOrDefault((from, to)));
                    case nameof(IReportRepository.SaveFinalSalesReportAsync):
                        savedReports.TryAdd((from, to), (byte[])arguments[2]!);
                        return Task.FromResult(savedReports[(from, to)]);
                    default:
                        reportReads++;
                        return Task.FromResult(tickets.Where(t => SalesReportRecognition.IncludesService(t, from, to)
                            || SalesReportRecognition.IncludesAmounts(t, from, to)).ToList());
                }
            });
            var work = Proxy<IUnitOfWork>((method, _) => method.Name switch
            {
                "get_Report" => report,
                "get_Tugboat" => Proxy<ITugboatRepository>((_, _) => Task.FromResult<IEnumerable<Tugboat>>([])),
                "get_TugboatOwner" => Proxy<ITugboatOwnerRepository>((_, _) => Task.FromResult<IEnumerable<TugboatOwner>>([owner])),
                "get_Customer" => Proxy<ICustomerRepository>((_, _) => Task.FromResult<IEnumerable<Customer>>([customer])),
                "get_VesselSchedule" => Proxy<IVesselScheduleRepository>((_, _) => Task.FromResult<IEnumerable<VesselSchedule>>([])),
                _ => throw new InvalidOperationException(method.Name)
            });
            var controller = new MaritimeReportController(work)
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), Proxy<ITempDataProvider>((_, _) => new Dictionary<string, object>()))
            };

            var mayBytes = await ExportAsync(may);
            using (var package = new ExcelPackage(new MemoryStream(mayBytes)))
            {
                var sheet = package.Workbook.Worksheets[0];
                Check(sheet.Cells[7, 2].Text == ticket.DispatchNumber && sheet.Cells[7, 15].GetValue<decimal>() == 2m,
                    "The original service and hours disappeared.");
                Check(sheet.Cells[6, 34].Text == "AP OTHER TUGS(NET OF VAT)" && sheet.Cells[6, 35].Text == "NET SALES"
                    && !sheet.Cells[6, 1, 6, sheet.Dimension.End.Column].Any(cell => cell.Text == "AP STATUS"),
                    "The original column layout was not restored.");
                Check(sheet.Cells[7, 33].Text == "Awaiting AP Confirmation" && sheet.Cells[8, 33].GetValue<decimal>() == 0m,
                    "The awaiting label was missing or affected AP totals.");
                Check(sheet.Cells[7, 16, 7, 32].All(cell => cell.Value == null) && sheet.Cells[7, 34, 7, 35].All(cell => cell.Value == null),
                    "Unconfirmed financial amounts were included.");
                Check(sheet.Cells[8, 17].GetValue<decimal>() == 0m && sheet.Cells[8, 35].GetValue<decimal>() == 0m,
                    "Service-only rows affected financial totals.");
                Check(sheet.Cells[7, FindColumn(sheet, "Provider")].Value == null
                    && sheet.Cells[7, FindColumn(sheet, "Customer")].Value == null,
                    "Deferred amounts leaked into a ledger column.");
                Check(sheet.Cells[7, FindColumn(sheet, "OTHER TUGS")].GetValue<decimal>() == 1m,
                    "The original tending service was not counted.");
            }

            ticket.ApOtherTugs = 100000m;
            ticket.OtherTugChargeLine = new OtherTugChargeLine { Charge = new OtherTugCharge { ConfirmedAt = new DateTime(2026, 6, 30, 23, 59, 59) } };
            ticket.Billing = new Billing { Date = new DateOnly(2026, 7, 1) };
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
            var repository = new ReportRepository(db);
            var filter = typeof(ReportRepository).GetMethod("FilterSalesReportTickets", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var month in new[] { may, june, june.AddMonths(1) })
            {
                var filtered = (IQueryable<DispatchTicket>)filter.Invoke(repository, [db.MsapDispatchTickets, month, month.AddMonths(1).AddDays(-1)])!;
                Check(filtered.ToQueryString().Contains("confirmed_at", StringComparison.Ordinal), "Settlement selection did not translate to PostgreSQL.");
                var where = (MethodCallExpression)filtered.Expression;
                var predicate = (Expression<Func<DispatchTicket, bool>>)((UnaryExpression)where.Arguments[1]).Operand;
                Check(predicate.Compile()(ticket) == (month != june.AddMonths(1)), "The query omitted the original or settlement month, or duplicated July.");
            }
            Check(!SalesReportRecognition.IncludesAmounts(ticket, may, may.AddMonths(1).AddDays(-1)), "Confirmation backfilled May.");
            Check(SalesReportRecognition.GetDeferredApLabel(ticket) == "Deferred to confirmation month",
                "A later confirmation was incorrectly shown as recognized in May.");
            Check(SalesReportRecognition.IncludesAmounts(ticket, june, june.AddMonths(1).AddDays(-1)), "Month-end confirmation was excluded.");
            var readsBeforeReplay = reportReads;
            var replay = await ExportAsync(may);
            Check(mayBytes.SequenceEqual(replay) && reportReads == readsBeforeReplay,
                "A finalized report was regenerated from changed transactions.");

            var juneBytes = await ExportAsync(june);
            using (var package = new ExcelPackage(new MemoryStream(juneBytes)))
            {
                var sheet = package.Workbook.Worksheets[0];
                Check(sheet.Cells[7, 17].GetValue<decimal>() == 150000m && sheet.Cells[7, 33].GetValue<decimal>() == 100000m
                    && sheet.Cells[7, 35].GetValue<decimal>() == 50000m, "Settlement amounts or margin were incorrect.");
                Check(sheet.Cells[8, 33].GetValue<decimal>() == 100000m, "Confirmed AP was replaced by a label or omitted from totals.");
                Check(sheet.Cells[8, 17].GetValue<decimal>() == 150000m && sheet.Cells[8, 35].GetValue<decimal>() == 50000m,
                    "Settlement amounts were omitted from totals.");
                Check(sheet.Cells[7, 15].Value == null && sheet.Cells[7, FindColumn(sheet, "OTHER TUGS")].Value == null,
                    "Settlement counted a second service or hours.");
                var summaryRow = Enumerable.Range(9, sheet.Dimension.End.Row - 8).Single(r => sheet.Cells[r, 3].Text == "SUB TOTAL - OTHER TUGS");
                Check(sheet.Cells[summaryRow, 6].GetValue<decimal>() == 0m && sheet.Cells[summaryRow, 8].GetValue<decimal>() == 0m,
                    "The lower summary counted the settlement as another service.");
                Check(sheet.Cells[7, FindColumn(sheet, "Provider")].GetValue<decimal>() == 100000m
                    && sheet.Cells[7, FindColumn(sheet, "Customer")].GetValue<decimal>() == 150000m,
                    "Settlement did not reach the AP and AR ledger columns.");
            }

            ticket.ApOtherTugs = 0m;
            Check(SalesReportRecognition.IncludesAmounts(ticket, june, june.AddMonths(1).AddDays(-1)), "Confirmed zero remained unsettled.");
            ticket.OtherTugChargeLine = null;
            Check(!SalesReportRecognition.IncludesAmounts(ticket, june, june.AddMonths(1).AddDays(-1)), "Missing cost was treated as zero.");
            ticket.ApOtherTugs = 10m;
            Check(SalesReportRecognition.IncludesAmounts(ticket, may, may.AddMonths(1).AddDays(-1)), "Legacy AP amounts lost their original period.");
            ticket.Tugboat.IsCompanyOwned = true;
            Check(SalesReportRecognition.IncludesAmounts(ticket, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
                "Company-owned billing-date recognition changed.");
            Console.WriteLine("PASS: service-only original period, deferred financial totals and ledgers, confirmation cutoff, no duplicate activity, confirmed zero, legacy AP and final export replay (no database).");

            async Task<byte[]> ExportAsync(DateOnly month)
            {
                var result = await controller.SalesSummary(month, month.AddMonths(1).AddDays(-1), default);
                Check(result is FileContentResult, $"Sales export failed: {controller.TempData["error"]}");
                return ((FileContentResult)result).FileContents;
            }
        }

        private static int FindColumn(ExcelWorksheet sheet, string label)
        {
            return Enumerable.Range(36, sheet.Dimension.End.Column - 35).First(c => sheet.Cells[6, c].Text == label);
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)proxy).Handler = handler;
            return proxy;
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
