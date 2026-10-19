using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models;
using IBS.Models.MSAP.MasterFile;
using IBSWeb.Areas.MSAP.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OfficeOpenXml;

namespace Checks.MSAP
{
    internal static class CustomerDiscountCheck
    {
        public static async Task RunAsync(MsapDbContext db, IUnitOfWork work, UserManager<ApplicationUser> users,
            ITempDataDictionaryFactory tempData)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "shared-check"), new Claim(ClaimTypes.Name, "Discount check")], "check"))
            };
            var controller = new CustomerController(work, NullLogger<CustomerController>.Instance, users, db)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = tempData.GetTempData(http)
            };
            var model = new CustomerViewModel
            {
                CustomerName = "Discount check", CustomerAddress = "Check address", CustomerTin = "000-000-000-00000",
                CustomerType = "Industrial", CustomerTerms = "COD", VatType = "Vatable", ZipCode = "1000"
            };
            Check(model.DefaultCosDiscountPercent == 0m, "Customer discount did not default to zero.");
            foreach (decimal percentage in new[] { -0.01m, 0m, 10m, 100m, 100.01m })
            {
                model.DefaultCosDiscountPercent = percentage;
                var errors = new List<ValidationResult>();
                bool valid = Validator.TryValidateProperty(percentage,
                    new ValidationContext(model) { MemberName = nameof(Customer.DefaultCosDiscountPercent) }, errors);
                Check(valid == (percentage >= 0m && percentage <= 100m), "Customer discount validation accepted an invalid percentage.");
            }
            model.DefaultCosDiscountPercent = 10m;
            controller.ModelState.AddModelError(nameof(Customer.CustomerCode), "The server-generated code was not submitted.");
            Check(await controller.Create(model, default) is RedirectToActionResult, "Customer create failed with a valid default discount.");
            Customer saved = await db.Customers.SingleAsync(c => c.CustomerId == model.CustomerId);
            Check(saved.DefaultCosDiscountPercent == 10m && new CustomerViewModel(saved).DefaultCosDiscountPercent == 10m,
                "Customer create/edit projection lost the default discount.");
            var edited = new CustomerViewModel(saved) { DefaultCosDiscountPercent = 12.5m };
            Check(await controller.Edit(edited, default) is RedirectToActionResult, "Customer edit failed with a valid default discount.");
            await db.Entry(saved).ReloadAsync();
            Check(saved.DefaultCosDiscountPercent == 12.5m && await db.AuditTrails.AnyAsync(a => a.Activity.Contains("default COS discount: 12.5")),
                "Customer edit did not persist/audit the default discount.");

            edited.DefaultCosDiscountPercent = -1m;
            controller.ModelState.AddModelError(nameof(Customer.DefaultCosDiscountPercent), "Invalid discount.");
            Check(await controller.Edit(edited, default) is ViewResult && edited.PaymentTerms?.Count > 0,
                "Invalid customer edit did not return a populated form.");
            await db.Entry(saved).ReloadAsync();
            Check(saved.DefaultCosDiscountPercent == 12.5m, "Invalid edit changed the saved discount.");
            controller.ModelState.Clear();
            ExcelPackage.Configure(options => options.Configuration = new ConfigurationBuilder()
                .SetBasePath(Path.GetFullPath("IBSWeb")).AddJsonFile("appsettings.json").Build());
            var export = (FileContentResult)await controller.Export(saved.CustomerId.ToString());
            using var package = new ExcelPackage(new MemoryStream(export.FileContents));
            Check(package.Workbook.Worksheets[0].Cells[1, 14].Text == "DefaultCosDiscountPercent"
                && package.Workbook.Worksheets[0].Cells[2, 14].GetValue<decimal>() == 12.5m,
                "Customer export lost the default discount.");
            var masterController = new MasterFileController(users, work, NullLogger<MasterFileController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = tempData.GetTempData(http)
            };
            var masterExport = (FileStreamResult)await masterController.GenerateExcel("customer", default, null!);
            using var masterPackage = new ExcelPackage(masterExport.FileStream);
            Check(masterPackage.Workbook.Worksheets[0].Cells[6, 13].Text == "DEFAULT COS DISCOUNT (%)"
                && masterPackage.Workbook.Worksheets[0].Cells[7, 13].GetValue<decimal>() == 12.5m,
                "Customer master export lost the default discount.");

            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE msap.customers SET default_cos_discount_percent = 101 WHERE customer_id = {saved.CustomerId}");
                throw new InvalidOperationException("Database allowed an out-of-range customer discount.");
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.CheckViolation)
            {
                await transaction.RollbackAsync();
            }
            Console.WriteLine("PASS: customer default discount validates 0–100%, persists through create/edit, survives projection/export, records audits and rejects invalid edits/direct writes.");
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
