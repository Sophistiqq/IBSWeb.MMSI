using IBS.Models.MSAP.MasterFile;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<MsapDbContext>();

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
                    CreatedDate = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(8), DateTimeKind.Unspecified)
                };
                context.Companies.Add(company);
                await context.SaveChangesAsync();
            }
        }
    }
}
