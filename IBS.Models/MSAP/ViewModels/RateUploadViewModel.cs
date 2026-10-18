namespace IBS.Models.MSAP.ViewModels
{
    public class RateUploadViewModel
    {
        public RateUpload? Upload { get; set; }
        public List<RateUploadRowViewModel> Rows { get; set; } = [];
        public List<string> Errors { get; set; } = [];
        public bool BaselineChanged { get; set; }
        public bool IsOwner { get; set; }
        public List<TicketChargeReviewViewModel> AffectedTickets { get; set; } = [];
        public bool ImpactLimitReached { get; set; }
    }
}
