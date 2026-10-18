using Microsoft.EntityFrameworkCore;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;

namespace Checks.MSAP
{
    internal static class AccountingGapsCheck
    {
        public static void Run()
        {
            var date = new DateOnly(2026, 10, 14);
            var entry = new VesselScheduleServiceEntry { VesselScheduleServiceEntryId = 1, TugboatId = 2, ServiceId = 3,
                TariffBasis = TariffChargeBasis.PerHour, BafMethod = BafCalculationMethod.PerHour };
            var schedule = new VesselSchedule { Revision = 1, ApprovedRevision = 1, Status = MsapConstants.VesselScheduleStatus.Confirmed, SbmaEnabled = true };
            entry.VesselSchedule = schedule;
            var ticket = new DispatchTicket { DispatchTicketId = 1, VesselScheduleServiceEntryId = 1, TugBoatId = 2, ServiceId = 3,
                CustomerOrderSlipId = 1, DateLeft = date, DateArrived = date, TimeLeft = new TimeOnly(8, 0), TimeArrived = new TimeOnly(8, 30), ExpectedServiceEntry = entry };
            Guid identity = ticket.AccountingSourceId;
            var tariff = new TariffRate { Dispatch = 1120m };
            var baf = new BafRate { Amount = 100m, AsOfDate = new DateOnly(2026, 10, 6), ApprovedAt = new DateTime(2026, 10, 7) };
            Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, tariff, baf) != null
                && ticket.Status == MsapConstants.DispatchTicketStatus.CalculationPending, "Old-week BAF was used for a new-week ticket.");
            baf.AsOfDate = new DateOnly(2026, 10, 13);
            Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, tariff, baf) == null
                && ticket.TotalHours == 1m && ticket.AccountingSourceId == identity, "Recalculation failed or replaced the accounting identity.");
            Check(ScheduledTicketCharges.BafCycleStart(new DateOnly(2026, 10, 12)) == new DateOnly(2026, 10, 6)
                && ScheduledTicketCharges.BafCycleStart(new DateOnly(2026, 10, 13)) == new DateOnly(2026, 10, 13), "Tuesday boundary is incorrect.");
            entry.ManualBafRate = 0m;
            Check(ScheduledTicketCharges.GetBlocker(ticket, schedule, entry, tariff, null) == null, "Approved manual BAF was subjected to weekly table coverage.");
            Check(SbmaShareCalculator.Calculate(ticket, true) == 2625m && SbmaShareCalculator.Calculate(ticket, false) == 0m,
                "SBMA ignored actual minutes or its COS toggle.");
            var second = new DispatchTicket { DispatchTicketId = 2, CustomerOrderSlipId = 1, TugBoatId = 2,
                DateLeft = date, DateArrived = date, TimeLeft = new TimeOnly(9, 0), TimeArrived = new TimeOnly(10, 0), ExpectedServiceEntry = entry };
            Check(SbmaShareCalculator.Allocate([ticket, second]).Values.Sum() == 7875m, "Combined actual tugboat service time is incorrect.");
            var bill = new Billing { MsapBillingId = 4, MsapBillingNumber = "26000001", Revision = 1, Date = new DateOnly(2026, 4, 30),
                Customer = new Customer { CustomerName = "Customer", CustomerTin = "TIN", CustomerAddress = "Address", VatType = MsapConstants.VatType_Vatable },
                Vessel = new Vessel { VesselName = "Vessel" }, Amount = 1120m, IsVatable = true, Company = MsapConstants.Company_MMSI };
            var book = BillingBookEntries.Create(bill, new DateTime(2026, 5, 2), "Name", "actor-id");
            Check(book.TransactionDate == new DateOnly(2026, 5, 2) && book.BillingDate == bill.Date && book.NetSales == 1000m
                && book.VatAmount == 120m && book.DocumentId == 4 && book.BillingRevision == 1, "Approval period, VAT or source linkage is incorrect.");
            bill.Discount = 112m;
            book = BillingBookEntries.Create(bill, new DateTime(2026, 5, 2), "Name", "actor-id");
            Check(book.Amount == 1008m && book.NetSales == 900m, "Sales Book deducted discount twice.");
            bill.Discount = 0; bill.IsVatable = false; bill.Customer.VatType = MsapConstants.VatType_Exempt;
            Check(BillingBookEntries.Create(bill, DateTime.Now, "Name", "id").VatExemptSales == 1120m, "Exempt sales were classified as zero rated.");
            bill.Principal = new Principal { PrincipalName = "Principal", TIN = "Principal TIN", Address1 = "Principal address" };
            Check(BillingBookEntries.Create(bill, DateTime.Now, "Name", "id").SoldTo == "Principal", "Principal billing used the customer as billed party.");
            ticket.TimeArrived = new TimeOnly(8, 0, 2);
            second.TimeLeft = new TimeOnly(9, 0); second.TimeArrived = new TimeOnly(9, 0, 2);
            Check(SbmaShareCalculator.Allocate([ticket, second]).Values.Sum() == 5.83m, "SBMA per-tug rounding residual was lost.");
            using var db = new IBS.DataAccess.MSAP.Data.MsapDbContext(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<IBS.DataAccess.MSAP.Data.MsapDbContext>()
                    .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
            var entity = db.Model.FindEntityType(typeof(IBS.Models.MSAP.Books.SalesBook))!;
            Check(entity.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "DocumentId", "BillingRevision" })), "Duplicate Sales Book revision is not prevented.");
            Console.WriteLine("PASS: weekly BAF boundaries, pending/recalculation, source identity, SBMA actual time, and Sales Book date/tax snapshots (no database).");
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
