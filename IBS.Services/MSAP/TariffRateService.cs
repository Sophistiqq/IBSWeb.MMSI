using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Helpers;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class TariffRateService : ITariffRateService
    {
        private readonly RateUploadService _rates;
        private readonly ILogger<TariffRateService> _logger;

        public TariffRateService(RateUploadService rates, ILogger<TariffRateService> logger)
        {
            _rates = rates;
            _logger = logger;
        }

        public async Task<ServiceResult<byte[]>> ExportAsync(DateOnly? dateFrom, DateOnly dateTo, CancellationToken ct, RateTableKind kind = RateTableKind.Tariff)
        {
            if (!Enum.IsDefined(kind) || dateTo.Year is < 1900 or > 9998 || dateFrom.HasValue
                && (dateFrom.Value.Year is < 1900 or > 9998 || dateFrom > dateTo))
            {
                return ServiceResult<byte[]>.Failure("Choose a valid table and dates between 1900 and 9998, with From on or before To.");
            }
            try
            {
                List<RateUploadRowViewModel> rows = await _rates.GetRowsAsync(kind, dateFrom, dateTo, ct);
                return ServiceResult<byte[]>.Success(await RateUploadWorkbook.WriteAsync(rows, kind, dateFrom.HasValue, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to export {Kind} rates", kind);
                return ServiceResult<byte[]>.Failure("The rate export could not be generated. Try again or contact support.");
            }
        }
    }
}
