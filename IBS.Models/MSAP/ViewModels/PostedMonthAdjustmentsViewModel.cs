namespace IBS.Models.MSAP.ViewModels
{
    public class PostedMonthAdjustmentsViewModel
    {
        public int? Year { get; set; }

        public int? Month { get; set; }

        public string? DocumentType { get; set; }

        public DateOnly? DateFrom { get; set; }

        public DateOnly? DateTo { get; set; }

        public List<PostedMonthAdjustmentItemViewModel> Items { get; set; } = [];
    }

    public class PostedMonthAdjustmentItemViewModel
    {
        public int Id { get; set; }

        public string DocumentType { get; set; } = string.Empty;

        public string DocumentTypeLabel { get; set; } = string.Empty;

        public int RecordId { get; set; }

        public string ReferenceNumber { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public string EditedBy { get; set; } = string.Empty;

        public DateTime EditedDate { get; set; }

        public string? AttachmentUrl { get; set; }

        public List<MsapFieldChange> Changes { get; set; } = [];
    }
}
