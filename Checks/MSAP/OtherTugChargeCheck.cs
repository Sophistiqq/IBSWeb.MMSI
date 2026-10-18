using System.Reflection;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Checks.MSAP
{
    internal static class OtherTugChargeCheck
    {
        public static async Task RunAsync()
        {
            var model = new OtherTugChargeViewModel
            {
                ProviderReference = "BILL-1", DocumentDate = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()), IncludesVat = true,
                Allocations = [new OtherTugChargeLineViewModel { DispatchTicketId = 1, Amount = 0m }]
            };
            Check(OtherTugChargeService.ValidateAllocations(model) == null, "Explicit zero charge was rejected.");
            model.Allocations[0].Amount = null;
            Check(OtherTugChargeService.ValidateAllocations(model) != null, "Missing charge was treated as zero.");
            model.Allocations[0].Amount = 1.001m;
            Check(OtherTugChargeService.ValidateAllocations(model) != null, "Excess charge precision was accepted.");
            model.Allocations[0].Amount = 100m;
            model.Allocations.Add(new OtherTugChargeLineViewModel { DispatchTicketId = 1, Amount = 200m });
            Check(OtherTugChargeService.ValidateAllocations(model) != null, "Duplicate ticket allocation was accepted.");
            model.Allocations[1].DispatchTicketId = 2;
            Check(OtherTugChargeService.ValidateAllocations(model) == null, "Distinct ticket allocations were rejected.");
            model.IncludesVat = null;
            Check(OtherTugChargeService.ValidateAllocations(model) != null, "Unknown VAT inclusion was silently defaulted.");
            var ticket = new DispatchTicket { Tugboat = new Tugboat { IsCompanyOwned = false } };
            Check(ticket.OtherTugCostPending, "Unrecorded external cost appeared as confirmed zero.");
            ticket.OtherTugChargeLine = new OtherTugChargeLine { Charge = new OtherTugCharge() };
            Check(ticket.OtherTugCostPending, "Draft cost was included in confirmed reports.");
            ticket.OtherTugChargeLine.Charge.ConfirmedAt = DateTimeHelper.GetCurrentPhilippineTime();
            Check(!ticket.OtherTugCostPending, "Explicitly confirmed zero remained pending.");
            await using var validStream = new MemoryStream("%PDF-1.7"u8.ToArray());
            var valid = new FormFile(validStream, 0, validStream.Length, "document", "bill.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            Check(await OtherTugChargeService.IsSupportedDocumentAsync(valid, default), "PDF signature was rejected.");
            valid.ContentType = "text/html";
            Check(!await OtherTugChargeService.IsSupportedDocumentAsync(valid, default), "Unsafe document content type was accepted.");
            await using var fakeStream = new MemoryStream("<script>"u8.ToArray());
            var fake = new FormFile(fakeStream, 0, fakeStream.Length, "document", "bill.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            Check(!await OtherTugChargeService.IsSupportedDocumentAsync(fake, default), "Renamed HTML was accepted as PDF.");
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
            var service = new OtherTugChargeService(db, null!, null!);
            var method = typeof(OtherTugChargeService).GetMethod("EligibleTickets", BindingFlags.NonPublic | BindingFlags.Instance)!;
            string sql = ((IQueryable<DispatchTicket>)method.Invoke(service, null)!).ToQueryString();
            Check(sql.Contains("RECID", StringComparison.Ordinal) && sql.Contains("MMSI", StringComparison.Ordinal), "Actual-ticket eligibility did not translate with company scope.");
            var line = db.Model.FindEntityType(typeof(OtherTugChargeLine))!;
            Check(line.GetIndexes().Any(index => index.IsUnique && index.Properties.Single().Name == nameof(OtherTugChargeLine.DispatchTicketId)), "Ticket allocation uniqueness is missing.");
            Console.WriteLine("PASS: provider allocation validation, pending/confirmed zero, document boundaries, company-scoped query translation and unique ticket allocation (no database).");
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
