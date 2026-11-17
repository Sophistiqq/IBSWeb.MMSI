namespace IBS.Models.MSAP.ViewModels
{
    public class ScheduleRevisionViewModel
    {
        public VesselSchedule Schedule { get; set; } = null!;

        public string? FocusedField { get; set; }

        public List<ScheduleNetChangeViewModel> NetChanges { get; set; } = [];

        public List<ScheduleRevisionEntryViewModel> Revisions { get; set; } = [];
    }

    public class ScheduleNetChangeViewModel
    {
        public string Field { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string OriginalValue { get; set; } = string.Empty;

        public string CurrentValue { get; set; } = string.Empty;

        public int RevisionCount { get; set; }

        public string? DeltaHint { get; set; }
    }

    public class ScheduleRevisionEntryViewModel
    {
        public DateTime EditedDate { get; set; }

        public string EditedBy { get; set; } = string.Empty;

        public string? Reason { get; set; }

        public bool IsApprovalCheckpoint { get; set; }

        public List<MsapFieldChange> Changes { get; set; } = [];
    }
}
