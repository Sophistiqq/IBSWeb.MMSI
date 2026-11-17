using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace IBS.Services.MSAP
{
    public sealed class RateWorkbookMasterLists
    {
        public List<string> Customers { get; init; } = [];
        public List<string> Ports { get; init; } = [];
        public List<string> Terminals { get; init; } = [];
        public List<string> Services { get; init; } = [];
    }

    public static class RateUploadWorkbook
    {
        public static async Task<byte[]> WriteAsync(
            List<RateUploadRowViewModel> rows,
            RateTableKind kind,
            bool history,
            CancellationToken ct,
            RateWorkbookMasterLists? masterLists = null)
        {
            using var package = new ExcelPackage();
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add(history ? $"{kind} history" : $"{kind} rates");
            string[] headers = ["CustomerCode", "Customer", "PortCode", "Port", "TerminalCode", "Terminal", "ServiceCode", "Service", "EffectiveDate", $"{kind}Amount"];
            if (history)
            {
                headers = [.. headers, "CreatedBy", "CreatedAt", "UpdatedBy", "ApprovalStatus"];
            }
            for (int column = 0; column < headers.Length; column++)
            {
                sheet.Cells[1, column + 1].Value = headers[column];
            }
            for (int index = 0; index < rows.Count; index++)
            {
                RateUploadRowViewModel rate = rows[index];
                int row = index + 2;
                sheet.Cells[row, 1].Value = rate.CustomerCode;
                sheet.Cells[row, 2].Value = rate.CustomerName;
                sheet.Cells[row, 3].Value = rate.PortCode;
                sheet.Cells[row, 4].Value = rate.PortName;
                sheet.Cells[row, 5].Value = rate.TerminalCode;
                sheet.Cells[row, 6].Value = rate.TerminalName;
                sheet.Cells[row, 7].Value = rate.ServiceCode;
                sheet.Cells[row, 8].Value = rate.ServiceName;
                sheet.Cells[row, 9].Value = rate.EffectiveDate.ToDateTime(TimeOnly.MinValue);
                sheet.Cells[row, 10].Value = rate.Amount;
                if (history)
                {
                    sheet.Cells[row, 11].Value = rate.CreatedBy;
                    sheet.Cells[row, 12].Value = rate.CreatedAt;
                    sheet.Cells[row, 13].Value = rate.UpdatedBy;
                    sheet.Cells[row, 14].Value = rate.ApprovalStatus;
                }
            }
            sheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
            sheet.Cells[1, 1, 1, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, 1, 1, headers.Length].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            sheet.Cells[1, 1, Math.Max(1, rows.Count + 1), headers.Length].AutoFilter = true;
            sheet.View.FreezePanes(2, 3);
            sheet.Column(9).Style.Numberformat.Format = "yyyy-mm-dd";
            sheet.Column(10).Style.Numberformat.Format = "#,##0.00";
            sheet.Column(12).Style.Numberformat.Format = "yyyy-mm-dd hh:mm";
            foreach (int column in new[] { 1, 3, 5, 7 })
            {
                sheet.Column(column).Style.Numberformat.Format = "@";
            }
            for (int column = 1; column <= headers.Length; column++)
            {
                sheet.Column(column).Width = column is 2 or 4 or 6 or 8 ? 32 : 22;
            }
            if (!history)
            {
                foreach (int column in new[] { 1, 3, 5, 7 })
                {
                    sheet.Column(column).Hidden = true;
                }
            }
            if (!history && masterLists != null)
            {
                var listsSheet = package.Workbook.Worksheets.Add("_MasterLists");
                listsSheet.Hidden = eWorkSheetHidden.Hidden;
                listsSheet.Cells[1, 1].Value = "Customers";
                listsSheet.Cells[1, 2].Value = "Ports";
                listsSheet.Cells[1, 3].Value = "Terminals";
                listsSheet.Cells[1, 4].Value = "Services";

                void WriteListColumn(int col, List<string> values, string targetColLetter)
                {
                    for (var i = 0; i < values.Count; i++)
                    {
                        listsSheet.Cells[i + 2, col].Value = values[i];
                    }
                    if (values.Count == 0)
                    {
                        return;
                    }
                    var maxRow = Math.Max(2000, rows.Count + 500);
                    var listColLetter = (char)('A' + col - 1);
                    var validation = sheet.DataValidations.AddListValidation($"{targetColLetter}2:{targetColLetter}{maxRow}");
                    validation.Formula.ExcelFormula = $"_MasterLists!${listColLetter}$2:${listColLetter}${values.Count + 1}";
                    validation.AllowBlank = false;
                    validation.ShowErrorMessage = true;
                    validation.ErrorTitle = "Invalid selection";
                    validation.Error = "Select a value from the dropdown list.";
                }

                WriteListColumn(1, masterLists.Customers, "B");
                WriteListColumn(2, masterLists.Ports, "D");
                WriteListColumn(3, masterLists.Terminals, "F");
                WriteListColumn(4, masterLists.Services, "H");

                var maxInputRow = Math.Max(2000, rows.Count + 500);
                var dateValidation = sheet.DataValidations.AddDateTimeValidation($"I2:I{maxInputRow}");
                dateValidation.Operator = OfficeOpenXml.DataValidation.ExcelDataValidationOperator.between;
                dateValidation.Formula.Value = new DateTime(1900, 1, 1);
                dateValidation.Formula2.Value = new DateTime(9998, 12, 31);
                dateValidation.ShowErrorMessage = true;
                dateValidation.ErrorTitle = "Invalid EffectiveDate";
                dateValidation.Error = kind == RateTableKind.BAF
                    ? "Enter a valid Tuesday date (yyyy-mm-dd)."
                    : "Enter a valid date (yyyy-mm-dd).";

                var amountValidation = sheet.DataValidations.AddDecimalValidation($"J2:J{maxInputRow}");
                amountValidation.Operator = OfficeOpenXml.DataValidation.ExcelDataValidationOperator.greaterThanOrEqual;
                amountValidation.Formula.Value = 0d;
                amountValidation.ShowErrorMessage = true;
                amountValidation.ErrorTitle = "Invalid amount";
                amountValidation.Error = "Enter a nonnegative amount (up to 2 decimal places).";
            }

            ExcelWorksheet notes = package.Workbook.Worksheets.Add("Read me");
            notes.Cells[1, 1].Value = $"MMSI {kind} rates. Edit EffectiveDate (yyyy-mm-dd) and {kind}Amount (max 2 decimal places), or add new rows using the Customer, Port, Terminal, and Service dropdowns.";
            notes.Cells[2, 1].Value = "When adding rows with the dropdowns, you may leave the Code columns blank. Zero is explicit (blank is not zero). For BAF, EffectiveDate must fall on a Tuesday.";
            notes.Cells[3, 1].Value = "Upload the saved .xlsx in Rate Updates -> Stage audited Excel workbook. Live rates remain unchanged until compared and approved.";
            notes.Column(1).Width = 110;
            notes.Column(1).Style.WrapText = true;
            notes.Row(1).Height = 45;
            notes.Row(2).Height = 45;
            notes.Row(3).Height = 45;
            return await package.GetAsByteArrayAsync(ct);
        }

        public static async Task<(List<RateUploadRowViewModel> Rows, List<string> Errors)> ReadAsync(
            Stream stream, RateTableKind kind, CancellationToken ct)
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            memory.Position = 0;
            using var package = new ExcelPackage(memory);
            var sheet = package.Workbook.Worksheets.FirstOrDefault(w =>
                !string.Equals(w.Name, "Read me", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.Name, "_MasterLists", StringComparison.OrdinalIgnoreCase));
            if (sheet == null || sheet.Dimension == null || sheet.Dimension.Rows < 2)
            {
                return ([], ["The uploaded workbook has no rate rows."]);
            }

            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var col = 1; col <= sheet.Dimension.Columns; col++)
            {
                var header = sheet.Cells[1, col].Text?.Trim();
                if (!string.IsNullOrWhiteSpace(header))
                {
                    headers[header] = col;
                }
            }

            var amountHeader = $"{kind}Amount";
            if (!headers.TryGetValue(amountHeader, out var amountCol)
                && !headers.TryGetValue("Amount", out amountCol))
            {
                return ([], [$"Missing '{amountHeader}' column. Export a fresh {kind} workbook and edit it."]);
            }
            if (!headers.TryGetValue("EffectiveDate", out var dateCol))
            {
                return ([], ["Missing 'EffectiveDate' column."]);
            }

            headers.TryGetValue("CustomerCode", out var custCodeCol);
            headers.TryGetValue("Customer", out var custNameCol);
            headers.TryGetValue("PortCode", out var portCodeCol);
            headers.TryGetValue("Port", out var portNameCol);
            headers.TryGetValue("TerminalCode", out var termCodeCol);
            headers.TryGetValue("Terminal", out var termNameCol);
            headers.TryGetValue("ServiceCode", out var svcCodeCol);
            headers.TryGetValue("Service", out var svcNameCol);

            if ((custCodeCol == 0 && custNameCol == 0)
                || (portCodeCol == 0 && portNameCol == 0)
                || (termCodeCol == 0 && termNameCol == 0)
                || (svcCodeCol == 0 && svcNameCol == 0))
            {
                return ([], ["Missing required Customer, Port, Terminal, or Service columns."]);
            }

            var rows = new List<RateUploadRowViewModel>();
            var errors = new List<string>();
            for (var r = 2; r <= sheet.Dimension.Rows; r++)
            {
                var custCode = custCodeCol > 0 ? sheet.Cells[r, custCodeCol].Text.Trim() : "";
                var custName = custNameCol > 0 ? sheet.Cells[r, custNameCol].Text.Trim() : "";
                var portCode = portCodeCol > 0 ? sheet.Cells[r, portCodeCol].Text.Trim() : "";
                var portName = portNameCol > 0 ? sheet.Cells[r, portNameCol].Text.Trim() : "";
                var termCode = termCodeCol > 0 ? sheet.Cells[r, termCodeCol].Text.Trim() : "";
                var termName = termNameCol > 0 ? sheet.Cells[r, termNameCol].Text.Trim() : "";
                var svcCode = svcCodeCol > 0 ? sheet.Cells[r, svcCodeCol].Text.Trim() : "";
                var svcName = svcNameCol > 0 ? sheet.Cells[r, svcNameCol].Text.Trim() : "";
                var rawDate = sheet.Cells[r, dateCol].Value;
                var rawAmount = sheet.Cells[r, amountCol].Value;

                if (string.IsNullOrEmpty(custCode) && string.IsNullOrEmpty(custName)
                    && string.IsNullOrEmpty(termCode) && string.IsNullOrEmpty(svcCode)
                    && rawDate == null && rawAmount == null)
                {
                    continue;
                }

                if (!TryParseDate(rawDate, sheet.Cells[r, dateCol].Text, out var effectiveDate))
                {
                    errors.Add($"Excel row {r}: invalid EffectiveDate.");
                    continue;
                }
                if (kind == RateTableKind.BAF && effectiveDate.DayOfWeek != DayOfWeek.Tuesday)
                {
                    errors.Add($"Excel row {r}: BAF EffectiveDate ({effectiveDate:yyyy-MM-dd}) must fall on a Tuesday.");
                    continue;
                }
                if (!TryParseDecimal(rawAmount, sheet.Cells[r, amountCol].Text, out var amount)
                    || amount < 0m || amount > 99999999999999.99m || decimal.Round(amount, 2) != amount)
                {
                    errors.Add($"Excel row {r}: enter a nonnegative amount with at most 2 decimal places.");
                    continue;
                }

                rows.Add(new RateUploadRowViewModel
                {
                    RowNumber = r,
                    CustomerCode = custCode,
                    CustomerName = custName,
                    PortCode = portCode,
                    PortName = portName,
                    TerminalCode = termCode,
                    TerminalName = termName,
                    ServiceCode = svcCode,
                    ServiceName = svcName,
                    EffectiveDate = effectiveDate,
                    Amount = amount,
                    Selected = true
                });
            }

            return (rows, errors);
        }

        private static bool TryParseDate(object? raw, string text, out DateOnly date)
        {
            if (raw is DateTime dt)
            {
                date = DateOnly.FromDateTime(dt);
                return true;
            }
            if (raw is double oa)
            {
                date = DateOnly.FromDateTime(DateTime.FromOADate(oa));
                return true;
            }
            return DateOnly.TryParse(text?.Trim(), out date);
        }

        private static bool TryParseDecimal(object? raw, string text, out decimal amount)
        {
            if (raw is decimal d)
            {
                amount = d;
                return true;
            }
            if (raw is double dbl)
            {
                amount = Convert.ToDecimal(dbl);
                return true;
            }
            if (raw is int i)
            {
                amount = i;
                return true;
            }
            return decimal.TryParse(text?.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out amount);
        }
    }
}
