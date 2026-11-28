using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.Models.MSAP.ViewModels
{
    public class VesselScheduleBoardViewModel
    {
        public DateTime Date { get; set; }
        public DateTime? SelectedDate { get; set; }
        public DateTime? SelectedEndDate { get; set; }
        public int? PortId { get; set; }
        public int? TerminalId { get; set; }
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string ViewMode { get; set; } = "calendar";
        public string? Triage { get; set; }
        public string LocationHeading { get; set; } = "All ports / All terminals";
        public List<SelectListItem> Ports { get; set; } = [];
        public List<SelectListItem> Terminals { get; set; } = [];
        public List<VesselSchedule> Schedules { get; set; } = [];
        public List<VesselSchedule> AllMatchingSchedules { get; set; } = [];
        public int AllMatchingTotalCount { get; set; }
        public List<VesselSchedule> TugBookings { get; set; } = [];
        public Dictionary<DateTime, HashSet<int>> ConflictsByDay { get; set; } = [];
        public Dictionary<int, List<string>> ConflictReasonsBySchedule { get; set; } = [];
        public Dictionary<int, VesselScheduleOpsMeta> OpsMetaBySchedule { get; set; } = [];
        public Dictionary<string, CalendarDaySummaryItem> InitialDaySummaries { get; set; } = [];
        public List<Tugboat> Tugboats { get; set; } = [];
    }

    public class VesselScheduleOpsMeta
    {
        public string StageKey { get; set; } = "scheduled";
        public string StageLabel { get; set; } = "Scheduled";
        public string StageBadgeClass { get; set; } = "status-confirmed";
        public int AssignedTugCount { get; set; }
        public int RequiredTugCount { get; set; }
        public int ActiveServiceCount { get; set; }
        public int LoggedTicketCount { get; set; }
        public bool HasMissingTickets { get; set; }
        public bool HasMissingRates { get; set; }
        public bool HasOverlap { get; set; }
        public List<ScheduleTicketBrief> TicketSummaries { get; set; } = [];
    }

    public class ScheduleTicketBrief
    {
        public int? EntryId { get; set; }
        public int TugboatId { get; set; }
        public int ServiceId { get; set; }
        public string DispatchNumber { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsComplete { get; set; }
    }

    public class CalendarDaySummaryItem
    {
        public string Date { get; set; } = "";
        public int Total { get; set; }
        public int Tentative { get; set; }
        public int Confirmed { get; set; }
        public int Completed { get; set; }
        public int Overlaps { get; set; }
        public int ShortTugs { get; set; }
    }
}
