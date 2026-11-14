using IBS.Models.MSAP.Enums;

namespace IBS.Models.MSAP.ViewModels
{
    public class BatchRateUpdateRequest
    {
        public string Action { get; set; } = "submit";
        public int? ScheduleId { get; set; }
        public Guid? TariffId { get; set; }
        public Guid? BafId { get; set; }
        public bool RequireTuesdayForBaf { get; set; }
        public string? Fingerprint { get; set; }
        public List<RateUploadRowViewModel> TariffRows { get; set; } = [];
        public List<RateUploadRowViewModel> BafRows { get; set; } = [];
    }

    public class BatchRatePreviewViewModel
    {
        public string Fingerprint { get; set; } = "";
        public bool HasChanges { get; set; }
        public List<BatchRatePreviewItemViewModel> Items { get; set; } = [];
    }

    public class BatchRatePreviewItemViewModel
    {
        public RateTableKind Kind { get; set; }
        public string KindLabel { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string PortName { get; set; } = "";
        public string TerminalName { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public DateOnly EffectiveDate { get; set; }
        public DateOnly? BaselineDate { get; set; }
        public decimal? StoredAmount { get; set; }
        public decimal Amount { get; set; }
        public string Change { get; set; } = "";
    }

    public class BatchRateSaveResult
    {
        public Guid? TariffUploadId { get; set; }
        public Guid? BafUploadId { get; set; }
        public bool Approved { get; set; }
    }
}
