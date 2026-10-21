namespace IBS.Models.MSAP.ViewModels
{
    public class DashboardCountViewModel
    {
        public bool ShowDashboard { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool CanFollowUpTickets { get; set; }
        public bool CanReviewCalculations { get; set; }
        public bool CanViewBanking { get; set; }
        public bool CanManageReferences { get; set; }
        public bool CanManageOtherTugCharges { get; set; }
        public bool CanConfirmOtherTugCharges { get; set; }
        public bool CanReadCos { get; set; }
        public int MissingOtherTugTicketCount { get; set; }
        public List<DispatchTicket> MissingOtherTugTickets { get; set; } = [];
        public int OtherTugChargeReviewCount { get; set; }
        public List<OtherTugCharge> OtherTugChargeReviews { get; set; } = [];
        public bool CanCreateSchedule { get; set; }
        public bool CanViewCollections { get; set; }
        public bool CanViewDispatch { get; set; }
        public bool CanViewBilling { get; set; }
        public bool CanViewFinance { get; set; }
        public bool CanCreateBilling { get; set; }
        public bool CanCreateCollection { get; set; }
        public bool CanApproveSchedule { get; set; }
        public bool CanApproveBilling { get; set; }
        public bool CanApproveRates { get; set; }
        public bool CanManageRates { get; set; }
        public bool CanViewRates { get; set; }
        public bool CanViewReports { get; set; }
        public string? DataError { get; set; }
        public string? ScheduleError { get; set; }
        public int RateUploadReviews { get; set; }
        public int ScheduleReviews { get; set; }
        public int ScheduleReapprovals { get; set; }
        public List<VesselSchedule> ScheduleReviewItems { get; set; } = [];
        public List<Billing> BillingReviewItems { get; set; } = [];
        public int ConfirmedToday { get; set; }
        public int NeedingTugs { get; set; }
        public int DueBookings { get; set; }
        public int MissingTicketCount { get; set; }
        public Dictionary<int, int> CompleteTicketsBySchedule { get; set; } = [];
        public List<VesselSchedule> MissingTicketSchedules { get; set; } = [];
        public int PendingCalculationCount { get; set; }
        public List<DispatchTicket> PendingCalculationTickets { get; set; } = [];
        public decimal KnownUnbilledCharges { get; set; }
        public decimal OutstandingBalance { get; set; }
        public Dictionary<string, int> BankingCounts { get; set; } = [];
        public Dictionary<string, decimal> BankingAmounts { get; set; } = [];
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
