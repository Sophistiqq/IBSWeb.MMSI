namespace IBS.Models.MSAP.ViewModels
{
    public class RatePendingItemViewModel
    {
        public int ScheduleId { get; set; }

        public int EntryId { get; set; }

        public int? CustomerOrderSlipId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string VesselName { get; set; } = string.Empty;

        public string TugboatName { get; set; } = string.Empty;

        public string ServiceName { get; set; } = string.Empty;

        public string TerminalName { get; set; } = string.Empty;

        public DateTime PlannedDate { get; set; }

        public DateTime CreatedDate { get; set; }

        public double HoursPending { get; set; }

        public bool IsPast24h => HoursPending >= 24;

        public int? DispatchTicketId { get; set; }

        public string? DispatchNumber { get; set; }
    }
}
