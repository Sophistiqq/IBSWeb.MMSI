using System.Linq.Expressions;
using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Constants;
using IBSWeb.Areas.MSAP.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using OfficeOpenXml;

namespace Checks.MSAP
{
    internal static class ArMonitoringCheck
    {
        public static async Task RunAsync()
        {
            ExcelPackage.License.SetNonCommercialPersonal("MSAP development checks");
            var tug = new Tugboat { TugboatName = "External tug", IsCompanyOwned = false };
            List<DispatchTicket> tickets = Enumerable.Range(1, 3).Select(id => new DispatchTicket
            {
                DispatchTicketId = id, DispatchNumber = $"T-{id}", Date = new DateOnly(2026, 10, 1),
                Status = MsapConstants.DispatchTicketStatus.ForBilling, TotalBilling = 1120m, TotalNetRevenue = 1120m, DispatchBillingAmount = 1120m, DispatchNetRevenue = 1120m,
                Customer = new Customer { CustomerName = "Customer", VatType = MsapConstants.VatType_Vatable },
                Vessel = new Vessel { VesselName = "Vessel" }, Tugboat = tug, Service = new Service { ServiceName = "Docking" },
                ApOtherTugs = id == 3 ? 112m : 0m,
                OtherTugChargeLine = id == 2 ? new OtherTugChargeLine { Charge = new OtherTugCharge { ConfirmedAt = new DateTime(2026, 10, 1) } } : null
            }).ToList();
            var report = Proxy<IReportRepository>((_, _) => Task.FromResult(tickets));
            var tugs = Proxy<ITugboatRepository>((_, _) => Task.FromResult<IEnumerable<Tugboat>>([]));
            var owners = Proxy<ITugboatOwnerRepository>((_, _) => Task.FromResult<IEnumerable<TugboatOwner>>([]));
            var customers = Proxy<ICustomerRepository>((_, _) => Task.FromResult<IEnumerable<Customer>>([]));
            var vesselSchedules = Proxy<IVesselScheduleRepository>((_, _) => Task.FromResult<IEnumerable<VesselSchedule>>([]));
            List<Billing> billings = [];
            List<DispatchTicket> billedTickets = [];
            List<Collection> collections = [];
            var billingRepository = Proxy<IBillingRepository>((_, arguments) => Task.FromResult<IEnumerable<Billing>>(
                arguments?[0] is Expression<Func<Billing, bool>> filter ? billings.Where(filter.Compile()).ToList() : billings));
            var dispatchTicketRepository = Proxy<IDispatchTicketRepository>((_, arguments) => Task.FromResult<IEnumerable<DispatchTicket>>(
                arguments?[0] is Expression<Func<DispatchTicket, bool>> filter ? billedTickets.Where(filter.Compile()).ToList() : billedTickets));
            var collectionRepository = Proxy<ICollectionRepository>((_, arguments) => Task.FromResult<IEnumerable<Collection>>(
                arguments?[0] is Expression<Func<Collection, bool>> filter ? collections.Where(filter.Compile()).ToList() : collections));
            var work = Proxy<IUnitOfWork>((method, _) => method.Name switch
            {
                "get_Report" => report, "get_Tugboat" => tugs, "get_TugboatOwner" => owners, "get_Customer" => customers,
                "get_VesselSchedule" => vesselSchedules, "get_Billing" => billingRepository,
                "get_DispatchTicket" => dispatchTicketRepository, "get_Collection" => collectionRepository,
                _ => throw new InvalidOperationException(method.Name)
            });
            var controller = new MaritimeReportController(work)
            {
                TempData = new TempDataDictionary(new DefaultHttpContext(), Proxy<ITempDataProvider>((_, _) => new Dictionary<string, object>()))
            };
            var result = await controller.SalesSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);
            if (result is not FileContentResult file)
            {
                throw new InvalidOperationException($"AR export failed: {controller.TempData["error"]}");
            }
            using var stream = new MemoryStream(file.FileContents);
            using var package = new ExcelPackage(stream);
            var sheet = package.Workbook.Worksheets[0];
            if (sheet.Cells[7, 33].Text != "Pending" || sheet.Cells[7, 35].Value != null
                || sheet.Cells[8, 33].GetValue<decimal>() != 0m
                || sheet.Cells[10, 33].GetValue<decimal>() != 112m || sheet.Cells[10, 35].GetValue<decimal>() != 1900m)
            {
                throw new InvalidOperationException($"AR export values: pending={sheet.Cells[7, 33].Text}, pending net={sheet.Cells[7, 35].Value ?? "null"}, confirmed zero={sheet.Cells[8, 33].Value}, AP total={sheet.Cells[10, 33].Value}, known net={sheet.Cells[10, 35].Value}.");
            }
            tickets.Clear();
            tickets.Add(new DispatchTicket
            {
                DispatchTicketId = 4, DispatchNumber = "T-4", Date = new DateOnly(2026, 10, 1),
                Status = MsapConstants.DispatchTicketStatus.Billed, TotalBilling = 1008m, TotalNetRevenue = 1008m,
                DispatchBillingAmount = 1120m, DispatchNetRevenue = 1008m,
                Customer = new Customer { CustomerName = "Customer", VatType = MsapConstants.VatType_Vatable },
                Vessel = new Vessel { VesselName = "Vessel" }, Tugboat = tug, Service = new Service { ServiceName = "Docking" },
                Billing = new Billing { IsVatable = true, IsVatInclusive = true, PrintWht = true }
            });
            result = await controller.SalesSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);
            using var discountedStream = new MemoryStream(((FileContentResult)result).FileContents);
            using var discountedPackage = new ExcelPackage(discountedStream);
            var discountedSheet = discountedPackage.Workbook.Worksheets[0];
            if (discountedSheet.Cells[7, 16].GetValue<decimal>() != 1008m
                || discountedSheet.Cells[7, 18].GetValue<decimal>() != 900m
                || discountedSheet.Cells[7, 20].GetValue<decimal>() != 18m)
            {
                throw new InvalidOperationException($"Discounted AR values: gross={discountedSheet.Cells[7, 16].Value}, net={discountedSheet.Cells[7, 18].Value}, WHT={discountedSheet.Cells[7, 20].Value}.");
            }

            foreach (var (earned, vatable, inclusive, gross, net, wht) in new[]
            {
                (1120m, true, true, 1120m, 1000m, 20m),
                (0m, true, true, 0m, 0m, 0m),
                (1008m, false, false, 1008m, 1008m, 20.16m),
                (1008m, true, false, 1128.96m, 1008m, 20.16m)
            })
            {
                tickets[0].DispatchNetRevenue = tickets[0].TotalNetRevenue = earned;
                tickets[0].Billing!.IsVatable = vatable;
                tickets[0].Billing!.IsVatInclusive = inclusive;
                var boundaryExport = await controller.SalesSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);
                using var boundaryStream = new MemoryStream(((FileContentResult)boundaryExport).FileContents);
                using var boundaryPackage = new ExcelPackage(boundaryStream);
                var boundarySheet = boundaryPackage.Workbook.Worksheets[0];
                if (boundarySheet.Cells[7, 16].GetValue<decimal>() != gross
                    || boundarySheet.Cells[7, 18].GetValue<decimal>() != net
                    || boundarySheet.Cells[7, 20].GetValue<decimal>() != wht)
                {
                    throw new InvalidOperationException($"AR discount/VAT boundary failed: earned={earned}, vatable={vatable}, inclusive={inclusive}.");
                }
            }

            var receipt = new Collection { MsapCollectionId = 3, MsapCollectionNumber = "BAF-R", Date = new DateOnly(2026, 10, 2), Amount = 198m, EWT = 3.60m };
            var bafBilling = new Billing { MsapBillingId = 11, BasicBillingId = 10, CollectionId = 3, Amount = 201.60m, AmountPaid = 201.60m };
            var basicBilling = new Billing { MsapBillingId = 10, IsVatable = true, IsVatInclusive = true, PrintWht = true, BafBilling = bafBilling };
            var billedTicket = new DispatchTicket
            {
                DispatchTicketId = 5, DispatchNumber = "T-5", Date = new DateOnly(2026, 10, 1), BillingId = 10,
                Status = MsapConstants.DispatchTicketStatus.Billed, TotalBilling = 1209.60m, TotalNetRevenue = 1209.60m,
                DispatchBillingAmount = 1120m, DispatchNetRevenue = 1008m, BAFBillingAmount = 224m, BAFNetRevenue = 201.60m,
                Customer = new Customer { CustomerName = "Customer", VatType = MsapConstants.VatType_Vatable },
                Vessel = new Vessel { VesselName = "Vessel" }, Tugboat = tug, Service = new Service { ServiceName = "Docking" },
                Billing = basicBilling
            };
            billings = [basicBilling, bafBilling];
            billedTickets = [billedTicket];
            collections = [receipt];
            tickets = [billedTicket];
            result = await controller.SalesSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);
            using var bafOnlyStream = new MemoryStream(((FileContentResult)result).FileContents);
            using var bafOnlyPackage = new ExcelPackage(bafOnlyStream);
            var bafOnlySheet = bafOnlyPackage.Workbook.Worksheets[0];
            if (bafOnlySheet.Cells[7, 23].Text != "BAF-R" || bafOnlySheet.Cells[7, 26].GetValue<decimal>() != 3.60m
                || bafOnlySheet.Cells[7, 27].GetValue<decimal>() != 198m)
            {
                throw new InvalidOperationException($"BAF-only receipt values: receipt={bafOnlySheet.Cells[7, 23].Text}, EWT={bafOnlySheet.Cells[7, 26].Value}, cash={bafOnlySheet.Cells[7, 27].Value}.");
            }

            var basicReceipt = new Collection { MsapCollectionId = 4, MsapCollectionNumber = "BASIC-R", Date = new DateOnly(2026, 10, 2), Amount = 990m, EWT = 18m };
            basicBilling.CollectionId = 4;
            billings = [basicBilling, bafBilling];
            collections = [receipt, basicReceipt];
            result = await controller.SalesSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);
            using var splitReceiptsStream = new MemoryStream(((FileContentResult)result).FileContents);
            using var splitReceiptsPackage = new ExcelPackage(splitReceiptsStream);
            var splitReceiptsSheet = splitReceiptsPackage.Workbook.Worksheets[0];
            if (!splitReceiptsSheet.Cells[7, 23].Text.Contains("BAF-R", StringComparison.Ordinal)
                || !splitReceiptsSheet.Cells[7, 23].Text.Contains("BASIC-R", StringComparison.Ordinal)
                || splitReceiptsSheet.Cells[7, 26].GetValue<decimal>() != 21.60m
                || splitReceiptsSheet.Cells[7, 27].GetValue<decimal>() != 1188m)
            {
                throw new InvalidOperationException($"Split receipt values: receipts={splitReceiptsSheet.Cells[7, 23].Text}, EWT={splitReceiptsSheet.Cells[7, 26].Value}, cash={splitReceiptsSheet.Cells[7, 27].Value}.");
            }

            var combinedReceipt = new Collection { MsapCollectionId = 5, MsapCollectionNumber = "BOTH-R", Date = new DateOnly(2026, 10, 2), Amount = 1188m, EWT = 21.60m };
            basicBilling.CollectionId = 5;
            bafBilling.CollectionId = 5;
            billings = [basicBilling, bafBilling];
            collections = [combinedReceipt];
            result = await controller.SalesSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);
            using var combinedReceiptStream = new MemoryStream(((FileContentResult)result).FileContents);
            using var combinedReceiptPackage = new ExcelPackage(combinedReceiptStream);
            var combinedReceiptSheet = combinedReceiptPackage.Workbook.Worksheets[0];
            if (combinedReceiptSheet.Cells[7, 23].Text != "BOTH-R"
                || combinedReceiptSheet.Cells[7, 26].GetValue<decimal>() != 21.60m
                || combinedReceiptSheet.Cells[7, 27].GetValue<decimal>() != 1188m)
            {
                throw new InvalidOperationException($"Combined receipt values: receipt={combinedReceiptSheet.Cells[7, 23].Text}, EWT={combinedReceiptSheet.Cells[7, 26].Value}, cash={combinedReceiptSheet.Cells[7, 27].Value}.");
            }

            basicBilling.CollectionId = 4;
            bafBilling.CollectionId = null;
            collections = [basicReceipt];
            await AssertReceiptAsync(billedTicket.DispatchNumber, "BASIC-R", 990m, 18m);

            var otherBilling = new Billing { MsapBillingId = 20, CollectionId = 5, Amount = 1344m,
                IsVatable = true, IsVatInclusive = true, PrintWht = true };
            var otherTicket = new DispatchTicket
            {
                DispatchTicketId = 6, DispatchNumber = "T-6", Date = billedTicket.Date, BillingId = 20,
                CustomerOrderSlipId = 2, Status = MsapConstants.DispatchTicketStatus.Billed,
                DispatchBillingAmount = 1120m, DispatchNetRevenue = 1120m, BAFBillingAmount = 224m, BAFNetRevenue = 224m,
                TotalBilling = 1344m, TotalNetRevenue = 1344m, Customer = billedTicket.Customer,
                Vessel = billedTicket.Vessel, Tugboat = tug, Service = billedTicket.Service, Billing = otherBilling
            };
            billedTicket.CustomerOrderSlipId = 1;
            basicBilling.CollectionId = bafBilling.CollectionId = 5;
            combinedReceipt.Amount = 2508m;
            combinedReceipt.EWT = 45.60m;
            billings = [basicBilling, bafBilling, otherBilling];
            billedTickets = [billedTicket, otherTicket];
            collections = [combinedReceipt];
            tickets = [billedTicket];
            await AssertReceiptAsync(billedTicket.DispatchNumber, "BOTH-R", 1188m, 21.60m);
            tickets = [billedTicket, otherTicket];
            await AssertReceiptAsync(billedTicket.DispatchNumber, "BOTH-R", 1188m, 21.60m);
            await AssertReceiptAsync(otherTicket.DispatchNumber, "BOTH-R", 1320m, 24m);

            Console.WriteLine("PASS: AR discounts, separate Basic/BAF receipts and filtered multi-COS settlement reconcile (no database).");

            async Task AssertReceiptAsync(string dispatchNumber, string receiptNumber, decimal cash, decimal ewt)
            {
                var export = await controller.SalesSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), default);
                using var exportStream = new MemoryStream(((FileContentResult)export).FileContents);
                using var exportPackage = new ExcelPackage(exportStream);
                var exportSheet = exportPackage.Workbook.Worksheets[0];
                var row = Enumerable.Range(7, exportSheet.Dimension.End.Row - 6)
                    .Single(index => exportSheet.Cells[index, 2].Text == dispatchNumber);
                if (exportSheet.Cells[row, 23].Text != receiptNumber
                    || exportSheet.Cells[row, 26].GetValue<decimal>() != ewt
                    || exportSheet.Cells[row, 27].GetValue<decimal>() != cash)
                {
                    throw new InvalidOperationException($"Receipt allocation for {dispatchNumber}: expected cash={cash}, EWT={ewt}; "
                        + $"actual cash={exportSheet.Cells[row, 27].Value}, EWT={exportSheet.Cells[row, 26].Value}.");
                }
            }
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)proxy).Handler = handler;
            return proxy;
        }
    }
}
