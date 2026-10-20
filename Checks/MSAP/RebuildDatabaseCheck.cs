using System.Reflection;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Checks.MSAP
{
    internal static class RebuildDatabaseCheck
    {
        public static async Task RunAsync()
        {
            Dictionary<string, string> original = await AuditSourceAsync();
            string? previous = Environment.GetEnvironmentVariable("IBS_REBUILD_TEST_DATABASE");
            try
            {
                string database = await TugboatWorkspaceDatabaseCheck.RunAsync();
                Environment.SetEnvironmentVariable("IBS_REBUILD_TEST_DATABASE", database);
                await TugboatOwnerCheck.RunAsync();
                await TicketIntegrityDatabaseCheck.RunAsync();
                await using var connection = new NpgsqlConnection(TugboatOwnerCheck.TestConnection());
                await connection.OpenAsync();
                await CheckConstraintsAsync(connection);
                await CheckReportVolumeAsync(connection);
                await WriteMigrationScriptsAsync(database);
                Console.WriteLine($"PASS: combined public/MSAP upgrade, rollback/reapply and workflow checks. Retained clone: {database}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("IBS_REBUILD_TEST_DATABASE", previous);
            }
            Dictionary<string, string> after = await AuditSourceAsync();
            Check(original.OrderBy(x => x.Key).SequenceEqual(after.OrderBy(x => x.Key)),
                "Working database changed during verification; inspect concurrent application activity before comparing it again.");
            Console.WriteLine("PASS: working database table fingerprints and migration histories are unchanged.");
        }

        public static async Task<Dictionary<string, string>> AuditSourceAsync()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("IBSWeb"))
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
                .AddUserSecrets(Assembly.Load("IBSWeb")).AddEnvironmentVariables().Build();
            var settings = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("MMSIConnection"));
            Check(settings.Host is "localhost" or "127.0.0.1", "Source audit only supports the configured local database.");
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync();
            }
            await CheckConstraintsAsync(connection);
            var fingerprints = new Dictionary<string, string>();
            var tables = new List<string>();
            await using (var command = new NpgsqlCommand("""
                SELECT format('%I.%I', schemaname, tablename) FROM pg_tables
                WHERE schemaname IN ('public', 'msap') ORDER BY schemaname, tablename
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            foreach (string table in tables)
            {
                // Identifiers come from PostgreSQL's quoted catalog output, never caller input.
                await using var command = new NpgsqlCommand($"""
                    SELECT count(*)::text || ':' || COALESCE(md5(string_agg(md5(to_jsonb(r)::text), '' ORDER BY md5(to_jsonb(r)::text))), '')
                    FROM {table} r
                    """, connection);
                fingerprints[table] = (string)(await command.ExecuteScalarAsync())!;
            }
            foreach (string table in tables.Where(x => x.Contains("mmsi_", StringComparison.Ordinal)
                || x.Contains("EFMigrationsHistory", StringComparison.Ordinal)))
            {
                Console.WriteLine($"Source read-only audit: {table} rows={fingerprints[table].Split(':')[0]}");
            }
            await using (var command = new NpgsqlCommand("""
                SELECT count(*) FROM msap.mmsi_dispatch_tickets
                WHERE num_nonnulls(date_left, time_left, date_arrived, time_arrived) BETWEEN 1 AND 3
                   OR date_arrived + time_arrived <= date_left + time_left
                """, connection))
            {
                Check((long)(await command.ExecuteScalarAsync())! == 0,
                    "Working database has partial/reversed actual tickets; the new constraint needs a data review before deployment.");
            }
            await transaction.RollbackAsync();
            Console.WriteLine($"PASS: read-only working database audit and content fingerprints ({tables.Count} tables).");
            return fingerprints;
        }

        private static async Task CheckConstraintsAsync(NpgsqlConnection connection)
        {
            await using (var command = new NpgsqlCommand("""
                SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
                WHERE n.nspname IN ('public', 'msap') AND NOT c.convalidated
                """, connection))
            {
                Check((long)(await command.ExecuteScalarAsync())! == 0, "The database has unvalidated constraints.");
            }
            await using (var command = new NpgsqlCommand("""
                SELECT count(*) FROM pg_index i JOIN pg_class t ON t.oid = i.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname IN ('public', 'msap') AND (NOT i.indisvalid OR NOT i.indisready)
                """, connection))
            {
                Check((long)(await command.ExecuteScalarAsync())! == 0, "The database has an invalid or unready index.");
            }
            var checks = new List<string>();
            await using (var command = new NpgsqlCommand("""
                SELECT format('SELECT %L AS name, count(*)::bigint AS failures FROM %I.%I c WHERE %s AND NOT EXISTS (SELECT 1 FROM %I.%I p WHERE %s)',
                    con.conname, ns.nspname, child.relname,
                    string_agg(format('c.%I IS NOT NULL', ca.attname), ' AND ' ORDER BY k.ord),
                    pn.nspname, parent.relname,
                    string_agg(format('p.%I = c.%I', pa.attname, ca.attname), ' AND ' ORDER BY k.ord))
                FROM pg_constraint con JOIN pg_class child ON child.oid = con.conrelid
                JOIN pg_namespace ns ON ns.oid = child.relnamespace
                JOIN pg_class parent ON parent.oid = con.confrelid JOIN pg_namespace pn ON pn.oid = parent.relnamespace
                CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY k(ck, pk, ord)
                JOIN pg_attribute ca ON ca.attrelid = child.oid AND ca.attnum = k.ck
                JOIN pg_attribute pa ON pa.attrelid = parent.oid AND pa.attnum = k.pk
                WHERE con.contype = 'f' AND ns.nspname IN ('public', 'msap')
                GROUP BY con.oid, ns.nspname, child.relname, pn.nspname, parent.relname ORDER BY con.oid
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    checks.Add(reader.GetString(0));
                }
            }
            if (checks.Count > 0)
            {
                await using var command = new NpgsqlCommand("SELECT * FROM (" + string.Join(" UNION ALL ", checks) + ") violations WHERE failures > 0", connection);
                await using var reader = await command.ExecuteReaderAsync();
                Check(!await reader.ReadAsync(), "An orphaned foreign-key relationship exists in the database.");
            }
            Console.WriteLine($"PASS: {checks.Count} foreign-key orphan scans, validated constraints and valid indexes.");
        }

        private static async Task CheckReportVolumeAsync(NpgsqlConnection connection)
        {
            await using var msap = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(TugboatOwnerCheck.TestConnection()).Options);
            int tugboat = await msap.MsapTugboats.Where(x => x.IsCompanyOwned).Select(x => x.TugboatId).FirstAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO filpride_general_ledger_books
                    (date, reference, description, account_id, account_no, account_title, debit, credit, created_by, created_date, is_posted, module_type, workspace_tugboat_id)
                SELECT DATE '2034-01-01' + (n % 365), 'VOLUME-CHECK', 'Disposable volume check', a.account_id, a.account_number,
                    a.account_name, 1, 0, 'Check', LOCALTIMESTAMP, true, 'Journal', CASE WHEN n % 50 = 0 THEN @tug ELSE NULL END
                FROM generate_series(1, 100000) n CROSS JOIN (SELECT account_id, account_number, account_name FROM filpride_chart_of_accounts LIMIT 1) a
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("tug", tugboat);
                Check(await insert.ExecuteNonQueryAsync() == 100000, "Ledger volume fixtures were not created.");
            }
            await using (var analyze = new NpgsqlCommand("ANALYZE filpride_general_ledger_books", connection, transaction))
            {
                await analyze.ExecuteNonQueryAsync();
            }
            await using (var explain = new NpgsqlCommand("""
                EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT)
                SELECT account_no, sum(debit - credit) FROM filpride_general_ledger_books
                WHERE workspace_tugboat_id = @tug AND date >= DATE '2034-01-01' AND date < DATE '2035-01-01'
                GROUP BY account_no
                """, connection, transaction))
            {
                explain.Parameters.AddWithValue("tug", tugboat);
                await using var reader = await explain.ExecuteReaderAsync();
                var lines = new List<string>();
                while (await reader.ReadAsync())
                {
                    lines.Add(reader.GetString(0));
                }
                Check(lines.Any(x => x.Contains("Index", StringComparison.Ordinal) && x.Contains("workspace", StringComparison.Ordinal)),
                    "The selected-workspace report did not use the ownership index at representative fixture volume.");
                Console.WriteLine("Ledger query plan (100,000 disposable rows):\n" + string.Join("\n", lines));
            }
            await transaction.RollbackAsync();
            Console.WriteLine("PASS: indexed workspace aggregation at fixture volume; all volume rows rolled back.");
        }

        private static async Task WriteMigrationScriptsAsync(string database)
        {
            string connection = TugboatOwnerCheck.TestConnection();
            await using var services = new ServiceCollection().AddLogging().AddSingleton<IBS.Utility.TugboatWorkspace>()
                .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection))
                .AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                .Services.BuildServiceProvider();
            await using var shared = services.GetRequiredService<ApplicationDbContext>();
            await using var msap = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(connection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options);
            string directory = Path.Combine(Path.GetTempPath(), "ibs-reimplementation-" + database);
            Directory.CreateDirectory(directory);
            foreach ((DbContext context, string name, string first) in new[]
            {
                ((DbContext)shared, "IBS", "_AddTugboatWorkspaceOwnership"),
                ((DbContext)msap, "MSAP", "_AddTugboatOwnerPaymentProfile")
            })
            {
                string[] migrations = context.Database.GetMigrations().ToArray();
                int start = Array.FindIndex(migrations, x => x.EndsWith(first, StringComparison.Ordinal));
                Check(start > 0, "Reimplementation migration boundary was not found.");
                IMigrator migrator = context.GetService<IMigrator>();
                string upgrade = migrator.GenerateScript(migrations[start - 1], migrations[^1]);
                string rollback = migrator.GenerateScript(migrations[^1], migrations[start - 1]);
                Check(!upgrade.Contains("UPDATE ", StringComparison.Ordinal) && !upgrade.Contains("DROP TABLE", StringComparison.Ordinal),
                    "Upgrade script contains data repair or table removal.");
                Check(rollback.Contains("DROP COLUMN", StringComparison.Ordinal), "Rollback is missing the new schema removal.");
                await File.WriteAllTextAsync(Path.Combine(directory, name + "-upgrade.sql"), upgrade);
                await File.WriteAllTextAsync(Path.Combine(directory, name + "-rollback.sql"), rollback);
            }
            Console.WriteLine("PASS: inspected focused upgrade/rollback scripts for both contexts: " + directory);
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
