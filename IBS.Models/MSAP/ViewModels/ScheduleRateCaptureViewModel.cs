using IBS.Models.MSAP.Enums;

namespace IBS.Models.MSAP.ViewModels
{
    public class ScheduleRateCaptureViewModel
    {
        public int ScheduleId { get; set; }
        public int EntryId { get; set; }
        public RateTableKind Kind { get; set; }
        public DateOnly EffectiveDate { get; set; }
        public string Fingerprint { get; set; } = "";
        public RateUploadRowViewModel Rate { get; set; } = new();
        public string ChargingContext { get; set; } = "";
        public bool CanCapture { get; set; }
        public Guid? PublishedUpdateId { get; set; }
        public int? CustomerId { get; set; }
        public int? PortId { get; set; }
        public int? TerminalId { get; set; }
        public int? ServiceId { get; set; }
        public decimal? Amount { get; set; }
        public bool IsPendingRate { get; set; }
        public decimal? ApplicableTableRate { get; set; }
    }
}
