using System.Text;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;

namespace Checks.MSAP
{
    internal static class MasterFileImportCheck
    {
        public static async Task RunAsync()
        {
            Check(MasterFileImportCsv.Kind("services.csv") == "services" && MasterFileImportCsv.Kind("SERVICE.CSV") == "services", "Service aliases failed.");
            Check(MasterFileImportCsv.Kind("tariff.csv") == "tariff", "Tariff migration staging was disabled.");
            foreach (string file in new[] { "billing.csv", "collection.csv", "dispatch.csv", "userfile.csv", "useraccess.csv", "customer-backup.csv" })
            {
                Check(MasterFileImportCsv.Kind(file) == null, $"Unsupported file accepted: {file}");
            }
            var rows = await ReadAsync("customer.csv", "RECID,NUMBER,NAME,ADDRESS1,ADDRESS2,ADDRESS3,TIN,TERMS,VATABLE,ACTIVE\n1,0015,Example,One-Harbor Road,,,---,3,False,True\n");
            var row = rows.Single();
            var friendly = await ReadAsync("port.csv", "Legacy source ID (optional),Port code,Port name\n1,001,Batangas\n");
            Check(friendly.Single().Code("NUMBER", 3) == "001" && friendly.Single().Required("NAME") == "Batangas", "Readable export headers cannot be reused as CSV.");
            Check(row.Code("NUMBER", 4) == "0015" && !row.Flag("VATABLE") && row.Flag("ACTIVE"), "Codes or explicit flags changed.");
            Check(row.PaymentTerms() == "3D" && row.Text("TIN") == string.Empty && row.Text("ADDRESS1") == "One-Harbor Road", "Terms, missing TIN or address punctuation corrupted.");
            var empty = new MasterFileImportCsv.Row(2, new Dictionary<string, string> { ["VATABLE"] = "", ["COMPANYOWN"] = "", ["TERMS"] = "", ["NUMBER"] = "001-002" });
            MustReject(() => empty.Flag("VATABLE"));
            MustReject(() => empty.Flag("COMPANYOWN"));
            MustReject(() => empty.PaymentTerms());
            Check(empty.TerminalCode() == ("001", "002"), "Terminal composite key was not preserved.");
            var original = new MasterFileImportDocument { Kind = "customer", FileName = "customer.csv", Columns = MasterFileImportColumns.For("customer"),
                Rows = [new MasterFileImportRow { Row = 2, Fields = new Dictionary<string, string> { ["NUMBER"] = "0015", ["VATABLE"] = "", ["CREATEDBY"] = "legacy" } }] };
            var edit = new MasterFileImportDocument { Kind = "customer", FileName = "customer.csv",
                Rows = [new MasterFileImportRow { Row = 2, Fields = new Dictionary<string, string> { ["NUMBER"] = "0015", ["VATABLE"] = "False", ["CREATEDBY"] = "legacy" } }] };
            MasterFileImportService.ApplyEdits([original], [edit]);
            Check(original.Rows[0].Fields["VATABLE"] == "False", "An explicit false correction was lost.");
            edit.Rows[0].Fields["CREATEDBY"] = "forged";
            MustReject(() => MasterFileImportService.ApplyEdits([original], [edit]));
            edit.Rows[0].Fields["CREATEDBY"] = "legacy";
            edit.Rows[0].Row = 99;
            MustReject(() => MasterFileImportService.ApplyEdits([original], [edit]));
            original.Rows[0].Fields["NAME"] = "=1+1";
            ExcelPackage.Configure(options => options.Configuration = new ConfigurationBuilder()
                .SetBasePath(Path.GetFullPath("IBSWeb")).AddJsonFile("appsettings.json").Build());
            using (var workbook = new ExcelPackage(new MemoryStream(await MasterFileImportWorkbook.WriteAsync([original], default))))
            {
                Check(workbook.Workbook.Worksheets["customer"].Cells[1, 1].Text == "Customer code"
                    && workbook.Workbook.Worksheets["customer"].Cells[2, 1].Text == "0015", "Clean export lost readable headers or leading zeros.");
                Check(string.IsNullOrEmpty(workbook.Workbook.Worksheets["customer"].Cells[2, 1].Formula), "Export interpreted a source value as a formula.");
                Check(workbook.Workbook.Worksheets["customer"].Cells[2, 2].Text == "=1+1"
                    && string.IsNullOrEmpty(workbook.Workbook.Worksheets["customer"].Cells[2, 2].Formula), "Source text became an executable spreadsheet formula.");
            }
            using (var template = new ExcelPackage(new MemoryStream(await MasterFileImportWorkbook.WriteAsync(null, default))))
            {
                Check(template.Workbook.Worksheets["tariff"].Cells[1, 6].Text == "Tariff amount", "Template omits migration rate headers.");
            }
            var review = new MasterFileImportViewModel
            {
                Documents = [original],
                Errors = ["customer.csv, row 2: WHT must be confirmed."],
                Items = [new MasterFileImportItem { FileName = "customer.csv", Row = 2, Action = "Invalid", Changes = "WHT must be confirmed." }],
                Availability = [new MasterFileImportAvailability { File = "port.csv", StoredRows = 12 }]
            };
            byte[] correctionBytes = await MasterFileImportWorkbook.WriteAsync(review.Documents, default, review);
            using (var corrections = new ExcelPackage(new MemoryStream(correctionBytes)))
            {
                var customer = corrections.Workbook.Worksheets["customer"];
                int whtColumn = MasterFileImportColumns.For("customer").FindIndex(column => column.Key == "WHT") + 1;
                Check(customer.Cells[2, whtColumn].Comment != null && customer.Cells[2, whtColumn].Text == "",
                    "Missing withholding was not annotated or was silently defaulted.");
                Check(customer.Cells[2, customer.Dimension.End.Column].Text.Contains("WHT")
                    && corrections.Workbook.Worksheets["Issues"].Dimension.End.Row > 1, "Correction report lost row notes or issues.");
                Check(corrections.Workbook.Worksheets["File status"].Cells[4, 4].Text.Contains("already exist"),
                    "Unselected existing masters were reported as absent.");
                customer.Cells[2, whtColumn].Value = "False";
                corrections.Workbook.Worksheets.Add("port").Cells[1, 1].Value = "Port code";
                corrections.Workbook.Worksheets["port"].Cells[2, 1].Style.Numberformat.Format = "@";
                byte[] completed = await corrections.GetAsByteArrayAsync();
                using var stream = new MemoryStream(completed);
                var returned = await MasterFileImportWorkbook.ReadAsync(new FormFile(stream, 0, stream.Length, "Files", "completed.xlsx"), default);
                Check(returned.Count == 1 && returned[0].Kind == "customer"
                    && returned[0].Rows[0].Code("NUMBER", 4) == "0015" && !returned[0].Rows[0].Flag("WHT")
                    && !returned[0].Rows[0].Fields.ContainsKey("Correction notes"), "Returned workbook lost corrections or imported report notes.");
                customer.Cells[2, 2].Formula = "1+1";
                byte[] formulaBytes = await corrections.GetAsByteArrayAsync();
                using var formulaStream = new MemoryStream(formulaBytes);
                bool rejected = false;
                try
                {
                    await MasterFileImportWorkbook.ReadAsync(new FormFile(formulaStream, 0, formulaStream.Length, "Files", "formula.xlsx"), default);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Check(rejected, "Returned workbook accepted executable formulas.");
            }
            foreach (string malformed in new[] { "RECID,NUMBER\n1,001\n", "RECID,NUMBER,DESC,DESC\n1,001,A,B\n", "RECID,NUMBER,DESC\n1,001,A,EXTRA\n" })
            {
                try
                {
                    await ReadAsync("services.csv", malformed);
                    throw new InvalidOperationException("Invalid CSV was accepted.");
                }
                catch (Exception ex) when (ex is InvalidDataException or CsvHelper.CsvHelperException)
                {
                }
            }
            using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
            Check(db.Model.FindEntityType(typeof(Port))!.FindProperty("HasSBMA") == null
                && MasterFileImportColumns.For("port").All(column => column.Key != "SBMA")
                && db.Model.FindEntityType(typeof(IBS.Models.MSAP.VesselSchedule))!.FindProperty("SbmaEnabled") != null,
                "SBMA must be controlled by the schedule, not the port master.");
            Check(db.Model.FindEntityType(typeof(BankAccount))!.GetTableName() == "bank_accounts"
                && db.Model.FindEntityType(typeof(Terms))!.GetTableName() == "terms", "Import transaction lock table names no longer match EF mapping.");

            Check(!db.Database.HasPendingModelChanges(), "MSAP migration snapshot does not match the current model.");
            var migrator = db.GetService<IMigrator>();
            string lastMigration = db.Database.GetMigrations().Last();
            string cleanupSql = migrator.GenerateScript("20261018082734_RemoveOtherTugSupplierLinks", lastMigration);
            Check(cleanupSql.Contains("DROP COLUMN has_sbma") && cleanupSql.Contains("DROP COLUMN commission_rate")
                && !cleanupSql.Contains("ALTER TABLE msap.mmsi_vessel_schedules"),
                "Cleanup migration SQL does not preserve the schedule SBMA setting.");
            string rollbackSql = migrator.GenerateScript(lastMigration, "20261018082734_RemoveOtherTugSupplierLinks");
            Check(rollbackSql.Contains("ADD has_sbma") && rollbackSql.Contains("ADD commission_rate"),
                "Cleanup rollback does not restore the retired columns.");


            string folder = Path.Combine("Docs", "MSAP", "CSVs for Imports");
            if (Directory.Exists(folder))
            {
                int files = 0;
                int records = 0;
                int unknownFlags = 0;
                foreach (string path in Directory.EnumerateFiles(folder, "*.csv").Where(path => MasterFileImportCsv.Kind(path) != null))
                {
                    await using var stream = File.OpenRead(path);
                    var file = new FormFile(stream, 0, stream.Length, "Files", Path.GetFileName(path));
                    var parsed = await MasterFileImportCsv.ReadAsync(file, CancellationToken.None);
                    files++;
                    records += parsed.Count;
                    foreach (var item in parsed)
                    {
                        foreach (string flag in new[] { "VATABLE", "COMPANYOWN" }.Where(item.Fields.ContainsKey))
                        {
                            if (item.Text(flag).Length == 0)
                            {
                                unknownFlags++;
                            }
                        }
                    }
                }
                Console.WriteLine($"Source CSV inspection: {files} master/rate files, {records} rows, {unknownFlags} blank VAT/ownership flags needing confirmation. No database used.");
            }
            foreach (string field in new[] { "CommissioneeId", "CommissionRate", "Commissionees", "Commissionee", "StationCode",
                "CreditLimit", "CreditLimitAsOfToday", "RetentionRate", "HasMultipleTerms", "RequiresPriceAdjustment" })
            {
                Check(typeof(Customer).GetProperty(field) == null, $"Unused IBS customer field remains in MSAP: {field}");
            }
            Console.WriteLine("PASS: master/rate staging allowlist, correction boundaries, workbook headers/codes, explicit flags, terms, EF metadata and obsolete customer-field removal. No import executed.");
        }

        private static async Task<List<MasterFileImportCsv.Row>> ReadAsync(string name, string contents)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
            return await MasterFileImportCsv.ReadAsync(new FormFile(stream, 0, stream.Length, "Files", name), CancellationToken.None);
        }

        private static void MustReject(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidOperationException("Missing source value was silently converted to a known value.");
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
