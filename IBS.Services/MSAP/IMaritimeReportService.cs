using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;

namespace IBS.Services.MSAP
{
    public interface IMaritimeReportService
    {
        Task<PostedMonthAdjustmentsViewModel> BuildPostedMonthAdjustmentsViewModelAsync(
            int? year,
            int? month,
            string? documentType,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            CancellationToken ct);

        string BuildFilterSummary(PostedMonthAdjustmentsViewModel model);

        Task<MsapAdjustmentLog?> GetAdjustmentLogAsync(int id, CancellationToken ct);

        Task<byte[]> ExportPostedMonthAdjustmentsExcelAsync(
            int? year,
            int? month,
            string? documentType,
            DateOnly? dateFrom,
            DateOnly? dateTo,
            CancellationToken ct);

        Task<byte[]> ExportSalesBookExcelAsync(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct);

        Task<byte[]> ExportDispatchForBillingExcelAsync(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct);

        Task<byte[]> ExportDispatchTicketSummaryExcelAsync(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct);

        Task<byte[]> ExportSalesSummaryExcelAsync(DateOnly dateFrom, DateOnly dateTo, CancellationToken ct);
    }
}
