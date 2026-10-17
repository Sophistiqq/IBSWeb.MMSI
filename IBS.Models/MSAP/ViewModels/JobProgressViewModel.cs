using IBS.Models.MSAP.Enums;

namespace IBS.Models.MSAP.ViewModels
{
    public class JobProgressViewModel
    {
        public int Stage { get; set; }
        public bool HasSchedule { get; set; }
        public int? VesselScheduleId { get; set; }
        public int ExpectedTicketCount { get; set; }
        public int RecordedTicketCount { get; set; }
        public List<Billing> LinkedBillings { get; set; } = [];
        public bool Stopped { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsInvalidated { get; set; }
        public string Guidance { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public string ActionController { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public int? TargetId { get; set; }
        public int? CustomerOrderSlipId { get; set; }
        public string? CustomerOrderSlipNumber { get; set; }
        public ProcedureEnum? Permission { get; set; }
        public string WaitingFor { get; set; } = string.Empty;
        public Collection? BankingCollection { get; set; }
        public Dictionary<int, string> CalculationBlockers { get; set; } = [];
        public List<DispatchTicket> Tickets { get; set; } = [];
        public List<Billing> Billings { get; set; } = [];
    }
}
