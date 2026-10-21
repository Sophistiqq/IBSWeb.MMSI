using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace IBS.Services.MSAP
{
    public static class RateUploadWorkbook
    {
        public static async Task<byte[]> WriteAsync(List<RateUploadRowViewModel> rows, RateTableKind kind, bool history, CancellationToken ct)
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
            sheet.Column(10).Style.Numberformat.Format = "#,##0.0000";
            sheet.Column(12).Style.Numberformat.Format = "yyyy-mm-dd hh:mm";
            foreach (int column in new[] { 1, 3, 5, 7 })
            {
                sheet.Column(column).Style.Numberformat.Format = "@";
            }
            for (int column = 1; column <= headers.Length; column++)
            {
                sheet.Column(column).Width = column is 2 or 4 or 6 or 8 ? 32 : 22;
            }
            ExcelWorksheet notes = package.Workbook.Worksheets.Add("Read me");
            notes.Cells[1, 1].Value = $"MMSI {kind} rates. Tariff and BAF are maintained independently. Edit rates in the application and submit for approval.";
            notes.Cells[2, 1].Value = "These are stored amounts. Zero is explicit. No fuel formula or VAT conversion is inferred.";
            notes.Cells[3, 1].Value = "Historical exports contain retained dated records; the rate-update review retains old/new correction evidence. Legacy approvals are not inferred.";
            notes.Column(1).Width = 110;
            notes.Column(1).Style.WrapText = true;
            notes.Row(1).Height = 45;
            notes.Row(2).Height = 45;
            notes.Row(3).Height = 45;
            return await package.GetAsByteArrayAsync(ct);
        }
    }
}
