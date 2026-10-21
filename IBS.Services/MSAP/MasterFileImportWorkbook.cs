using System.Globalization;
using System.IO.Compression;
using System.Text;
using CsvHelper;
using IBS.Models.MSAP.ViewModels;
using Microsoft.AspNetCore.Http;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace IBS.Services.MSAP
{
    public static class MasterFileImportWorkbook
    {
        public static async Task<byte[]> WriteAsync(List<MasterFileImportDocument>? documents, CancellationToken ct,
            MasterFileImportViewModel? review = null)
        {
            using var package = new ExcelPackage();
            bool corrections = review != null;
            var results = review?.Items.ToDictionary(item => (item.FileName, item.Row));
            var issues = new List<(string File, int Row, string Field, string Note)>();
            IEnumerable<string> kinds = documents == null ? MasterFileImportColumns.Kinds : documents.Select(document => document.Kind);
            foreach (string kind in kinds)
            {
                var source = documents?.Single(document => document.Kind == kind);
                var columns = MasterFileImportColumns.For(kind);
                var sheet = package.Workbook.Worksheets.Add(kind);
                int notesColumn = columns.Count + 1;
                for (int index = 0; index < columns.Count; index++)
                {
                    var column = columns[index];
                    var header = sheet.Cells[1, index + 1];
                    header.Value = column.Label;
                    header.AddComment(column.Note, "MSAP");
                    sheet.Column(index + 1).Style.Numberformat.Format = kind == "tariff" && column.Key is "DISPATCH" or "BAF" ? "0.0000" : "@";
                    sheet.Column(index + 1).Width = column.Key is "NAME" or "DESC" or "ADDRESS1" or "ADDRESS2" or "ADDRESS3" ? 36 : 24;
                    for (int rowIndex = 0; rowIndex < (source?.Rows.Count ?? 0); rowIndex++)
                    {
                        var row = new MasterFileImportCsv.Row(source!.Rows[rowIndex].Row, source.Rows[rowIndex].Fields);
                        string value = row.Text(column.Key);
                        if (column.Choices.Length > 0 && value.Length > 0)
                        {
                            if (column.Key == "TYPE" && value.ToUpperInvariant() is "L" or "LOCAL" or "F" or "FOREIGN")
                            {
                                value = value.ToUpperInvariant() is "L" or "LOCAL" ? "L" : "F";
                            }
                            else if (column.Choices.Contains("True"))
                            {
                                value = value.ToUpperInvariant() switch { "TRUE" or "T" or "1" => "True", "FALSE" or "F" or "0" => "False", _ => value };
                            }
                        }
                        if (value.Length > 0 && column.Key is "NUMBER" or "AGENT" or "CUSTNO" or "PORT" or "OWNER" or "SERVICE")
                        {
                            int width = (column.Key is "AGENT" or "CUSTNO" || (column.Key == "NUMBER" && kind is "customer" or "principal" or "vessel")) ? 4 : 3;
                            if (value.All(character => character is >= '0' and <= '9'))
                            {
                                value = value.PadLeft(width, '0');
                            }
                        }
                        var cell = sheet.Cells[rowIndex + 2, index + 1];
                        cell.Value = kind == "tariff" && column.Key is "DISPATCH" or "BAF"
                            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) ? amount : value;
                        if (!corrections)
                        {
                            continue;
                        }
                        var result = results!.GetValueOrDefault((source.FileName, row.Number));
                        bool missing = MasterFileImportColumns.Missing(column, row, kind);
                        bool invalid = result?.Action == "Invalid" && result.Changes.Contains(column.Key, StringComparison.OrdinalIgnoreCase);
                        if (missing || invalid)
                        {
                            string note = (missing ? "Not supplied. " : result!.Changes + " ") + column.Note;
                            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            cell.Style.Fill.BackgroundColor.SetColor(missing ? System.Drawing.Color.LightYellow : System.Drawing.Color.MistyRose);
                            cell.AddComment(note, "MSAP");
                            issues.Add((source.FileName, row.Number, column.Label, note));
                        }
                    }
                    if (column.Choices.Length > 0)
                    {
                        var validation = sheet.DataValidations.AddListValidation(sheet.Cells[2, index + 1, Math.Max(1001, (source?.Rows.Count ?? 0) + 1), index + 1].Address);
                        foreach (string choice in column.Choices)
                        {
                            validation.Formula.Values.Add(choice);
                        }
                        validation.AllowBlank = true;
                        validation.ShowErrorMessage = true;
                        validation.Error = "Choose a listed value, or leave unknown values blank for confirmation.";
                    }
                }
                if (corrections)
                {
                    sheet.Cells[1, notesColumn].Value = "Correction notes";
                    sheet.Column(notesColumn).Width = 85;
                    sheet.Column(notesColumn).Style.WrapText = true;
                    var rowIssues = issues.Where(issue => issue.File == source?.FileName).ToLookup(issue => issue.Row);
                    for (int index = 0; index < (source?.Rows.Count ?? 0); index++)
                    {
                        var row = source!.Rows[index];
                        var result = results!.GetValueOrDefault((source.FileName, row.Row));
                        var notes = rowIssues[row.Row]
                            .Select(issue => issue.Field + ": " + issue.Note).ToList();
                        if (result?.Action == "Invalid")
                        {
                            notes.Add("Validation: " + result.Changes);
                            issues.Add((source.FileName, row.Row, "Record validation", result.Changes));
                        }
                        if (!string.IsNullOrWhiteSpace(result?.Warning))
                        {
                            notes.Add("Review: " + result.Warning);
                            issues.Add((source.FileName, row.Row, "Review", result.Warning));
                        }
                        sheet.Cells[index + 2, notesColumn].Value = string.Join("\n", notes);
                    }
                }
                Format(sheet, corrections ? notesColumn : columns.Count, Math.Max(2, (source?.Rows.Count ?? 0) + 1));
            }
            if (review != null)
            {
                var issueSheet = package.Workbook.Worksheets.Add("Issues");
                string[] headers = ["File", "Source row", "Field", "What to confirm or correct"];
                for (int index = 0; index < headers.Length; index++)
                {
                    issueSheet.Cells[1, index + 1].Value = headers[index];
                }
                int next = 2;
                foreach (var issue in issues)
                {
                    issueSheet.Cells[next, 1].Value = issue.File;
                    issueSheet.Cells[next, 2].Value = issue.Row;
                    issueSheet.Cells[next, 3].Value = issue.Field;
                    issueSheet.Cells[next++, 4].Value = issue.Note;
                }
                foreach (string error in review.Errors.Where(error => !System.Text.RegularExpressions.Regex.IsMatch(error, @", row \d+:")))
                {
                    issueSheet.Cells[next, 3].Value = "File / batch";
                    issueSheet.Cells[next++, 4].Value = error;
                }
                issueSheet.Column(1).Width = 25;
                issueSheet.Column(3).Width = 30;
                issueSheet.Column(4).Width = 100;
                issueSheet.Column(4).Style.WrapText = true;
                Format(issueSheet, 4, Math.Max(2, next - 1));
                var files = package.Workbook.Worksheets.Add("File status");
                string[] fileHeaders = ["Expected CSV / worksheet", "Supplied rows", "Stored rows", "Availability / action"];
                for (int index = 0; index < fileHeaders.Length; index++)
                {
                    files.Cells[1, index + 1].Value = fileHeaders[index];
                }
                int fileRow = 2;
                foreach (string kind in MasterFileImportColumns.Kinds)
                {
                    var availability = review.Availability.FirstOrDefault(item => item.File == kind + ".csv");
                    int supplied = availability?.SuppliedRows ?? documents?.FirstOrDefault(document => document.Kind == kind)?.Rows.Count ?? 0;
                    files.Cells[fileRow, 1].Value = kind + ".csv";
                    files.Cells[fileRow, 2].Value = supplied;
                    files.Cells[fileRow, 3].Value = availability?.StoredRows;
                    files.Cells[fileRow++, 4].Value = supplied > 0 ? "Supplied in this batch"
                        : availability?.StoredRows > 0 ? "Not selected; master records already exist"
                        : availability?.StoredRows == 0 ? "Not supplied and no stored rows. Supply if needed for this workflow."
                        : "Not selected; stored availability was not checked.";
                }
                files.Column(1).Width = 32;
                files.Column(4).Width = 90;
                Format(files, 4, fileRow - 1);
            }
            var notesSheet = package.Workbook.Worksheets.Add("Read me");
            string[] instructions =
            [
                corrections ? "CORRECTION REQUEST — not an approval or a completed import. All supplied records are retained; yellow/red cells and Correction notes identify the work. No database records were changed by export." : "MSAP master-file template / cleaned data. Codes, account numbers and TINs are text; preserve leading zeros.",
                "Complete highlighted cells using confirmed master data. Cell comments and the Issues sheet explain missing/invalid fields. Do not replace unknown values with False, 0, fake TINs or ZIP codes.",
                "Customer withholding tax (2307), withholding VAT (2306), actual VAT type and default COS discount are separate confirmations. False/0 are valid when explicitly confirmed.",
                "Legacy VAT registered=False does not distinguish Exempt from Zero-Rated. Supply VAT type per customer; do not guess from a sales transaction.",
                "Payment term days: 0 is COD. Positive days require the matching shared IBS payment term. Port-terminal uses 001-002; related master codes are not database IDs.",
                "File status distinguishes masters absent from this batch from masters absent in the database. Importing one file at a time does not itself make the other files invalid.",
                "Keep master worksheet names and headers. Return the completed .xlsx directly to MSAP, or save a master sheet as UTF-8 CSV under its sheet name. Issues, File status, Read me and Correction notes are not imported.",
                "The returned file is validated again and compared to current stored data. Import/approval is never automatic; rates still become independent Tariff/BAF review drafts.",
                "Blank added fields preserve existing settings on matched masters; new records require explicit withholding/discount/IOC confirmations. Source omissions still need business review even when an existing setting is retained.",
                "Legacy source ID is optional provenance. Do not renumber codes, merge names or delete records to hide an issue. Original DBF-converted CSVs remain unchanged."
            ];
            for (int index = 0; index < instructions.Length; index++)
            {
                notesSheet.Cells[index + 1, 1].Value = instructions[index];
                notesSheet.Row(index + 1).Height = 52;
            }
            notesSheet.Column(1).Width = 115;
            notesSheet.Column(1).Style.WrapText = true;
            return await package.GetAsByteArrayAsync(ct);
        }

        private static void Format(ExcelWorksheet sheet, int columns, int lastRow)
        {
            sheet.Cells[1, 1, 1, columns].Style.Font.Bold = true;
            sheet.Cells[1, 1, 1, columns].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, 1, 1, columns].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            sheet.Cells[1, 1, lastRow, columns].AutoFilter = true;
            sheet.View.FreezePanes(2, 3);
        }

        public static async Task<List<(string Kind, List<MasterFileImportCsv.Row> Rows)>> ReadAsync(IFormFile file, CancellationToken ct)
        {
            if (file.Length is <= 0 or > 10 * 1024 * 1024)
            {
                throw new InvalidDataException("Each workbook must contain data and be no larger than 10 MB.");
            }
            await using var stream = file.OpenReadStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                if (zip.Entries.Sum(entry => entry.Length) > 50 * 1024 * 1024)
                {
                    throw new InvalidDataException("Workbook expands beyond the supported 50 MB limit.");
                }
            }
            stream.Position = 0;
            using var package = new ExcelPackage(stream);
            var result = new List<(string Kind, List<MasterFileImportCsv.Row> Rows)>();
            foreach (var sheet in package.Workbook.Worksheets)
            {
                string kind = sheet.Name.ToLowerInvariant();
                if (!MasterFileImportColumns.Kinds.Contains(kind) || sheet.Dimension == null || sheet.Dimension.End.Row < 2)
                {
                    continue;
                }
                if (sheet.Dimension.End.Row > 10001 || sheet.Dimension.End.Column > 50)
                {
                    throw new InvalidDataException($"{sheet.Name}: maximum 10,000 records and 50 columns.");
                }
                var columns = MasterFileImportColumns.For(kind);
                var selected = Enumerable.Range(1, sheet.Dimension.End.Column).Where(index =>
                    columns.Any(column => column.Key.Equals(sheet.Cells[1, index].Text.Trim(), StringComparison.OrdinalIgnoreCase)
                        || column.Label.Equals(sheet.Cells[1, index].Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    || sheet.Cells[1, index].Text.Equals("VAT registered", StringComparison.OrdinalIgnoreCase)).ToList();
                if (selected.Count == 0)
                {
                    throw new InvalidDataException($"{sheet.Name}: no recognized master headers. Keep the supplied header row.");
                }
                using var text = new StringWriter(CultureInfo.InvariantCulture);
                int dataRows = 0;
                using (var csv = new CsvWriter(text, CultureInfo.InvariantCulture, true))
                {
                    foreach (int index in selected)
                    {
                        csv.WriteField(sheet.Cells[1, index].Text);
                    }
                    await csv.NextRecordAsync();
                    for (int row = 2; row <= sheet.Dimension.End.Row; row++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (selected.All(index => string.IsNullOrWhiteSpace(sheet.Cells[row, index].Text)))
                        {
                            continue;
                        }
                        dataRows++;
                        foreach (int index in selected)
                        {
                            var cell = sheet.Cells[row, index];
                            if (!string.IsNullOrEmpty(cell.Formula))
                            {
                                throw new InvalidDataException($"{sheet.Name}, row {row}: enter actual values, not formulas.");
                            }
                            csv.WriteField(cell.Text);
                        }
                        await csv.NextRecordAsync();
                    }
                    await csv.FlushAsync();
                }
                if (dataRows == 0)
                {
                    continue;
                }
                await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
                result.Add((kind, await MasterFileImportCsv.ReadAsync(new FormFile(bytes, 0, bytes.Length, "Files", kind + ".csv"), ct)));
            }
            if (result.Count == 0)
            {
                throw new InvalidDataException("Workbook has no populated supported master worksheets.");
            }
            return result;
        }
    }
}
