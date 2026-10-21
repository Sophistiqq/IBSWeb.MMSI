using IBS.Models.MSAP.Enums;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface ITariffRateService
    {
        Task<ServiceResult<byte[]>> ExportAsync(DateOnly? dateFrom, DateOnly dateTo, CancellationToken ct, RateTableKind kind = RateTableKind.Tariff);
    }
}
