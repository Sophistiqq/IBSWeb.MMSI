using System.Globalization;
using System.Text.Json.Serialization;

namespace IBS.Models.MSAP.ViewModels
{
    public class RateUploadRowViewModel
    {
        public int RowNumber { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string PortCode { get; set; } = "";
        public string TerminalCode { get; set; } = "";
        public string ServiceCode { get; set; } = "";
        public string PortName { get; set; } = "";
        public string TerminalName { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public int CustomerId { get; set; }
        public int PortId { get; set; }
        public int TerminalId { get; set; }
        public int ServiceId { get; set; }
        public DateOnly EffectiveDate { get; set; }
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? Amount { get; set; }
        public string? AmountText => Amount?.ToString(CultureInfo.InvariantCulture);
        public string? StoredAmountText => StoredAmount?.ToString(CultureInfo.InvariantCulture);
        public int? ExistingRateId { get; set; }
        public int? BaselineRateId { get; set; }
        public DateOnly? BaselineDate { get; set; }
        public DateTime? BaselineUpdatedAt { get; set; }
        public decimal? StoredAmount { get; set; }
        public bool Selected { get; set; } = true;
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public string? ApprovalStatus { get; set; }
        public string? LegacySourceId { get; set; }
        public string? LegacyVesselType { get; set; }
        public string Change => !ExistingRateId.HasValue ? "New" : StoredAmount == Amount ? "Unchanged" : "Changed";
    }
}
