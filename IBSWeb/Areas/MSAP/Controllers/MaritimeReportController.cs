using System.Drawing;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess("Access denied. You don't have permission to view reports.", ProcedureEnum.ViewMaritimeReport)]
public class MaritimeReportController(IUnitOfWork unitOfWork) : Controller
    {
        public IActionResult Index() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalesBook(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
        {
            if (!ModelState.IsValid || dateFrom == default || dateTo == default || dateFrom > dateTo)
            {
                return BadRequest("From must not be later than To.");
            }
            var entries = await unitOfWork.Report.GetSalesBookAsync(dateFrom, dateTo, ct);
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("Sales Book");
            WriteCompanyHeader(sheet);
            sheet.Cells[2, 1].Value = $"SALES BOOK · FINAL APPROVAL {dateFrom:dd MMM yyyy} – {dateTo:dd MMM yyyy}";
            string[] headers = ["Approval date", "Billing date", "Billing number", "Billed to", "TIN", "Amount after discount", "VATable sales", "VAT", "VAT exempt", "Zero rated", "Discount", "Net sales", "Revision", "Approved by", "Undocumented"];
            for (int column = 0; column < headers.Length; column++)
            {
                sheet.Cells[4, column + 1].Value = headers[column];
            }
            StyleHeader(sheet, 4, headers.Length);
            int row = 5;
            foreach (var entry in entries)
            {
                object[] values = [entry.TransactionDate.ToString("yyyy-MM-dd"), entry.BillingDate.ToString("yyyy-MM-dd"),
                    entry.SerialNo, entry.SoldTo, entry.TinNo, entry.Amount, entry.VatableSales, entry.VatAmount,
                    entry.VatExemptSales, entry.ZeroRated, entry.Discount, entry.NetSales, entry.BillingRevision,
                    entry.ApprovedByName, entry.IsUndocumented ? "Yes" : "No"];
                for (int column = 0; column < values.Length; column++)
                {
                    sheet.Cells[row, column + 1].Value = values[column];
                }
                sheet.Cells[row, 6, row, 12].Style.Numberformat.Format = "#,##0.00";
                row++;
            }
            sheet.Cells.AutoFitColumns();
            sheet.View.FreezePanes(5, 1);
            return File(await package.GetAsByteArrayAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"MSAP-Sales-Book-{dateFrom:yyyyMMdd}-{dateTo:yyyyMMdd}.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DispatchForBilling(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
        {
            try
            {
                var data = (await unitOfWork.Report.GetDispatchReportData(dateFrom, dateTo, ct))
                    .Where(t => t.Status == MsapConstants.DispatchTicketStatus.ForBilling).ToList();
                using var pkg = new ExcelPackage();
                var ws = pkg.Workbook.Worksheets.Add("Dispatch For Billing");
                const int totalCols = 22;

                WriteCompanyHeader(ws);
                ws.Cells["A2"].Value = $"DISPATCH TICKET FOR BILLING AS OF {DateTimeHelper.GetCurrentPhilippineTime():MM/dd/yyyy}";
                ws.Cells["A2"].Style.Font.Size = 14;
                ws.Cells["A2"].Style.Font.Bold = true;
                ws.Cells["A3"].Value = $"Period: {dateFrom:MM/dd/yyyy} - {dateTo:MM/dd/yyyy}";

                var mainLabels = new[] { "COS #", "DISPATCH #", "DATE", "SERVICE", "VOYAGE #", "CUSTOMER", "VESSEL",
                    "PORT", "TERMINAL", "DATE/TIME           LEFT", "DATE/TIME           ARRIVED", "# OF HOURS", "RATE INDICATOR" };
                for (int i = 0; i < mainLabels.Length; i++)
                {
                    ws.Cells[5, i + 1].Value = mainLabels[i];
                }

                MergeRowRange(ws, 5, 14, 17, "D I S P A T C H");
                MergeRowRange(ws, 5, 18, 21, "B A F");
                ws.Cells[5, 22].Value = "TOTAL BILL AMOUNT";
                StyleHeader(ws, 5, totalCols);

                foreach (var (col, label) in new[] { (14, "RATE"), (15, "BILL AMOUNT"), (16, "DISCOUNT"), (17, "NET AMOUNT"),
                    (18, "RATE"), (19, "BILL AMOUNT"), (20, "DISCOUNT"), (21, "NET AMOUNT") })
                {
                    ws.Cells[6, col].Value = label;
                }

                StyleHeader(ws, 6, totalCols);
                for (int c = 1; c <= 13; c++)
                {
                    ws.Cells[5, c, 6, c].Merge = true;
                }

                ws.Cells[5, 22, 6, 22].Merge = true;

                int dataStart = 7, row = dataStart;
                foreach (var t in data)
                {
                    ws.Cells[row, 1].Value = t.COSNumber;
                    ws.Cells[row, 2].Value = t.DispatchNumber;
                    ws.Cells[row, 3].Value = t.Date.ToString("MM/dd/yyyy");
                    ws.Cells[row, 4].Value = t.Service.ServiceName;
                    ws.Cells[row, 5].Value = t.VoyageNumber;
                    ws.Cells[row, 6].Value = t.Customer.CustomerName;
                    ws.Cells[row, 7].Value = t.Vessel.VesselName;
                    ws.Cells[row, 8].Value = t.Terminal.Port.PortName;
                    ws.Cells[row, 9].Value = t.Terminal.TerminalName;
                    ws.Cells[row, 10].Value = FormatDateTime(t.DateLeft, t.TimeLeft);
                    ws.Cells[row, 11].Value = FormatDateTime(t.DateArrived, t.TimeArrived);
                    ws.Cells[row, 12].Value = Math.Round(t.TotalHours, 2);
                    ws.Cells[row, 13].Value = "Per Move";
                    ws.Cells[row, 14].Value = NullIfZero(t.DispatchRate);
                    ws.Cells[row, 15].Value = NullIfZero(t.DispatchBillingAmount);
                    ws.Cells[row, 16].Value = NullIfZero(t.DispatchDiscount);
                    ws.Cells[row, 17].Value = NullIfZero(t.DispatchNetRevenue);
                    ws.Cells[row, 18].Value = NullIfZero(t.BAFRate);
                    ws.Cells[row, 19].Value = NullIfZero(t.BAFBillingAmount);
                    ws.Cells[row, 20].Value = NullIfZero(t.BAFDiscount);
                    ws.Cells[row, 21].Value = NullIfZero(t.BAFNetRevenue);
                    ws.Cells[row, 22].Value = NullIfZero(t.TotalBilling);
                    ws.Cells[row, 1, row, totalCols].Style.Font.Size = 11;
                    for (int c = 12; c <= 22; c++)
                    {
                        ws.Cells[row, c].Style.Numberformat.Format = "#,##0.00";
                    }

                    row++;
                }

                FinalizeColumns(ws, dataStart, row - 1, totalCols);
                return File(await pkg.GetAsByteArrayAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Dispatch_For_Billing_{DateTimeHelper.GetCurrentPhilippineTime():yyyyMMdd}.xlsx");
            }
            catch (Exception ex)
            {
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DispatchTicketSummary(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
        {
            try
            {
                var data = await unitOfWork.Report.GetDispatchReportData(dateFrom, dateTo, ct);
                var (collectionShares, collectionsByTicket) = await AllocateCollectionsByTicketAsync(data, ct);
                using var pkg = new ExcelPackage();
                var ws = pkg.Workbook.Worksheets.Add("Dispatch Summary");
                const int totalCols = 35;

                WriteCompanyHeader(ws);
                ws.Cells["A2"].Value = $"DISPATCH TICKET SUMMARY AS OF {DateTimeHelper.GetCurrentPhilippineTime():MM/dd/yyyy}";
                ws.Cells["A2"].Style.Font.Size = 14;
                ws.Cells["A2"].Style.Font.Bold = true;
                ws.Cells["A3"].Value = $"Period: {dateFrom:MM/dd/yyyy} - {dateTo:MM/dd/yyyy}";

                var mainLabels = new[] { "COS #", "DISPATCH #", "DATE", "SERVICE", "VOYAGE #", "CUSTOMER", "VESSEL",
                    "PORT", "TERMINAL", "DATE/TIME           LEFT", "DATE/TIME           ARRIVED", "# OF HOURS", "RATE INDICATOR" };
                for (int i = 0; i < mainLabels.Length; i++)
                {
                    ws.Cells[5, i + 1].Value = mainLabels[i];
                }

                MergeRowRange(ws, 5, 14, 17, "D I S P A T C H");
                MergeRowRange(ws, 5, 18, 21, "B A F");
                ws.Cells[5, 22].Value = "TOTAL BILL AMOUNT";
                MergeRowRange(ws, 5, 23, 28, "B I L L I N G");
                MergeRowRange(ws, 5, 29, 35, "C O L L E C T I O N");
                for (int c = 1; c <= 13; c++)
                {
                    ws.Cells[5, c, 7, c].Merge = true;
                }

                ws.Cells[5, 22, 7, 22].Merge = true;

                var peach = Color.FromArgb(0xFF, 0xCC, 0x99);
                var cyan = Color.FromArgb(0xCC, 0xFF, 0xFF);
                var lavender = Color.FromArgb(0xCC, 0xCC, 0xFF);
                var paleGreen = Color.FromArgb(0xCC, 0xFF, 0xCC);
                var paleYellow = Color.FromArgb(0xFF, 0xFF, 0x99);
                foreach (int r in new[] { 5, 6, 7 })
                {
                    StyleSection(ws, r, 1, 13, peach);
                    StyleSection(ws, r, 14, 17, cyan);
                    StyleSection(ws, r, 18, 21, lavender);
                    StyleSection(ws, r, 22, 22, peach);
                    StyleSection(ws, r, 23, 28, paleGreen);
                    StyleSection(ws, r, 29, 35, paleYellow);
                }

                foreach (var (col, label) in new[] {
                    (14, "RATE"), (15, "BILL AMOUNT"), (16, "DISCOUNT"), (17, "NET AMOUNT"),
                    (18, "RATE"), (19, "BILL AMOUNT"), (20, "DISCOUNT"), (21, "NET AMOUNT"),
                    (23, "BILL #"), (24, "DATE"),
                    (29, "AP OTHER TUG"), (30, "CR NUMBER"), (31, "CHECK NUMBER"),
                    (32, "CHECK DATE"), (33, "DATE DEPOSITED"), (34, "AMOUNT PER DISPATCH"), (35, "2307 PER DISPATCH") })
                {
                    ws.Cells[6, col].Value = label;
                }

                MergeRowRange(ws, 6, 25, 26, "DISPATCH");
                MergeRowRange(ws, 6, 27, 28, "BAF");
                ws.Cells[7, 25].Value = "RATE";
                ws.Cells[7, 26].Value = "AMOUNT";
                ws.Cells[7, 27].Value = "RATE";
                ws.Cells[7, 28].Value = "AMOUNT";
                for (int c = 14; c <= 21; c++)
                {
                    ws.Cells[6, c, 7, c].Merge = true;
                }

                ws.Cells[6, 23, 7, 23].Merge = true;
                ws.Cells[6, 24, 7, 24].Merge = true;
                ws.Cells[6, 29, 7, 29].Merge = true;
                for (int c = 30; c <= 35; c++)
                {
                    ws.Cells[6, c, 7, c].Merge = true;
                }

                ws.View.FreezePanes(8, 1);

                int dataStart = 8, row = dataStart;
                foreach (var t in data)
                {
                    ws.Cells[row, 1].Value = t.COSNumber;
                    ws.Cells[row, 2].Value = t.DispatchNumber;
                    ws.Cells[row, 3].Value = t.Date.ToString("MM/dd/yyyy");
                    ws.Cells[row, 4].Value = t.Service.ServiceName;
                    ws.Cells[row, 5].Value = t.VoyageNumber;
                    ws.Cells[row, 6].Value = t.Customer.CustomerName;
                    ws.Cells[row, 7].Value = t.Vessel.VesselName;
                    ws.Cells[row, 8].Value = t.Terminal.Port.PortName;
                    ws.Cells[row, 9].Value = t.Terminal.TerminalName;
                    ws.Cells[row, 10].Value = FormatDateTime(t.DateLeft, t.TimeLeft);
                    ws.Cells[row, 11].Value = FormatDateTime(t.DateArrived, t.TimeArrived);
                    ws.Cells[row, 12].Value = Math.Round(t.TotalHours, 2);
                    ws.Cells[row, 13].Value = "Per Move";
                    ws.Cells[row, 14].Value = NullIfZero(t.DispatchRate);
                    ws.Cells[row, 15].Value = NullIfZero(t.DispatchBillingAmount);
                    ws.Cells[row, 16].Value = NullIfZero(t.DispatchDiscount);
                    ws.Cells[row, 17].Value = NullIfZero(t.DispatchNetRevenue);
                    ws.Cells[row, 18].Value = NullIfZero(t.BAFRate);
                    ws.Cells[row, 19].Value = NullIfZero(t.BAFBillingAmount);
                    ws.Cells[row, 20].Value = NullIfZero(t.BAFDiscount);
                    ws.Cells[row, 21].Value = NullIfZero(t.BAFNetRevenue);
                    ws.Cells[row, 22].Formula = $"O{row}+S{row}";
                    ws.Cells[row, 23].Value = t.Billing?.MsapBillingNumber;
                    ws.Cells[row, 24].Value = t.Billing?.Date.ToString("MM/dd/yyyy");
                    ws.Cells[row, 25].Value = NullIfZero(t.DispatchRate);
                    ws.Cells[row, 26].Value = NullIfZero(t.DispatchBillingAmount);
                    ws.Cells[row, 27].Value = NullIfZero(t.BAFRate);
                    ws.Cells[row, 28].Value = NullIfZero(t.BAFBillingAmount);
                    ws.Cells[row, 29].Value = t.OtherTugCostPending ? "Pending" : t.OtherTugChargeLine?.Charge.ConfirmedAt != null ? t.ApOtherTugs : NullIfZero(t.ApOtherTugs);
                    var ticketCollections = collectionsByTicket.GetValueOrDefault(t.DispatchTicketId) ?? [];
                    ws.Cells[row, 30].Value = JoinCollectionValues(ticketCollections, c => c.MsapCollectionNumber.Trim());
                    ws.Cells[row, 31].Value = JoinCollectionValues(ticketCollections, c => c.CheckNumber?.Trim());
                    ws.Cells[row, 32].Value = JoinCollectionValues(ticketCollections, c => c.CheckDate?.ToString("MM/dd/yyyy"));
                    ws.Cells[row, 33].Value = JoinCollectionValues(ticketCollections, c => c.DepositDate?.ToString("MM/dd/yyyy"));
                    var (amount, ewt) = collectionShares.GetValueOrDefault(t.DispatchTicketId);
                    ws.Cells[row, 34].Value = NullIfZero(amount);
                    ws.Cells[row, 35].Value = NullIfZero(ewt);
                    ws.Cells[row, 1, row, totalCols].Style.Font.Size = 11;
                    ws.Cells[row, 12].Style.Numberformat.Format = "#,##0.00";
                    for (int c = 14; c <= 35; c++)
                    {
                        if (c < 30 || c > 33)
                        {
                            ws.Cells[row, c].Style.Numberformat.Format = "_(#,##0.00_);[Red](#,##0.00)";
                        }
                    }

                    row++;
                }

                int lastDataRow = row - 1, totalRow = row;
                ws.Cells[totalRow, 1].Value = "TOTAL";
                ws.Cells[totalRow, 1, totalRow, totalCols].Style.Font.Size = 11;
                ws.Cells[totalRow, 1, totalRow, totalCols].Style.Font.Bold = true;
                foreach (int c in new[] { 15, 17, 19, 21, 22, 26, 28, 29, 34, 35 })
                {
                    ws.Cells[totalRow, c].FormulaR1C1 = $"SUM(R{dataStart}C:R{lastDataRow}C)";
                }

                FinalizeColumns(ws, dataStart, lastDataRow, totalCols);
                ws.Column(6).Width = 50.7;
                ws.Column(25).Width = 20.7;
                return File(await pkg.GetAsByteArrayAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Dispatch_Ticket_Summary_{DateTimeHelper.GetCurrentPhilippineTime():yyyyMMdd}.xlsx");
            }
            catch (Exception ex)
            {
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalesSummary(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
        {
            try
            {
                if (!ModelState.IsValid || dateFrom == default || dateTo == default || dateFrom > dateTo)
                {
                    TempData["error"] = "Please select valid From and To dates.";
                    return RedirectToAction(nameof(Index));
                }

                var generatedAt = DateTimeHelper.GetCurrentPhilippineTime();
                var isFinalReport = dateTo < new DateOnly(generatedAt.Year, generatedAt.Month, 1);
                var fileName = $"Sales_Summary_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx";
                const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                if (isFinalReport && await unitOfWork.Report.GetFinalSalesReportAsync(dateFrom, dateTo, ct) is { } savedReport)
                {
                    return File(savedReport, contentType, fileName);
                }

                var data = await unitOfWork.Report.GetDispatchReportData(dateFrom, dateTo, ct, filterByBillingDate: true);
                var (collectionShares, collectionsByTicket) = await AllocateCollectionsByTicketAsync(data, ct);
                var sbmaShares = data.ToDictionary(t => t.DispatchTicketId, t => t.SbmaShareAmount);
                var cosIds = data.Where(t => t.CustomerOrderSlipId.HasValue).Select(t => t.CustomerOrderSlipId!.Value).Distinct().ToList();
                var scheduleIoc = (await unitOfWork.VesselSchedule.GetAllAsync(s => s.CustomerOrderSlipId.HasValue
                    && cosIds.Contains(s.CustomerOrderSlipId.Value), ct))
                    .ToDictionary(s => s.CustomerOrderSlipId!.Value, s => s.IsIoc);
                bool IsIoc(DispatchTicket ticket)
                {
                    return (ticket.CustomerOrderSlipId.HasValue ? scheduleIoc.GetValueOrDefault(ticket.CustomerOrderSlipId.Value) : null)
                        ?? (ticket.Terminal?.IsIoc == true);
                }

                // Load all masterfile entities required for dynamic columns (as in old system curtug, curowner, curcustomer)
                var companyOwnedTugboats = (await unitOfWork.Tugboat.GetAllAsync(cancellationToken: ct))
                    .Where(t => t.IsCompanyOwned)
                    .OrderBy(t => t.TugboatName)
                    .ToList();

                var tugboatOwners = (await unitOfWork.TugboatOwner.GetAllAsync(cancellationToken: ct))
                    .OrderBy(o => o.TugboatOwnerName)
                    .ToList();

                var allCustomers = (await unitOfWork.Customer.GetAllAsync(cancellationToken: ct))
                    .OrderBy(c => c.CustomerName)
                    .ToList();

                using var pkg = new ExcelPackage();
                var ws = pkg.Workbook.Worksheets.Add("AR Monitoring");

                WriteCompanyHeader(ws);
                ws.Cells["A2"].Value = $"{(isFinalReport ? "FINAL SALES SUMMARY" : "SALES SUMMARY")} AS OF {generatedAt:MM/dd/yyyy}";
                ws.Cells["A2"].Style.Font.Size = 14;
                ws.Cells["A2"].Style.Font.Bold = true;
                ws.Cells["A3"].Value = $"Period: {dateFrom:MM/dd/yyyy} - {dateTo:MM/dd/yyyy}";

                // Keep the fixed Sales Report columns identical to the submitted workbook.
                var colInfo = BuildSalesSummaryColumns(companyOwnedTugboats, tugboatOwners, allCustomers);
                var totalCols = colInfo.Count;

                var sectionColors = new Dictionary<int, Color>
                {
                    [0] = Color.FromArgb(192, 192, 192), // Details (Gray 25%)
                    [1] = Color.FromArgb(255, 255, 0),   // FOR PNL USE (Yellow - ColorIndex 6)
                    [2] = Color.FromArgb(255, 153, 0),   // AP LEDGER (Light Orange - ColorIndex 45)
                    [3] = Color.FromArgb(192, 192, 192), // A/R LEDGER (Gray 25% - ColorIndex 15)
                    [4] = Color.FromArgb(255, 192, 0),   // Number of ASSISTS (Gold - ColorIndex 44)
                    [5] = Color.FromArgb(255, 204, 153), // Number of TENDING (Tan - ColorIndex 40)
                    [6] = Color.FromArgb(255, 255, 102), // Number of TENDING HOURS (Yellow+ - ColorIndex 27)
                };

                var sectionLabels = new Dictionary<int, string>
                {
                    [0] = "DETAILS OF TRIPS OF TUGBOAT",
                    [1] = "FOR PNL USE",
                    [2] = "AP LEDGER",
                    [3] = "A/R LEDGER",
                    [4] = "Number of ASSISTS",
                    [5] = "Number of TENDING",
                    [6] = "Number of TENDING HOURS",
                };

                void PaintSectionHeader(int startCol1, int endCol1, int section)
                {
                    var rng = ws.Cells[5, startCol1, 5, endCol1];
                    rng.Merge = true;
                    rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rng.Style.Fill.BackgroundColor.SetColor(sectionColors[section]);
                    rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    rng.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    rng.Style.WrapText = true;
                    ws.Cells[5, startCol1].Value = sectionLabels[section];
                    ws.Cells[5, startCol1].Style.Font.Bold = true;
                    ws.Cells[5, startCol1].Style.Font.Size = 10;
                }

                // Row 5: Section 0 (DETAILS) spans the 35 fixed Sales Report columns.
                PaintSectionHeader(1, 35, 0);

                // Row 5: Dynamic Sections (starts at Col 36: FOR PNL USE)
                int currentSection = -1;
                int sectionStart = -1;
                for (int c = 36; c <= totalCols; c++)
                {
                    var sec = colInfo[c - 1].section;
                    if (sec != currentSection)
                    {
                        if (currentSection >= 0 && sectionStart >= 0)
                        {
                            PaintSectionHeader(sectionStart, c - 1, currentSection);
                        }
                        currentSection = sec;
                        sectionStart = c;
                    }
                }
                if (currentSection >= 0 && sectionStart >= 0)
                {
                    PaintSectionHeader(sectionStart, totalCols, currentSection);
                }

                // Row 6: Individual Column Headers
                ws.Row(6).Height = 63.75;
                for (int i = 0; i < totalCols; i++)
                {
                    ws.Cells[6, i + 1].Value = colInfo[i].label;
                }

                var headerNotes = new Dictionary<int, string>
                {
                    [15] = "Saved billable hours in the original ticket period. Settlement-only rows do not count hours or services again.",
                    [16] = "Same amount as GROSS SALES; this is the total service charge, not an hourly rate.",
                    [17] = "Saved dispatch charge + BAF charge after discounts. Add 12% VAT for VAT-exclusive vatable billings; otherwise use the saved charges.",
                    [18] = "GROSS SALES / 1.12 for VAT-inclusive vatable billings, or unbilled local vessels. Otherwise, saved dispatch + BAF charges after discounts. Rounded to 2 decimals.",
                    [19] = "GROSS SALES - NET OF VAT, rounded to 2 decimals.",
                    [20] = "NET OF VAT x 2% when withholding tax is enabled on the billing; otherwise zero.",
                    [25] = "NET OF VAT when allocated EWT is nonzero and the billing is vatable; otherwise zero.",
                    [26] = "Receipt EWT allocated to each bill using its rounded settlement, then to its tickets by charges after discounts (Basic and/or BAF). Includes tickets outside the report period; equal ticket shares if all charges are zero. Rounded to cents with remainder distributed.",
                    [27] = "Receipt amount allocated to each bill using its rounded net settlement, then to its tickets by charges after discounts (Basic and/or BAF). Includes tickets outside the report period; equal ticket shares if all charges are zero. Rounded to cents with remainder distributed.",
                    [28] = "Saved SBMA share: actual service minutes x 87.50 when SBMA is enabled, rounded and allocated per COS/tugboat. Uses actual time, not minimum billable hours.",
                    [29] = "Currently zero in this export.",
                    [30] = "Currently zero in this export.",
                    [31] = "Currently zero in this export.",
                    [32] = "NET OF VAT - SBMA SHARE - OVERPAYMENT - AGENCY INCENTIVE - AGENT COMMISSION. Collections and withholding taxes are not deducted here.",
                    [33] = "Confirmed provider charge. Text indicates financial amounts are deferred and excluded from this period's totals; confirmed amounts appear in the confirmation period.",
                    [34] = "AP OTHER TUGS / 1.12 when the provider charge includes VAT; otherwise AP OTHER TUGS. Rounded to 2 decimals.",
                    [35] = "BALANCE - AP OTHER TUGS(NET OF VAT). Financial amounts are blank outside the Other Tug confirmation period."
                };
                foreach (var (column, note) in headerNotes)
                {
                    var comment = ws.Cells[6, column].AddComment(note, "MMSI");
                    comment.AutoFit = true;
                }

                using (var rng = ws.Cells[6, 1, 6, totalCols])
                {
                    rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rng.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(153, 204, 255)); // Pale Blue (ColorIndex 37)
                    rng.Style.Font.Size = 8;
                    rng.Style.Font.Bold = true;
                    rng.Style.WrapText = true;
                    rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    rng.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    rng.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                }

                // Header styling for Row 5 and Row 6 borders & font
                using (var rng = ws.Cells[5, 1, 6, totalCols])
                {
                    rng.Style.Font.Name = "Calibri";
                    rng.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                }

                // Build lookup maps for dynamic column indices (1-based)
                var tugboatCols = new Dictionary<string, TugboatCols>(StringComparer.OrdinalIgnoreCase);
                TugboatCols GetTugCols(string name) =>
                    tugboatCols.TryGetValue(name, out var c) ? c : tugboatCols[name] = new TugboatCols();

                var ownerNameToCol = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var customerNameToCol = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                int otherTugsIncomeCol = 0;
                int otherTugsAssistsLocCol = 0, otherTugsAssistsForCol = 0;
                int otherTugsTendingCol = 0;
                int otherTugsTendHrsLocCol = 0, otherTugsTendHrsForCol = 0;
                int arTotalCol = 0, docUndocCol = 0, principalCol = 0;
                int customerStartCol = -1, customerEndCol = -1;

                for (int i = 0; i < totalCols; i++)
                {
                    int col1 = i + 1;
                    var (label, sec) = colInfo[i];

                    switch (sec)
                    {
                        case 1 when label.StartsWith("INCOME FROM ", StringComparison.OrdinalIgnoreCase):
                        {
                            var tugName = label["INCOME FROM ".Length..].Trim();
                            if (tugName.Equals("OTHER TUGS", StringComparison.OrdinalIgnoreCase))
                            {
                                otherTugsIncomeCol = col1;
                            }
                            else
                            {
                                GetTugCols(tugName).Income = col1;
                            }
                            break;
                        }
                        case 1 when TryStripSuffix(label, " # OF HOURS", out var name):
                            GetTugCols(name).Hours = col1;
                            break;

                        case 2:
                            ownerNameToCol[label] = col1;
                            break;

                        case 3:
                            if (label == "TOTAL")
                            {
                                arTotalCol = col1;
                            }
                            else
                            {
                                if (customerStartCol == -1)
                                {
                                    customerStartCol = col1;
                                }

                                customerEndCol = col1;
                                customerNameToCol[label] = col1;
                            }
                            break;

                        case 4 when label == "OTHER TUGS LOCAL":
                            otherTugsAssistsLocCol = col1;
                            break;
                        case 4 when label == "OTHER TUGS FOREIGN":
                            otherTugsAssistsForCol = col1;
                            break;
                        case 4 when TryStripSuffix(label, " LOCAL (IOC)", out var name):
                            GetTugCols(name).AssistsLocalIoc = col1;
                            break;
                        case 4 when TryStripSuffix(label, " FOREIGN (IOC)", out var name):
                            GetTugCols(name).AssistsForeignIoc = col1;
                            break;
                        case 4 when TryStripSuffix(label, " LOCAL (OUTSIDE)", out var name):
                            GetTugCols(name).AssistsLocalOutside = col1;
                            break;
                        case 4 when TryStripSuffix(label, " FOREIGN (OUTSIDE)", out var name):
                            GetTugCols(name).AssistsForeignOutside = col1;
                            break;

                        case 5 when label == "OTHER TUGS":
                            otherTugsTendingCol = col1;
                            break;
                        case 5:
                            GetTugCols(label).Tending = col1;
                            break;

                        case 6 when label == "OTHER TUGS LOCAL":
                            otherTugsTendHrsLocCol = col1;
                            break;
                        case 6 when label == "OTHER TUGS FOREIGN":
                            otherTugsTendHrsForCol = col1;
                            break;
                        case 6 when TryStripSuffix(label, " TENDING HOURS - LOCAL", out var name):
                            GetTugCols(name).TendingHoursLocal = col1;
                            break;
                        case 6 when TryStripSuffix(label, " TENDING HOURS - FOREIGN", out var name):
                            GetTugCols(name).TendingHoursForeign = col1;
                            break;
                        case 6 when label == "DOC/UNDOC":
                            docUndocCol = col1;
                            break;
                        case 6 when label == "PRINCIPAL":
                            principalCol = col1;
                            break;
                    }
                }

                ws.View.FreezePanes(7, 1);

                // Write Data Rows
                int row = 7;
                foreach (var t in data)
                {
                    var includesService = SalesReportRecognition.IncludesService(t, dateFrom, dateTo);
                    var includesAmounts = SalesReportRecognition.IncludesAmounts(t, dateFrom, dateTo);
                    ws.Cells[row, 1, row, totalCols].Style.Font.Size = 11;

                    // Fixed Sales Report columns 1..35
                    ws.Cells[row, 1].Value = (t.Tugboat.IsCompanyOwned ? t.Billing?.Date ?? t.Date : t.Date).ToString("MM/dd/yyyy");
                    ws.Cells[row, 2].Value = t.DispatchNumber?.Trim();
                    ws.Cells[row, 3].Value = t.Billing?.MsapBillingNumber?.Trim();
                    ws.Cells[row, 4].Value = t.Customer.CustomerName;
                    ws.Cells[row, 5].Value = t.Vessel.VesselName;
                    ws.Cells[row, 6].Value = t.Vessel.VesselType?.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase) == true ? "FOREIGN" : "LOCAL";
                    ws.Cells[row, 7].Value = t.Tugboat.TugboatName;
                    ws.Cells[row, 8].Value = t.Tugboat.IsCompanyOwned ? "OWNED" : "SUBCON";
                    ws.Cells[row, 9].Value = t.Port?.PortName ?? "";
                    ws.Cells[row, 10].Value = t.Terminal?.TerminalName ?? "";
                    ws.Cells[row, 11].Value = IsIoc(t) ? "IOC" : "NON-IOC";
                    ws.Cells[row, 12].Value = t.Service?.ServiceName ?? "";
                    ws.Cells[row, 13].Value = FormatLegacyDateTime(t.DateLeft, t.TimeLeft);
                    ws.Cells[row, 14].Value = FormatLegacyDateTime(t.DateArrived, t.TimeArrived);
                    ws.Cells[row, 15].Value = includesService ? Math.Round(t.TotalHours, 2) : null;

                    var ticketCharges = t.DispatchNetRevenue + t.BAFNetRevenue;
                    var vatExclusive = t.Billing is { IsVatable: true, IsVatInclusive: false };
                    decimal grossSales = vatExclusive ? Math.Round(ticketCharges * 1.12m, 2) : ticketCharges;
                    var vatIncluded = t.Billing is { IsVatable: true, IsVatInclusive: true }
                        || t.Billing == null && t.Vessel.VesselType?.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase) != true;
                    var netOfVat = vatIncluded ? Math.Round(grossSales / 1.12m, 2) : ticketCharges;
                    ws.Cells[row, 16].Value = grossSales;
                    ws.Cells[row, 17].Value = grossSales;
                    ws.Cells[row, 18].Value = netOfVat;
                    ws.Cells[row, 19].Value = Math.Round(grossSales - netOfVat, 2);
                    ws.Cells[row, 20].Value = t.Billing?.PrintWht == true ? Math.Round(netOfVat * 0.02m, 2) : 0m;

                    // Collection/Deposit details
                    var ticketCollections = collectionsByTicket.GetValueOrDefault(t.DispatchTicketId) ?? [];
                    ws.Cells[row, 21].Value = JoinCollectionValues(ticketCollections, c => c.DepositDate?.ToString("MM/dd/yyyy"));
                    ws.Cells[row, 22].Value = JoinCollectionValues(ticketCollections, c => c.Date.ToString("MM/dd/yyyy"));
                    ws.Cells[row, 23].Value = JoinCollectionValues(ticketCollections, c => c.MsapCollectionNumber.Trim());
                    ws.Cells[row, 24].Value = JoinCollectionValues(ticketCollections, c => c.CheckBank?.Trim());

                    var (amountDeposited, ewt) = collectionShares.GetValueOrDefault(t.DispatchTicketId);
                    decimal vatableAmount = ewt != 0 && t.Billing?.IsVatable == true ? netOfVat : 0m;

                    ws.Cells[row, 25].Value = vatableAmount;
                    ws.Cells[row, 26].Value = ewt;
                    ws.Cells[row, 27].Value = amountDeposited;
                    ws.Cells[row, 28].Value = sbmaShares[t.DispatchTicketId];
                    ws.Cells[row, 29].Value = 0m;
                    ws.Cells[row, 30].Value = 0m;
                    ws.Cells[row, 31].Value = 0m;
                    var balance = netOfVat - (decimal)ws.Cells[row, 28].Value - (decimal)ws.Cells[row, 29].Value - (decimal)ws.Cells[row, 30].Value - (decimal)ws.Cells[row, 31].Value;
                    ws.Cells[row, 32].Value = balance;
                    ws.Cells[row, 33].Value = t.ApOtherTugs;
                    var apNetOfVat = t.ApOtherTugsIncludesVat ? Math.Round(t.ApOtherTugs / 1.12m, 2) : t.ApOtherTugs;
                    ws.Cells[row, 34].Value = apNetOfVat;
                    ws.Cells[row, 35].Value = balance - apNetOfVat;
                    if (!includesAmounts)
                    {
                        ws.Cells[row, 16, row, 35].Value = null;
                        ws.Cells[row, 33].Value = SalesReportRecognition.GetDeferredApLabel(t);
                        ws.Cells[row, 33].Style.WrapText = true;
                        ws.Cells[row, 33].AddComment("Service tracking only. Financial amounts are recognized in the provider invoice confirmation period.", "MMSI");
                    }
                    if (!includesService)
                    {
                        ws.Cells[row, 2].AddComment("Financial recognition only. This service and its hours were counted in the original ticket period.", "MMSI");
                    }

                    // Number formats for detail section
                    ws.Cells[row, 15].Style.Numberformat.Format = "#0.00_);";
                    ws.Cells[row, 16, row, 17].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";
                    ws.Cells[row, 18, row, 35].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";

                    var tugName = t.Tugboat.TugboatName;
                    var isCompanyOwned = t.Tugboat.IsCompanyOwned;
                    var isForeign = t.Vessel.VesselType?.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase) == true;
                    var isIoc = IsIoc(t);

                    // Red font on Port (Col 9) and AP Other Tugs (Col 33) for non-company-owned tugs (VFP lines 615-618)
                    if (!isCompanyOwned)
                    {
                        ws.Cells[row, 9].Style.Font.Color.SetColor(Color.Red);
                        ws.Cells[row, 33].Style.Font.Color.SetColor(Color.Red);
                    }

                    // Section 1: FOR PNL USE Formulas
                    if (isCompanyOwned && tugboatCols.TryGetValue(tugName, out var tc))
                    {
                        if (tc.Income.HasValue)
                        {
                            ws.Cells[row, tc.Income.Value].Value = ws.Cells[row, 35].Value;
                            ws.Cells[row, tc.Income.Value].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";
                        }
                        if (tc.Hours.HasValue)
                        {
                            ws.Cells[row, tc.Hours.Value].Value = ws.Cells[row, 15].Value;
                            ws.Cells[row, tc.Hours.Value].Style.Numberformat.Format = "#0.00_);";
                        }
                    }
                    else if (!isCompanyOwned && otherTugsIncomeCol > 0)
                    {
                        ws.Cells[row, otherTugsIncomeCol].Value = ws.Cells[row, 35].Value;
                        ws.Cells[row, otherTugsIncomeCol].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";
                    }

                    // Section 2: AP LEDGER Formula
                    var ownerName = t.Tugboat.TugboatOwner?.TugboatOwnerName;
                    if (ownerName != null && ownerNameToCol.TryGetValue(ownerName, out var apCol))
                    {
                        ws.Cells[row, apCol].Value = includesAmounts ? t.ApOtherTugs : null;
                        ws.Cells[row, apCol].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";
                    }

                    // Section 3: A/R LEDGER Formula
                    var custName = t.Customer.CustomerName;
                    if (customerNameToCol.TryGetValue(custName, out var arCol))
                    {
                        ws.Cells[row, arCol].Value = ws.Cells[row, 17].Value;
                        ws.Cells[row, arCol].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";
                    }
                    if (arTotalCol > 0 && customerStartCol != -1 && customerEndCol != -1)
                    {
                        ws.Cells[row, arTotalCol].Value = includesAmounts ? t.DispatchBillingAmount + t.BAFBillingAmount : null;
                        ws.Cells[row, arTotalCol].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";
                    }

                    // Service Type Determination
                    var serviceName = t.Service?.ServiceName ?? "";
                    bool isTending = serviceName.Contains("TENDING", StringComparison.OrdinalIgnoreCase);
                    bool isAssist = !isTending;

                    // Section 4: Number of ASSISTS
                    if (includesService && isAssist)
                    {
                        int? assistCol = null;
                        if (isCompanyOwned && tugboatCols.TryGetValue(tugName, out var tcAssist))
                        {
                            assistCol = (isIoc, isForeign) switch
                            {
                                (true, false) => tcAssist.AssistsLocalIoc,
                                (true, true) => tcAssist.AssistsForeignIoc,
                                (false, false) => tcAssist.AssistsLocalOutside,
                                (false, true) => tcAssist.AssistsForeignOutside,
                            };
                        }
                        else if (!isCompanyOwned)
                        {
                            assistCol = isForeign ? otherTugsAssistsForCol : otherTugsAssistsLocCol;
                        }

                        if (assistCol.HasValue && assistCol.Value > 0)
                        {
                            ws.Cells[row, assistCol.Value].Value = 1;
                            ws.Cells[row, assistCol.Value].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }
                    }

                    // Section 5 & 6: Number of TENDING & TENDING HOURS
                    if (includesService && isTending)
                    {
                        if (isCompanyOwned && tugboatCols.TryGetValue(tugName, out var tcTend))
                        {
                            if (tcTend.Tending.HasValue && tcTend.Tending.Value > 0)
                            {
                                ws.Cells[row, tcTend.Tending.Value].Value = 1;
                                ws.Cells[row, tcTend.Tending.Value].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                            }
                            var tendHrsCol = isForeign ? tcTend.TendingHoursForeign : tcTend.TendingHoursLocal;
                            if (tendHrsCol.HasValue && tendHrsCol.Value > 0)
                            {
                                ws.Cells[row, tendHrsCol.Value].Value = ws.Cells[row, 15].Value;
                                ws.Cells[row, tendHrsCol.Value].Style.Numberformat.Format = "#0.00_);";
                            }
                        }
                        else if (!isCompanyOwned)
                        {
                            if (otherTugsTendingCol > 0)
                            {
                                ws.Cells[row, otherTugsTendingCol].Value = 1;
                                ws.Cells[row, otherTugsTendingCol].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                            }
                            var oTendHrsCol = isForeign ? otherTugsTendHrsForCol : otherTugsTendHrsLocCol;
                            if (oTendHrsCol > 0)
                            {
                                ws.Cells[row, oTendHrsCol].Value = ws.Cells[row, 15].Value;
                                ws.Cells[row, oTendHrsCol].Style.Numberformat.Format = "#0.00_);";
                            }
                        }
                    }

                    // DOC/UNDOC Column (VFP line 545: IIF(EMPTY(number),'',IIF(undocumented,'UNDOC','DOC')))
                    if (docUndocCol > 0)
                    {
                        if (string.IsNullOrWhiteSpace(t.Billing?.MsapBillingNumber))
                        {
                            ws.Cells[row, docUndocCol].Value = "";
                        }
                        else
                        {
                            ws.Cells[row, docUndocCol].Value = t.Billing.IsUndocumented ? "UNDOC" : "DOC";
                        }
                        ws.Cells[row, docUndocCol].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    }

                    // PRINCIPAL Column
                    if (principalCol > 0)
                    {
                        ws.Cells[row, principalCol].Value = t.Billing?.Principal?.PrincipalName?.Trim() ?? "";
                    }

                    row++;
                }

                int dataStartRow = 7, lastDataRow = row - 1, totalRow = row;
                ws.Cells[totalRow, 1].Value = "TOTAL";
                ws.Cells[totalRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                decimal Number(int r, int c)
                {
                    var value = ws.Cells[r, c].Value;
                    return value is null || (c == 33 && value is string) ? 0m : Convert.ToDecimal(value);
                }
                // ponytail: scans report rows for each lower-summary line; group first if exports become slow.
                void WriteActivity(int targetRow, string? tugName, bool owned, bool? ioc, bool foreign)
                {
                    var rows = data.Where(t =>
                        SalesReportRecognition.IncludesService(t, dateFrom, dateTo) &&
                        t.Tugboat.IsCompanyOwned == owned &&
                        (tugName == null || t.Tugboat.TugboatName == tugName) &&
                        (!ioc.HasValue || IsIoc(t) == ioc.Value) &&
                        (t.Vessel.VesselType?.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase) == true) == foreign).ToList();
                    var tending = rows.Where(t => (t.Service?.ServiceName ?? "")
                        .Contains("TENDING", StringComparison.OrdinalIgnoreCase)).ToList();
                    ws.Cells[targetRow, 6].Value = tending.Count;
                    ws.Cells[targetRow, 7].Value = Number(targetRow, 5) + tending.Count;
                    ws.Cells[targetRow, 8].Value = tending.Sum(t => t.TotalHours);
                    ws.Cells[targetRow, 9].Value = rows.Sum(t => t.TotalHours) - tending.Sum(t => t.TotalHours);
                }

                // Main Table TOTAL Row Formulas & Formatting (VFP lines 641-664)
                for (int c = 1; c <= totalCols; c++)
                {
                    var (label, sec) = colInfo[c - 1];
                    if (c is 15 or 16 or 17 or 18 or 19 or 20 or 25 or 26 or 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 || sec == 1 || sec == 2 || sec == 3)
                    {
                        ws.Cells[totalRow, c].Value = Enumerable.Range(dataStartRow, Math.Max(0, lastDataRow - dataStartRow + 1)).Sum(r => Number(r, c));
                        ws.Cells[totalRow, c].Style.Numberformat.Format = "#,##0.00_);[Red](#,##0.00);";
                    }
                    else if (sec == 4 || sec == 5)
                    {
                        ws.Cells[totalRow, c].Value = Enumerable.Range(dataStartRow, Math.Max(0, lastDataRow - dataStartRow + 1)).Sum(r => Number(r, c));
                        ws.Cells[totalRow, c].Style.Numberformat.Format = "##0_);(##0);";
                        ws.Cells[totalRow, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    }
                    else if (sec == 6 && label != "" && label != "DOC/UNDOC" && label != "PRINCIPAL")
                    {
                        ws.Cells[totalRow, c].Value = Enumerable.Range(dataStartRow, Math.Max(0, lastDataRow - dataStartRow + 1)).Sum(r => Number(r, c));
                        ws.Cells[totalRow, c].Style.Numberformat.Format = "#,##0.00_);(#,##0.00);";
                    }
                }

                if (data.Any(t => !SalesReportRecognition.IncludesAmounts(t, dateFrom, dateTo)))
                {
                    foreach (var column in new[] { 17, 18, 33, 34, 35 })
                    {
                        ws.Cells[totalRow, column].AddComment("Service-only rows contribute no financial amounts. Other Tug amounts are recognized in the invoice confirmation period.", "MMSI");
                    }
                }

                using (var rng = ws.Cells[totalRow, 1, totalRow, totalCols])
                {
                    rng.Style.Font.Size = 11;
                    rng.Style.Font.Bold = true;
                    rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rng.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(204, 255, 255)); // Lite Turquoise (ColorIndex 20)
                    rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
                }

                // =========================================================================
                // LOWER SUMMARY BLOCK ("TAKEN FROM SALES SUMMARY REPORT:")
                // =========================================================================
                int summaryStartRow = totalRow + 6;
                ws.Cells[summaryStartRow, 1].Value = "TAKEN FROM SALES SUMMARY REPORT:";
                ws.Cells[summaryStartRow, 5].Value = "DOCK/UND";
                ws.Cells[summaryStartRow, 6].Value = "TENDING";
                ws.Cells[summaryStartRow, 7].Value = "TOTAL MOVES";
                ws.Cells[summaryStartRow, 8].Value = "TOTAL TENDING HRS";
                ws.Cells[summaryStartRow, 9].Value = "TOTAL TOWING HRS";

                using (var rng = ws.Cells[summaryStartRow, 5, summaryStartRow, 9])
                {
                    rng.Style.Font.Bold = true;
                    rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }

                using (var rng = ws.Cells[summaryStartRow, 1, summaryStartRow, 9])
                {
                    rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                }

                var subtotalRows = new List<int>();
                int currRow = summaryStartRow;

                // Per Company-Owned Tugboat Summary (VFP lines 681-736)
                foreach (var tug in companyOwnedTugboats)
                {
                    var name = tug.TugboatName;
                    if (!tugboatCols.TryGetValue(name, out var tcSummary))
                    {
                        continue;
                    }

                    // IOC LOCAL
                    currRow += 2;
                    ws.Cells[currRow, 1].Value = name;
                    ws.Cells[currRow, 3].Value = "IOC";
                    ws.Cells[currRow, 4].Value = "LOCAL";
                    if (tcSummary.AssistsLocalIoc.HasValue)
                    {
                        ws.Cells[currRow, 5].Value = Number(totalRow, tcSummary.AssistsLocalIoc.Value);
                    }

                    WriteActivity(currRow, name, true, true, false);

                    // IOC FOREIGN
                    currRow += 1;
                    ws.Cells[currRow, 3].Value = "IOC";
                    ws.Cells[currRow, 4].Value = "FOREIGN";
                    if (tcSummary.AssistsForeignIoc.HasValue)
                    {
                        ws.Cells[currRow, 5].Value = Number(totalRow, tcSummary.AssistsForeignIoc.Value);
                    }

                    WriteActivity(currRow, name, true, true, true);

                    // OTHER PORT LOCAL
                    currRow += 1;
                    ws.Cells[currRow, 3].Value = "OTHER PORT";
                    ws.Cells[currRow, 4].Value = "LOCAL";
                    if (tcSummary.AssistsLocalOutside.HasValue)
                    {
                        ws.Cells[currRow, 5].Value = Number(totalRow, tcSummary.AssistsLocalOutside.Value);
                    }

                    WriteActivity(currRow, name, true, false, false);

                    // OTHER PORT FOREIGN
                    currRow += 1;
                    ws.Cells[currRow, 3].Value = "OTHER PORT";
                    ws.Cells[currRow, 4].Value = "FOREIGN";
                    if (tcSummary.AssistsForeignOutside.HasValue)
                    {
                        ws.Cells[currRow, 5].Value = Number(totalRow, tcSummary.AssistsForeignOutside.Value);
                    }

                    WriteActivity(currRow, name, true, false, true);

                    // SUB TOTAL - <TUGBOAT>
                    currRow += 1;
                    ws.Cells[currRow, 3].Value = $"SUB TOTAL - {name}";
                    for (int c = 5; c <= 9; c++)
                    {
                        ws.Cells[currRow, c].Value = Enumerable.Range(currRow - 4, 4).Sum(r => Number(r, c));
                    }

                    using (var rng = ws.Cells[currRow, 1, currRow, 9])
                    {
                        rng.Style.Font.Bold = true;
                        rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        rng.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(204, 255, 255));
                        rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        rng.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    }
                    subtotalRows.Add(currRow);
                }

                // OTHER TUGS Summary (VFP lines 738-766)
                currRow += 2;
                ws.Cells[currRow, 1].Value = "OTHER TUGS";
                ws.Cells[currRow, 3].Value = "ALL PORTS";
                ws.Cells[currRow, 4].Value = "LOCAL";
                if (otherTugsAssistsLocCol > 0)
                {
                    ws.Cells[currRow, 5].Value = Number(totalRow, otherTugsAssistsLocCol);
                }

                WriteActivity(currRow, null, false, null, false);

                currRow += 1;
                ws.Cells[currRow, 3].Value = "ALL PORTS";
                ws.Cells[currRow, 4].Value = "FOREIGN";
                if (otherTugsAssistsForCol > 0)
                {
                    ws.Cells[currRow, 5].Value = Number(totalRow, otherTugsAssistsForCol);
                }

                WriteActivity(currRow, null, false, null, true);

                // SUB TOTAL - OTHER TUGS
                currRow += 1;
                ws.Cells[currRow, 3].Value = "SUB TOTAL - OTHER TUGS";
                for (int c = 5; c <= 9; c++)
                {
                    ws.Cells[currRow, c].Value = Enumerable.Range(currRow - 2, 2).Sum(r => Number(r, c));
                }

                using (var rng = ws.Cells[currRow, 1, currRow, 9])
                {
                    rng.Style.Font.Bold = true;
                    rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rng.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(204, 255, 255));
                    rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                }
                subtotalRows.Add(currRow);

                // OVER ALL TOTAL (VFP lines 768-806)
                currRow += 2;
                ws.Cells[currRow, 1].Value = "OVER ALL TOTAL";
                if (subtotalRows.Count > 0)
                {
                    for (int c = 5; c <= 9; c++)
                    {
                        ws.Cells[currRow, c].Value = subtotalRows.Sum(r => Number(r, c));
                    }
                }

                using (var rng = ws.Cells[currRow, 1, currRow, 9])
                {
                    rng.Style.Font.Bold = true;
                    rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rng.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 255, 204)); // Ivory (ColorIndex 19)
                    rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    rng.Style.Border.Bottom.Style = ExcelBorderStyle.Double;
                }

                // Format lower summary grid alignment and numbers
                var summaryDataRng = ws.Cells[summaryStartRow + 2, 5, currRow, 9];
                summaryDataRng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Cells[summaryStartRow + 2, 5, currRow, 7].Style.Numberformat.Format = "##0_);(##0);";
                ws.Cells[summaryStartRow + 2, 8, currRow, 9].Style.Numberformat.Format = "##0.00_);(##0.00);";

                FinalizeColumns(ws, dataStartRow, lastDataRow, totalCols);
                ws.Column(33).Width = 38;
                if (principalCol > 0)
                {
                    ws.Column(principalCol).Width = 50;
                }

                // Export a snapshot. Excel should receive values, not hidden report logic.
                ws.Calculate();
                for (int r = 1; r <= ws.Dimension.End.Row; r++)
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                {
                    var cell = ws.Cells[r, c];
                    if (!string.IsNullOrEmpty(cell.Formula))
                    {
                        var value = cell.Value;
                        cell.Formula = null;
                        cell.Value = value;
                    }
                }

                var content = await pkg.GetAsByteArrayAsync(ct);
                if (isFinalReport)
                {
                    content = await unitOfWork.Report.SaveFinalSalesReportAsync(dateFrom, dateTo, content, generatedAt, ct);
                }
                return File(content, contentType, fileName);
            }
            catch (Exception ex)
            {
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        private static bool TryStripSuffix(string label, string suffix, out string name)
        {
            if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = label[..^suffix.Length];
                return true;
            }
            name = string.Empty;
            return false;
        }

        private sealed class TugboatCols
        {
            public int? Income, Hours;
            public int? AssistsLocalIoc, AssistsForeignIoc, AssistsLocalOutside, AssistsForeignOutside;
            public int? Tending, TendingHoursLocal, TendingHoursForeign;
        }

        private static List<(string label, int section)> BuildSalesSummaryColumns(
            List<Tugboat> tugboats, List<TugboatOwner> owners, List<Customer> customers)
        {
            var cols = new List<(string, int)>();

            // Detail Columns (Section 0)
            var detailHeaders = new[] {
                "BILLING STATEMENT DATE/DISPATCH DATE", "DISPATCH TICKET NUMBER", "BILLING STATEMENT #",
                "CUSTOMER NAME", "NAME OF VESSEL", "TYPE OF VESSEL", "NAME OF TUGBOAT", "SUBCON/OWNED", "PORT", "TERMINAL",
                "IOC/NON-IOC", "NATURE OF SERVICE", "TIME STARTED", "TIME END", "NO. OF HRS", "RATE", "GROSS SALES",
                "NET OF VAT", "VAT", "WHT", "DATE DEPOSITED", "RECEIPT DATE", "RECEIPT NUMBER", "BANK",
                "VATABLE AMOUNT", "EWT", "AMOUNT DEPOSITED", "SBMA SHARE", "OVERPAYMENT", "AGENCY INCENTIVE", "AGENT COMMISSION",
                "BALANCE", "AP OTHER TUGS", "AP OTHER TUGS(NET OF VAT)", "NET SALES" };

            foreach (var h in detailHeaders)
            {
                cols.Add((h, 0));
            }

            // Section 1: FOR PNL USE (Tugboat Income + Other Tugs Income + Tugboat Hours)
            foreach (var t in tugboats)
            {
                cols.Add(($"INCOME FROM {t.TugboatName}", 1));
            }
            cols.Add(("INCOME FROM OTHER TUGS", 1));

            foreach (var t in tugboats)
            {
                cols.Add(($"{t.TugboatName} # OF HOURS", 1));
            }

            // Section 2: AP LEDGER (Owners)
            foreach (var o in owners)
            {
                cols.Add((o.TugboatOwnerName, 2));
            }

            // Section 3: A/R LEDGER (Customers + TOTAL)
            foreach (var c in customers)
            {
                cols.Add((c.CustomerName, 3));
            }
            cols.Add(("TOTAL", 3));

            // Section 4: Number of ASSISTS (Grouped by Category across tugboats, then Other Tugs)
            foreach (var t in tugboats)
            {
                cols.Add(($"{t.TugboatName} LOCAL (IOC)", 4));
            }
            foreach (var t in tugboats)
            {
                cols.Add(($"{t.TugboatName} FOREIGN (IOC)", 4));
            }
            foreach (var t in tugboats)
            {
                cols.Add(($"{t.TugboatName} LOCAL (OUTSIDE)", 4));
            }
            foreach (var t in tugboats)
            {
                cols.Add(($"{t.TugboatName} FOREIGN (OUTSIDE)", 4));
            }
            cols.Add(("OTHER TUGS LOCAL", 4));
            cols.Add(("OTHER TUGS FOREIGN", 4));

            // Section 5: Number of TENDING (Per Tugboat + Other Tugs)
            foreach (var t in tugboats)
            {
                cols.Add((t.TugboatName, 5));
            }
            cols.Add(("OTHER TUGS", 5));

            // Section 6: Number of TENDING HOURS (Local & Foreign per Tugboat, then Other Tugs, Blank, DOC/UNDOC, PRINCIPAL)
            foreach (var t in tugboats)
            {
                cols.Add(($"{t.TugboatName} TENDING HOURS - LOCAL", 6));
                cols.Add(($"{t.TugboatName} TENDING HOURS - FOREIGN", 6));
            }
            cols.Add(("OTHER TUGS LOCAL", 6));
            cols.Add(("OTHER TUGS FOREIGN", 6));

            cols.Add(("", 6));
            cols.Add(("DOC/UNDOC", 6));
            cols.Add(("PRINCIPAL", 6));

            return cols;
        }

        private static void SetDecimal(ExcelWorksheet ws, int row, int col, decimal val)
        {
            if (val == 0)
            {
                return;
            }

            ws.Cells[row, col].Value = val;
            ws.Cells[row, col].Style.Numberformat.Format = "#,##0.00";
        }

        private static string? FormatLegacyDateTime(DateOnly? date, TimeOnly? time)
        {
            if (!date.HasValue)
            {
                return null;
            }

            var d = date.Value.ToString("MM/dd/yyyy");
            return time.HasValue ? $"{d} {time.Value:HH:mm}" : d;
        }

        private static void WriteCompanyHeader(ExcelWorksheet ws)
        {
            ws.Cells.Style.Font.Name = "Calibri";
            ws.Cells["A1"].Value = "MALAYAN MARITIME SERVICES INC.";
            ws.Cells["A1"].Style.Font.Size = 16;
            ws.Cells["A1"].Style.Font.Bold = true;
        }

        // Keep every report column visible; only size the columns for readability.
        private static void FinalizeColumns(ExcelWorksheet ws, int dataStart, int lastRow, int totalCols)
        {
            ws.Cells[dataStart, 1, lastRow, totalCols].AutoFitColumns();
            for (int c = 1; c <= totalCols; c++)
            {
                if (ws.Column(c).Width < 14)
                {
                    ws.Column(c).Width = 14;
                }
            }
        }

        private static void MergeRowRange(ExcelWorksheet ws, int row, int startCol, int endCol, string label)
        {
            var rng = ws.Cells[row, startCol, row, endCol];
            rng.Merge = true;
            rng.Value = label;
            rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            rng.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        private static void StyleSection(ExcelWorksheet ws, int row, int startCol, int endCol, Color color)
        {
            using var rng = ws.Cells[row, startCol, row, endCol];
            rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rng.Style.Fill.BackgroundColor.SetColor(color);
            rng.Style.Font.Size = 8;
            rng.Style.Font.Bold = true;
            rng.Style.WrapText = true;
            rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            rng.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            rng.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            rng.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            rng.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        }

        private static void StyleHeader(ExcelWorksheet ws, int row, int colCount)
        {
            using var rng = ws.Cells[row, 1, row, colCount];
            rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rng.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
            rng.Style.Font.Size = 8;
            rng.Style.Font.Bold = true;
            rng.Style.WrapText = true;
            rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            rng.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            rng.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            rng.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            rng.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            rng.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        }

        private static string? FormatDateTime(DateOnly? date, TimeOnly? time)
        {
            return date.HasValue
                ? time.HasValue ? $"{date:MM/dd/yyyy} {time:HH:mm}" : date.Value.ToString("MM/dd/yyyy")
                : null;
        }

        private async Task<(Dictionary<int, (decimal Amount, decimal Ewt)> Shares, Dictionary<int, List<Collection>> CollectionsByTicket)> AllocateCollectionsByTicketAsync(
            List<DispatchTicket> reportTickets, CancellationToken ct)
        {
            var shares = new Dictionary<int, (decimal Amount, decimal Ewt)>();
            var collectionsByTicket = new Dictionary<int, List<Collection>>();
            var sourceBillingIds = reportTickets.Where(t => t.BillingId.HasValue).Select(t => t.BillingId!.Value).Distinct().ToArray();
            if (sourceBillingIds.Length == 0)
            {
                return (shares, collectionsByTicket);
            }

            var billings = (await unitOfWork.Billing.GetAllAsync(b => sourceBillingIds.Contains(b.MsapBillingId)
                || b.BasicBillingId.HasValue && sourceBillingIds.Contains(b.BasicBillingId.Value), ct))
                .Where(b => b.CollectionId.HasValue).ToList();
            if (billings.Count == 0)
            {
                return (shares, collectionsByTicket);
            }

            var collectionIds = billings.Select(b => b.CollectionId!.Value).Distinct().ToArray();
            // Keep bills outside the report filter in each receipt's allocation denominator.
            billings = (await unitOfWork.Billing.GetAllAsync(b => b.CollectionId.HasValue
                && collectionIds.Contains(b.CollectionId.Value), ct)).ToList();
            var collections = (await unitOfWork.Collection.GetAllAsync(c => collectionIds.Contains(c.MsapCollectionId), ct))
                .ToDictionary(c => c.MsapCollectionId);
            var relatedBillingIds = billings.Select(b => b.BasicBillingId ?? b.MsapBillingId).Distinct().ToArray();
            var allTickets = await unitOfWork.DispatchTicket.GetAllAsync(
                t => t.BillingId.HasValue && relatedBillingIds.Contains(t.BillingId.Value), ct);
            var ticketsByBilling = allTickets.GroupBy(t => t.BillingId!.Value).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var group in billings.GroupBy(b => b.CollectionId!.Value).OrderBy(g => g.Key))
            {
                if (!collections.TryGetValue(group.Key, out var collection))
                {
                    continue;
                }

                var billingAmounts = CollectionService.GetCollectedBillingAmounts(collection, group);
                foreach (var billing in group)
                {
                    var sourceBillingId = billing.BasicBillingId ?? billing.MsapBillingId;
                    if (!ticketsByBilling.TryGetValue(sourceBillingId, out var billingTickets))
                    {
                        continue;
                    }

                    var tickets = billingTickets.OrderBy(t => t.DispatchTicketId).Select(ticket => new
                    {
                        ticket.DispatchTicketId,
                        Weight = Math.Max(billing.BasicBillingId.HasValue ? ticket.BAFNetRevenue
                            : billing.BafBilling != null ? ticket.DispatchNetRevenue
                            : ticket.DispatchNetRevenue + ticket.BAFNetRevenue, 0m)
                    }).ToList();
                    var totalWeight = tickets.Sum(t => t.Weight);
                    var settled = billingAmounts[billing.MsapBillingId];
                    var cumulativeWeight = 0m;
                    var previousAmount = 0m;
                    var previousEwt = 0m;
                    foreach (var ticket in tickets)
                    {
                        cumulativeWeight += totalWeight > 0m ? ticket.Weight : 1m;
                        var denominator = totalWeight > 0m ? totalWeight : tickets.Count;
                        var amount = Math.Round((settled.Amount * cumulativeWeight) / denominator, 2, MidpointRounding.AwayFromZero);
                        var ewt = Math.Round((settled.Ewt * cumulativeWeight) / denominator, 2, MidpointRounding.AwayFromZero);
                        var oldShare = shares.GetValueOrDefault(ticket.DispatchTicketId);
                        shares[ticket.DispatchTicketId] = ((oldShare.Amount + amount) - previousAmount, (oldShare.Ewt + ewt) - previousEwt);
                        if (!collectionsByTicket.TryGetValue(ticket.DispatchTicketId, out var ticketCollections))
                        {
                            ticketCollections = [];
                            collectionsByTicket[ticket.DispatchTicketId] = ticketCollections;
                        }
                        if (!ticketCollections.Any(c => c.MsapCollectionId == collection.MsapCollectionId))
                        {
                            ticketCollections.Add(collection);
                        }
                        previousAmount = amount;
                        previousEwt = ewt;
                    }
                }
            }

            return (shares, collectionsByTicket);
        }

        private static string? JoinCollectionValues(IEnumerable<Collection> collections, Func<Collection, string?> valueSelector)
        {
            var values = collections.Select(valueSelector).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
            return values.Count == 0 ? null : string.Join(", ", values);
        }

        private static decimal? NullIfZero(decimal val) => val == 0 ? null : val;
    }
}
