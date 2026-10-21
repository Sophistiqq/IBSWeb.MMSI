namespace IBS.Models.MSAP.ViewModels
{
    public class OtherTugChargeTicketViewModel
    {
        public int DispatchTicketId { get; set; }
        public string TicketNumber { get; set; } = "";
        public string CosNumber { get; set; } = "";
        public string TugboatName { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public DateOnly? ServiceDate { get; set; }
        public bool Selected { get; set; }
        public decimal? Amount { get; set; }
    }
}
