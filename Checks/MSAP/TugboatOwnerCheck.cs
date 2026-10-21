using System.Reflection;
using System.Text.Json;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Checks.MSAP
{
    internal static class TugboatOwnerCheck
    {
        public static string TestConnection()
        {
            IConfigurationRoot config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("IBSWeb"))
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
                .AddUserSecrets(Assembly.Load("IBSWeb")).AddEnvironmentVariables().Build();
            var settings = new NpgsqlConnectionStringBuilder(config.GetConnectionString("MMSIConnection"));
            string? database = Environment.GetEnvironmentVariable("IBS_REBUILD_TEST_DATABASE");
            if (settings.Host is not ("localhost" or "127.0.0.1") || database == null
                || !database.StartsWith("ibs_workspace_check_", StringComparison.Ordinal) || database == settings.Database)
            {
                throw new InvalidOperationException("Set IBS_REBUILD_TEST_DATABASE to a retained local workspace-check database.");
            }
            settings.Database = database;
            return settings.ConnectionString;
        }

        public static async Task RunAsync()
        {
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(TestConnection(), pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options);
            Dictionary<int, string> identity = await db.MsapTugboatOwners.Select(x => new { x.TugboatOwnerId, x.TugboatOwnerNumber })
                .ToDictionaryAsync(x => x.TugboatOwnerId, x => x.TugboatOwnerNumber);
            await db.Database.MigrateAsync();
            Check(!db.Database.HasPendingModelChanges(), "Owner-profile migration differs from the model.");
            string[] migrations = db.Database.GetMigrations().ToArray();
            int profile = Array.FindIndex(migrations, x => x.EndsWith("_AddTugboatOwnerPaymentProfile", StringComparison.Ordinal));
            Check(profile > 0, "Owner-profile migration was not found.");
            string sql = db.GetService<IMigrator>().GenerateScript(migrations[profile - 1], migrations[profile]);
            Check(!sql.Contains("UPDATE ") && !sql.Contains("DROP TABLE"), "Owner migration contains a data repair or table replacement.");
            await db.GetService<IMigrator>().MigrateAsync(migrations[profile - 1]);
            await db.Database.MigrateAsync();
            Check(identity.OrderBy(x => x.Key).SequenceEqual((await db.MsapTugboatOwners
                .ToDictionaryAsync(x => x.TugboatOwnerId, x => x.TugboatOwnerNumber)).OrderBy(x => x.Key)), "Owner migration changed master identities.");

            await using var transaction = await db.Database.BeginTransactionAsync();
            await using var shared = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(TestConnection()).Options);
            var work = new UnitOfWork(db, shared);
            var service = new TugboatOwnerService(work, NullLogger<TugboatOwnerService>.Instance);
            var owner = new TugboatOwner { TugboatOwnerNumber = "ZQ1", TugboatOwnerName = "Owner profile check" };
            Check(TugboatOwnerService.ValidatePaymentProfile(owner) == null
                && TugboatOwnerService.ValidatePaymentProfile(owner, true) != null, "An incomplete imported profile is payable.");
            owner.Address = "Check address";
            Check(!(await service.CreateAsync(owner, "Check", default)).IsSuccess, "A partially completed profile was saved.");
            owner.Tin = "000-000-000-00000";
            owner.VatType = "Vatable";
            owner.TaxType = "Withholding Tax";
            owner.WithholdingTaxPercent = 0.03m;
            Check(!(await service.CreateAsync(owner, "Check", default)).IsSuccess, "An unsupported withholding rate was saved.");
            owner.WithholdingTaxPercent = 0.02m;
            Check((await service.CreateAsync(owner, "Check", default)).IsSuccess, "A complete owner profile could not be saved.");
            db.ChangeTracker.Clear();
            var saved = await db.MsapTugboatOwners.SingleAsync(x => x.TugboatOwnerId == owner.TugboatOwnerId);
            Check(saved.Address == owner.Address && saved.Tin == owner.Tin && saved.WithholdingTaxPercent == 0.02m,
                "Owner profile did not persist correctly.");
            saved.TaxType = "Exempt";
            Check((await service.UpdateAsync(saved, "Check", default)).IsSuccess, "Owner profile update failed.");
            await db.Entry(saved).ReloadAsync();
            Check(saved.WithholdingTaxPercent == 0m, "An exempt owner retained a withholding rate.");
            Check(await db.AuditTrails.CountAsync(x => x.Username == "Check" && x.Activity.Contains("ZQ1")) == 2,
                "Owner maintenance did not record its create/update audit entries.");
            var expectedTugboats = await db.MsapTugboats.AsNoTracking()
                .Select(x => new
                {
                    x.TugboatId,
                    OwnerName = x.TugboatOwner == null ? null : x.TugboatOwner.TugboatOwnerName,
                    x.Port.PortName
                }).ToDictionaryAsync(x => x.TugboatId);
            Check(expectedTugboats.Values.Any(x => x.OwnerName != null), "Tugboat list check requires an assigned owner.");
            db.ChangeTracker.Clear();
            var tugboatService = new TugboatService(work, NullLogger<TugboatService>.Instance);
            IEnumerable<Tugboat> tugboats = await tugboatService.GetAllAsync(default);
            using JsonDocument list = JsonSerializer.SerializeToDocument(new { data = tugboats }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            JsonElement rows = list.RootElement.GetProperty("data");
            Check(rows.GetArrayLength() == expectedTugboats.Count, "Tugboat list lost records.");
            foreach (JsonElement row in rows.EnumerateArray())
            {
                var expected = expectedTugboats[row.GetProperty("tugboatId").GetInt32()];
                JsonElement ownerJson = row.GetProperty("tugboatOwner");
                string? ownerName = ownerJson.ValueKind == JsonValueKind.Null ? null : ownerJson.GetProperty("tugboatOwnerName").GetString();
                Check(ownerName == expected.OwnerName, "Tugboat list did not return its assigned owner's name.");
                JsonElement portJson = row.GetProperty("port");
                Check(portJson.ValueKind != JsonValueKind.Null && portJson.GetProperty("portName").GetString() == expected.PortName,
                    "Tugboat list did not return its port name.");
            }
            await transaction.RollbackAsync();
            Console.WriteLine("PASS: owner migration preserves imported identities; incomplete/invalid payment profiles fail; valid/exempt profiles persist and audit correctly (isolated database).");
            Console.WriteLine("PASS: tugboat list JSON returns every tugboat with its assigned owner and port from a fresh change tracker.");
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
