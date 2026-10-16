using IBS.Models.MSAP;

namespace IBS.Models.MSAP.ViewModels
{
    public class DashboardCountViewModel
    {
        public bool ShowDashboard { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool CanViewDispatch { get; set; }
        public bool CanViewBilling { get; set; }
        public bool CanViewFinance { get; set; }
        public bool CanCreateDispatch { get; set; }
        public bool CanCreateBilling { get; set; }
        public bool CanCreateCollection { get; set; }
        public bool CanCreateCustomerOrderSlip { get; set; }
        public string? DataError { get; set; }
        public string? ScheduleError { get; set; }
        public Dictionary<string, int> DispatchCounts { get; set; } = [];
        public int ForPosting { get; set; }
        public decimal BilledMonth { get; set; }
        public decimal ReceivedMonth { get; set; }
        public int DispatchesMonth { get; set; }
        public int VesselsMonth { get; set; }
        public int ScheduleCount { get; set; }
        public List<VesselSchedule> Schedules { get; set; } = [];
        public Dictionary<int, int> AssignedTugs { get; set; } = [];
        public HashSet<int> ConflictingScheduleIds { get; set; } = [];
        public List<VesselSchedule> UpcomingSchedules { get; set; } = [];
    }
}
