using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.MasterFile;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Checks.MSAP
{
    internal static class CustomerCodeDatabaseCheck
    {
        public static async Task RunAsync()
        {
            var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("IBSWeb"))
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
                .AddEnvironmentVariables().Build();
            var source = new NpgsqlConnectionStringBuilder(config.GetConnectionString("MMSIConnection"));
            Check(source.Host is "localhost" or "127.0.0.1", "Customer-code checks require a local PostgreSQL server.");
            await using (var existing = new NpgsqlConnection(source.ConnectionString))
            {
                await existing.OpenAsync();
                await using var duplicates = new NpgsqlCommand(
                    "SELECT count(*) FROM (SELECT customer_code FROM msap.customers GROUP BY customer_code HAVING count(*) > 1) codes",
                    existing);
                var duplicateGroups = (long)(await duplicates.ExecuteScalarAsync())!;
                Check(duplicateGroups == 0, $"The configured database has {duplicateGroups} duplicate customer codes; the unique index migration would fail.");
            }

            var database = "msap_customer_code_check_" + Guid.NewGuid().ToString("N");
            var serverConnection = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres", Pooling = false };
            var testConnection = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = database, Pooling = false };
            await using (var server = new NpgsqlConnection(serverConnection.ConnectionString))
            {
                await server.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", server);
                await create.ExecuteNonQueryAsync();
            }

            try
            {
                await using (var connection = new NpgsqlConnection(testConnection.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using var createSchema = new NpgsqlCommand("CREATE SCHEMA msap", connection);
                    await createSchema.ExecuteNonQueryAsync();
                }

                await using (var shared = new ApplicationDbContext(
                    new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseNpgsql(testConnection.ConnectionString)
                        .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                        .Options))
                {
                    await shared.Database.MigrateAsync("20261020094302_AddTugboatWorkspaceOwnership");
                }

                await using var db = CreateMsapContext(testConnection.ConnectionString);
                await db.Database.MigrateAsync(db.Database.GetMigrations().First());
                await db.Database.MigrateAsync();
                Check(!db.Database.HasPendingModelChanges(), "Fresh MSAP migrations differ from the current model.");
                var codeIndex = db.Model.FindEntityType(typeof(Customer))!.GetIndexes()
                    .Single(index => index.Properties.SingleOrDefault()?.Name == nameof(Customer.CustomerCode));
                Check(codeIndex.IsUnique, "CustomerCode must have a unique database index.");

                db.Customers.Add(CreateCustomer("0093", "Imported numeric customer"));
                await db.SaveChangesAsync();
                var repository = new CustomerRepository(db);
                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    Check(await repository.GenerateCodeAsync(nameof(CustomerType.Industrial)) == "IND0001",
                        "Industrial codes should start with the agreed prefix, independently of imported numeric codes.");
                    await transaction.RollbackAsync();
                }
                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    Check(await repository.GenerateCodeAsync(nameof(CustomerType.Reseller)) == "RES0001",
                        "Reseller codes should use their type prefix.");
                    await transaction.RollbackAsync();
                }

                using var barrier = new Barrier(2);
                var first = CreateRetailCustomerAsync("first", barrier, testConnection.ConnectionString);
                var second = CreateRetailCustomerAsync("second", barrier, testConnection.ConnectionString);
                var codes = await Task.WhenAll(first, second);
                Check(codes.Order().SequenceEqual(["RET0001", "RET0002"]),
                    $"Concurrent customer creates did not allocate distinct sequential codes: {string.Join(", ", codes)}.");

                await using var duplicateDb = CreateMsapContext(testConnection.ConnectionString);
                await using var duplicateTransaction = await duplicateDb.Database.BeginTransactionAsync();
                duplicateDb.Customers.Add(CreateCustomer(codes[0], "Duplicate code probe"));
                var duplicateRejected = false;
                try
                {
                    await duplicateDb.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    duplicateRejected = true;
                }
                await duplicateTransaction.RollbackAsync();
                Check(duplicateRejected, "The unique customer-code index accepted a duplicate.");

                var importedCode = await db.Customers.AsNoTracking()
                    .SingleAsync(customer => customer.CustomerName == "Imported numeric customer");
                Check(importedCode.CustomerCode == "0093", "Generating new codes changed an imported numeric code.");
                Console.WriteLine("PASS: fresh schema, preserved numeric codes, type prefixes, concurrent allocation and unique constraint.");
            }
            finally
            {
                await using var server = new NpgsqlConnection(serverConnection.ConnectionString);
                await server.OpenAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", server);
                await drop.ExecuteNonQueryAsync();
            }
        }

        private static async Task<string> CreateRetailCustomerAsync(string name, Barrier barrier, string connectionString)
        {
            await using var db = CreateMsapContext(connectionString);
            await using var transaction = await db.Database.BeginTransactionAsync();
            Check(barrier.SignalAndWait(TimeSpan.FromSeconds(30)), "Customer creation transactions did not synchronize.");
            var code = await new CustomerRepository(db).GenerateCodeAsync(nameof(CustomerType.Retail));
            db.Customers.Add(CreateCustomer(code, $"Concurrent {name}"));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return code;
        }

        private static MsapDbContext CreateMsapContext(string connectionString)
        {
            return new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(connectionString, options => options.MigrationsHistoryTable("__EFMigrationsHistory", "msap"))
                .Options);
        }

        private static Customer CreateCustomer(string code, string name)
        {
            return new Customer
            {
                CustomerCode = code,
                CustomerName = name,
                CustomerAddress = "Customer code check",
                CustomerTin = "000-000-000-00000",
                CustomerTerms = "COD",
                CustomerType = nameof(CustomerType.Retail),
                VatType = "Vatable",
                ZipCode = "0000",
                Company = "MMSI"
            };
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
