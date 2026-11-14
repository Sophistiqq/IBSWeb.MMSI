using System.Globalization;
using System.Text.Json.Serialization;

namespace IBS.Models.MSAP.ViewModels
{
    public class RateEditorCombinedRowViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public int PortId { get; set; }
        public string PortCode { get; set; } = "";
        public string PortName { get; set; } = "";
        public int TerminalId { get; set; }
        public string TerminalCode { get; set; } = "";
        public string TerminalName { get; set; } = "";
        public int ServiceId { get; set; }
        public string ServiceCode { get; set; } = "";
        public string ServiceName { get; set; } = "";

        public DateOnly TariffEffectiveDate { get; set; }
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? TariffAmount { get; set; }
        public string? TariffAmountText => TariffAmount?.ToString(CultureInfo.InvariantCulture);
        public int? TariffExistingRateId { get; set; }
        public int? TariffBaselineRateId { get; set; }
        public DateOnly? TariffBaselineDate { get; set; }
        public decimal? TariffStoredAmount { get; set; }
        public string? TariffStoredAmountText => TariffStoredAmount?.ToString(CultureInfo.InvariantCulture);

        public DateOnly BafEffectiveDate { get; set; }
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? BafAmount { get; set; }
        public string? BafAmountText => BafAmount?.ToString(CultureInfo.InvariantCulture);
        public int? BafExistingRateId { get; set; }
        public int? BafBaselineRateId { get; set; }
        public DateOnly? BafBaselineDate { get; set; }
        public decimal? BafStoredAmount { get; set; }
        public string? BafStoredAmountText => BafStoredAmount?.ToString(CultureInfo.InvariantCulture);

        public int? SourceScheduleId { get; set; }
        public int? SourceServiceEntryId { get; set; }
        public int? SourceRevision { get; set; }
        public bool IsPendingTariff { get; set; }
        public bool IsPendingBaf { get; set; }
        public string? ContextNote { get; set; }
    }
}
