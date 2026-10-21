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
                Status = MsapConstants.DispatchTicketStatus.ForBilling, TotalBilling = 1120m, TotalNetRevenue = 1120m, DispatchBillingAmount = 1120m,
                Customer = new Customer { CustomerName = "Customer", VatType = MsapConstants.VatType_Vatable },
                Vessel = new Vessel { VesselName = "Vessel" }, Tugboat = tug, Service = new Service { ServiceName = "Docking" },
                ApOtherTugs = id == 3 ? 112m : 0m,
                OtherTugChargeLine = id == 2 ? new OtherTugChargeLine { Charge = new OtherTugCharge { ConfirmedAt = new DateTime(2026, 10, 1) } } : null
            }).ToList();
            var report = Proxy<IReportRepository>((_, _) => Task.FromResult(tickets));
            var tugs = Proxy<ITugboatRepository>((_, _) => Task.FromResult<IEnumerable<Tugboat>>([]));
            var owners = Proxy<ITugboatOwnerRepository>((_, _) => Task.FromResult<IEnumerable<TugboatOwner>>([]));
            var customers = Proxy<ICustomerRepository>((_, _) => Task.FromResult<IEnumerable<Customer>>([]));
            var work = Proxy<IUnitOfWork>((method, _) => method.Name switch
            {
                "get_Report" => report, "get_Tugboat" => tugs, "get_TugboatOwner" => owners, "get_Customer" => customers,
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
            Console.WriteLine("PASS: AR export retains pending/confirmed-zero costs and totals only known values (no database).");
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)proxy).Handler = handler;
            return proxy;
        }
    }
}
