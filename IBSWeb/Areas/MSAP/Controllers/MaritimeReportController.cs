using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess("Access denied. You don't have permission to view reports.", ProcedureEnum.ViewMaritimeReport)]
    public class MaritimeReportController : Controller
    {
        private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly IMaritimeReportService _reportService;
        private readonly ICloudStorageService? _cloudStorageService;

        [ActivatorUtilitiesConstructor]
        public MaritimeReportController(
            IMaritimeReportService reportService,
            ICloudStorageService? cloudStorageService = null)
        {
            _reportService = reportService;
            _cloudStorageService = cloudStorageService;
        }

        public MaritimeReportController(
            IUnitOfWork unitOfWork,
            ICloudStorageService? cloudStorageService = null)
            : this(new MaritimeReportService(unitOfWork), cloudStorageService)
        {
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> PostedMonthAdjustments(
            int? year,
            int? month,
            string? documentType,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            CancellationToken ct)
        {
            var viewModel = await _reportService.BuildPostedMonthAdjustmentsViewModelAsync(year, month, documentType, dateFrom, dateTo, ct);
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportPostedMonthAdjustmentsExcel(
            int? year,
            int? month,
            string? documentType,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            CancellationToken ct)
        {
            var content = await _reportService.ExportPostedMonthAdjustmentsExcelAsync(year, month, documentType, dateFrom, dateTo, ct);
            var stamp = DateTimeHelper.GetCurrentPhilippineTime().ToString("yyyyMMdd-HHmm");
            return File(content, ExcelContentType, $"MSAP-Posted-Month-Adjustments-{stamp}.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportPostedMonthAdjustmentsPdf(
            int? year,
            int? month,
            string? documentType,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            CancellationToken ct)
        {
            var viewModel = await _reportService.BuildPostedMonthAdjustmentsViewModelAsync(year, month, documentType, dateFrom, dateTo, ct);
            var generatedAt = DateTimeHelper.GetCurrentPhilippineTime();
            var filterSummary = _reportService.BuildFilterSummary(viewModel);

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(8.5f));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("MALAYAN MARITIME SERVICES, INC.").Bold().FontSize(13);
                        col.Item().Text("Posted Month Adjustments & Audit Evidence Report").SemiBold().FontSize(11);
                        col.Item().Text($"Scope: {filterSummary}   |   Generated: {generatedAt:MMM dd, yyyy HH:mm} PHT")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(78);   // Edited Date
                            columns.ConstantColumn(68);   // Doc Type
                            columns.ConstantColumn(76);   // Reference #
                            columns.ConstantColumn(80);   // Edited By
                            columns.RelativeColumn(2);    // Reason
                            columns.RelativeColumn(2.6f); // Diffs
                            columns.ConstantColumn(72);   // Auth Doc
                        });

                        table.Header(header =>
                        {
                            string[] headers = ["Edited Date", "Doc Type", "Reference #", "Edited By", "Reason", "Before -> After Changes", "Auth Doc"];
                            foreach (var h in headers)
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Medium)
                                    .Padding(4).Text(h).Bold().FontSize(8);
                            }
                        });

                        if (viewModel.Items.Count == 0)
                        {
                            table.Cell().ColumnSpan(7).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(10)
                                .AlignCenter().Text("No posted-month adjustments found for the selected filters.").FontColor(Colors.Grey.Darken1);
                        }
                        else
                        {
                            foreach (var item in viewModel.Items)
                            {
                                var diffSummary = item.Changes.Count == 0
                                    ? "—"
                                    : string.Join("\n", item.Changes.Select(c => $"• {c.Label}: {c.Before ?? "—"} -> {c.After ?? "—"}"));

                                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(item.EditedDate.ToString("yyyy-MM-dd\nHH:mm"));
                                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(item.DocumentTypeLabel);
                                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(item.ReferenceNumber).SemiBold();
                                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(item.EditedBy);
                                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(item.Reason);
                                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(diffSummary);
                                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4)
                                    .Text(string.IsNullOrWhiteSpace(item.AttachmentUrl) ? "None" : "Attached");
                            }
                        }
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf();

            var stamp = generatedAt.ToString("yyyyMMdd-HHmm");
            return File(pdfBytes, "application/pdf", $"MSAP-Posted-Month-Adjustments-{stamp}.pdf");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAdjustmentAttachment(int id, CancellationToken ct)
        {
            var log = await _reportService.GetAdjustmentLogAsync(id, ct);
            if (log == null || string.IsNullOrWhiteSpace(log.AttachmentUrl))
            {
                return NotFound("Authorization document not found.");
            }

            if (_cloudStorageService == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Storage service is not configured.");
            }

            var stream = await _cloudStorageService.DownloadFileAsync(log.AttachmentUrl);
            var extension = Path.GetExtension(log.AttachmentUrl).ToLowerInvariant();
            var contentType = extension switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };

            return File(stream, contentType, log.AttachmentUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalesBook(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
        {
            if (!ModelState.IsValid || dateFrom == default || dateTo == default || dateFrom > dateTo)
            {
                return BadRequest("From must not be later than To.");
            }

            var content = await _reportService.ExportSalesBookExcelAsync(dateFrom, dateTo, ct);
            return File(content, ExcelContentType, $"MSAP-Sales-Book-{dateFrom:yyyyMMdd}-{dateTo:yyyyMMdd}.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DispatchForBilling(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
        {
            try
            {
                var content = await _reportService.ExportDispatchForBillingExcelAsync(dateFrom, dateTo, ct);
                return File(content, ExcelContentType, $"Dispatch_For_Billing_{DateTimeHelper.GetCurrentPhilippineTime():yyyyMMdd}.xlsx");
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
                var content = await _reportService.ExportDispatchTicketSummaryExcelAsync(dateFrom, dateTo, ct);
                return File(content, ExcelContentType, $"Dispatch_Ticket_Summary_{DateTimeHelper.GetCurrentPhilippineTime():yyyyMMdd}.xlsx");
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

                var content = await _reportService.ExportSalesSummaryExcelAsync(dateFrom, dateTo, ct);
                return File(content, ExcelContentType, $"Sales_Summary_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
            }
            catch (Exception ex)
            {
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
