using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IBS.DataAccess.MSAP.Data
{
    public static class DbSeeder
    {
        private static readonly (string Code, int Days, int Months)[] DefaultPaymentTerms =
        [
            ("COD", 0, 0),
            ("PREPAID", 0, 0),
            ("3D", 3, 0),
            ("7D", 7, 0),
            ("7PDC", 7, 0),
            ("10D", 10, 0),
            ("15D", 15, 0),
            ("15PDC", 15, 0),
            ("20D", 20, 0),
            ("21D", 21, 0),
            ("30D", 30, 0),
            ("30PDC", 30, 0),
            ("45D", 45, 0),
            ("45PDC", 45, 0),
            ("60D", 60, 0),
            ("60PDC", 60, 0),
            ("90D", 90, 0),
            ("M15", 15, 1),
            ("M29", 29, 1),
            ("M30", 0, 2),
            ("M30+1", 1, 2)
        ];

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<MsapDbContext>();
            var now = DateTimeHelper.GetCurrentPhilippineTime();

            // 1. Seed Company
            if (!await context.Companies.AnyAsync(c => c.CompanyName == "MMSI"))
            {
                var company = new Company
                {
                    CompanyCode = "MMS",
                    CompanyName = "MMSI",
                    CompanyAddress = "Office Address 14th Floor Jollibee Centre, San Miguel Ave., Pasig City",
                    CompanyTin = "000-000-000-000",
                    BusinessStyle = "Maritime Services",
                    IsActive = true,
                    CreatedBy = "SYSTEM",
                    CreatedDate = now
                };
                context.Companies.Add(company);
                await context.SaveChangesAsync();
            }

            // 2. Seed MSAP Payment Terms (initial seed only so user deletions persist)
            if (!await context.Terms.AnyAsync())
            {
                var initialTerms = DefaultPaymentTerms
                    .Select(t => new Terms
                    {
                        TermsCode = t.Code,
                        NumberOfDays = t.Days,
                        NumberOfMonths = t.Months,
                        CreatedBy = "SYSTEM",
                        CreatedDate = now,
                        EditedBy = string.Empty
                    })
                    .ToList();

                context.Terms.AddRange(initialTerms);
                await context.SaveChangesAsync();
            }
        }
    }
}
