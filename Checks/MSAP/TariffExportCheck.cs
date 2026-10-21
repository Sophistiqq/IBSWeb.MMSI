using System.Reflection;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;

namespace Checks.MSAP
{
    internal static class TariffExportCheck
    {
        public static async Task RunAsync()
        {
            ExcelPackage.Configure(options => options.Configuration = new ConfigurationBuilder()
                .SetBasePath(Path.GetFullPath("IBSWeb")).AddJsonFile("appsettings.json").Build());
            var date = new DateOnly(2026, 3, 3);
            var customer = new Customer { CustomerCode = "0000123", CustomerName = "Customer" };
            var port = new Port { PortNumber = "001", PortName = "Batangas" };
            var terminal = new Terminal { TerminalNumber = "002", TerminalName = "Terminal" };
            var activity = new Service { ServiceNumber = "003", ServiceName = "Docking" };
            var tariffs = DispatchProxy.Create<ITariffTableRepository, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)tariffs).Handler = (method, args) => method.Name == "GetRatesForExportAsync"
                ? Task.FromResult(new List<TariffRate> { new() { AsOfDate = date, Dispatch = 35000m, Customer = customer, Port = port, Terminal = terminal, Service = activity, CreatedBy = "Preparer" } })
                : throw new NotSupportedException(method.Name);
            var bafs = DispatchProxy.Create<IBafTableRepository, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)bafs).Handler = (method, args) => method.Name == "GetRatesForExportAsync"
                ? Task.FromResult(new List<BafRate> { new() { AsOfDate = date, Amount = 0m, Customer = customer, Port = port, Terminal = terminal, Service = activity, CreatedBy = "Preparer" } })
                : throw new NotSupportedException(method.Name);
            var work = DispatchProxy.Create<IUnitOfWork, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)work).Handler = (method, args) => method.Name switch
            {
                "get_TariffTable" => tariffs,
                "get_BafTable" => bafs,
                _ => throw new NotSupportedException(method.Name)
            };
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
            var service = new TariffRateService(new RateUploadService(db, work), NullLogger<TariffRateService>.Instance);
            Check(!(await service.ExportAsync(date.AddDays(1), date, default)).IsSuccess, "Reversed range was accepted.");
            foreach (var kind in Enum.GetValues<RateTableKind>())
            {
                foreach (bool history in new[] { false, true })
                {
                    var result = await service.ExportAsync(history ? date : null, date, default, kind);
                    Check(result.IsSuccess, result.Message ?? "Export failed.");
                    using var package = new ExcelPackage(new MemoryStream(result.Data!));
                    var sheet = package.Workbook.Worksheets[0];
                    Check(sheet.Cells[2, 1].Text == "0000123" && sheet.Cells[2, 3].Text == "001", "Leading zeros lost.");
                    Check(sheet.Cells[2, 9].GetValue<DateTime>() == date.ToDateTime(TimeOnly.MinValue)
                        && sheet.Cells[2, 10].GetValue<decimal>() == (kind == RateTableKind.Tariff ? 35000m : 0m), "Independent rates or typed date changed.");
                    Check(sheet.Cells[1, 10].Text == $"{kind}Amount" && string.IsNullOrEmpty(sheet.Cells[2, 10].Formula), "Wrong table or invented formula.");
                    Check(!history || sheet.Cells[2, 11].Text == "Preparer" && sheet.Cells[2, 14].Text == "Not recorded", "Fabricated approval evidence.");
                }
            }
            var row = new RateUploadRowViewModel { CustomerId = 1, PortId = 1, TerminalId = 1, ServiceId = 1, EffectiveDate = date, Amount = 0m };
            Check(RateUploadService.ValidateRows([row]).Count == 0, "Explicit zero rejected.");
            row.Amount = null;
            Check(RateUploadService.ValidateRows([row]).Count > 0, "Missing amount accepted.");
            row.Amount = 1.00001m;
            Check(RateUploadService.ValidateRows([row]).Count > 0, "Excess precision accepted.");
            row.Amount = 0;
            Check(RateUploadService.ValidateRows([row, row]).Any(error => error.Contains("duplicate")), "Duplicate rate accepted.");
            CheckCharges();
            CheckRateAudit(db);
            Console.WriteLine("PASS: independent tariff/BAF exports, missing versus zero, rate validation and all four BAF charging methods (no database used).");
        }

        private static void CheckRateAudit(MsapDbContext db)
        {
            var actor = new IBS.Models.ApplicationUser { Id = "actor-id", UserName = "account-login", Name = "Full Person Name" };
            var update = new RateUpload
            {
                RateKind = RateTableKind.BAF,
                RowsJson = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new RateUploadRowViewModel { StoredAmount = 35000m, Amount = 29000m, Selected = true },
                    new RateUploadRowViewModel { StoredAmount = 50000m, Amount = 40000m, Selected = false }
                })
            };
            MethodInfo method = typeof(RateUploadService).GetMethod("CreateAudit", BindingFlags.NonPublic | BindingFlags.Static)!;
            AuditTrail audit = (AuditTrail)method.Invoke(null, [actor, update, "Approved selected rates", update.RowsJson, true])!;
            Check(audit.ActorName == actor.Name && audit.UserId == actor.Id && audit.Username == actor.UserName
                && audit.ReferenceNumber == update.RateUploadId.ToString(), "Audit lost the actor name, stable identity or update reference.");
            int before = audit.Activity.IndexOf("Before: ", StringComparison.Ordinal) + 8;
            int after = audit.Activity.IndexOf(". After: ", StringComparison.Ordinal);
            using var oldValues = System.Text.Json.JsonDocument.Parse(audit.Activity[before..after]);
            using var newValues = System.Text.Json.JsonDocument.Parse(audit.Activity[(after + 9)..].TrimEnd('.'));
            Check(oldValues.RootElement.GetArrayLength() == 1 && newValues.RootElement.GetArrayLength() == 1
                && oldValues.RootElement[0].GetProperty("Amount").GetDecimal() == 35000m
                && newValues.RootElement[0].GetProperty("Amount").GetDecimal() == 29000m,
                "Approval audit omitted old/new amounts or included a row that was not approved.");
            var repository = new IBS.DataAccess.MSAP.Repository.AuditTrailRepository(db);
            MethodInfo queryMethod = repository.GetType().GetMethod("NamedAudits", BindingFlags.NonPublic | BindingFlags.Instance)!;
            IQueryable<AuditTrail> query = (IQueryable<AuditTrail>)queryMethod.Invoke(repository, null)!;
            string sql = query.Where(item => item.ActorName != null && item.ActorName.Contains("Full") && item.Activity.Contains("Approved"))
                .OrderBy(item => item.ActorName).ToQueryString();
            Check(sql.Contains("actor_name", StringComparison.Ordinal) && sql.Contains("AspNetUsers", StringComparison.Ordinal), "Named audit filtering does not translate to SQL.");
            Console.WriteLine("PASS: rate approval audit records the full name, account ID, reference and selected old/new amounts.");
        }

        private static void CheckCharges()
        {
            var schedule = new VesselSchedule { Revision = 1, ApprovedRevision = 1, Status = MsapConstants.VesselScheduleStatus.Confirmed, CosDiscountPercent = 10m };
            var entry = new VesselScheduleServiceEntry { VesselScheduleServiceEntryId = 1, TugboatId = 1, ServiceId = 1, TariffBasis = TariffChargeBasis.PerHour };
            var ticket = new DispatchTicket
            {
                VesselScheduleServiceEntryId = 1, TugBoatId = 1, ServiceId = 1,
                DateLeft = new DateOnly(2026, 1, 1), TimeLeft = new TimeOnly(10, 0),
                DateArrived = new DateOnly(2026, 1, 1), TimeArrived = new TimeOnly(12, 0)
            };
            foreach (var method in Enum.GetValues<BafCalculationMethod>())
            {
                entry.BafMethod = method;
                Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, new TariffRate { Dispatch = 100m }, new BafRate { Amount = 35000m, AsOfDate = new DateOnly(2025, 12, 30), ApprovedAt = new DateTime(2025, 12, 30) }) == null,
                    $"{method} blocked despite final amount.");
                Check(ticket.BAFBillingAmount == (method == BafCalculationMethod.PerHour ? 70000m : 35000m)
                    && ticket.BAFNetRevenue == (method == BafCalculationMethod.PerHour ? 63000m : 31500m)
                    && ticket.DispatchNetRevenue == 180m, $"{method} multiplied incorrectly or changed discount rules.");
                Check(ticket.BAFChargeType == (method == BafCalculationMethod.PerHour ? "Per hour" : method == BafCalculationMethod.PerMove ? "Per move" : method.ToString()), "BAF method label lost.");
            }
            Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, new TariffRate { Dispatch = 100m }, null) != null, "Missing BAF treated as zero.");
            entry.ManualBafRate = 0m;
            Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, new TariffRate { Dispatch = 100m }, null) == null && ticket.BAFBillingAmount == 0m, "Approved manual zero rejected.");
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
