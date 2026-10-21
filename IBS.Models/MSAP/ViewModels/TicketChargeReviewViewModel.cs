namespace IBS.Models.MSAP.ViewModels
{
    public class TicketChargeReviewViewModel
    {
        public int DispatchTicketId { get; set; }
        public int CustomerOrderSlipId { get; set; }
        public string TicketNumber { get; set; } = "";
        public string CosNumber { get; set; } = "";
        public bool HasSavedCharges { get; set; }
        public decimal SavedBafRate { get; set; }
        public decimal ProposedBafRate { get; set; }
        public decimal SavedGross { get; set; }
        public decimal ProposedGross { get; set; }
        public decimal SavedNet { get; set; }
        public decimal ProposedNet { get; set; }
        public string Fingerprint { get; set; } = "";
    }
}
