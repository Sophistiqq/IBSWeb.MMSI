using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess(ProcedureEnum.ViewRates, ProcedureEnum.ManageRates, ProcedureEnum.ApproveRateUpload)]
    public class TariffRateController : Controller
    {
        private readonly ITariffRateService _rates;

        public TariffRateController(ITariffRateService rates)
        {
            _rates = rates;
        }

        [HttpGet]
        public async Task<IActionResult> Export(CancellationToken cancellationToken, DateOnly? asOfDate = null,
            DateOnly? dateFrom = null, DateOnly? dateTo = null, bool history = false, RateTableKind kind = RateTableKind.Tariff)
        {
            if (!ModelState.IsValid || !Enum.IsDefined(kind) || history && (!dateFrom.HasValue || !dateTo.HasValue))
            {
                TempData["error"] = "Choose a valid rate table and effective dates for the export.";
                return RedirectToAction("Editor", "RateUpload");
            }
            DateOnly through = history ? dateTo!.Value : asOfDate ?? DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime());
            ServiceResult<byte[]> result = await _rates.ExportAsync(history ? dateFrom : null, through, cancellationToken, kind);
            if (!result.IsSuccess)
            {
                TempData["error"] = result.Message;
                return RedirectToAction("Editor", "RateUpload");
            }
            string filename = history ? $"MMSI_{kind}_History_{dateFrom:yyyyMMdd}_{through:yyyyMMdd}.xlsx" : $"MMSI_{kind}_Rates_{through:yyyyMMdd}.xlsx";
            return File(result.Data!, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }
    }
}
