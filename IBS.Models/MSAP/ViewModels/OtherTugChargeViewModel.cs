using System.ComponentModel.DataAnnotations;

namespace IBS.Models.MSAP.ViewModels
{
    public class OtherTugChargeViewModel
    {
        public int? Id { get; set; }
        public int Revision { get; set; }
        public int CustomerOrderSlipId { get; set; }
        public int TugboatOwnerId { get; set; }
        public string ProviderName { get; set; } = "";
        [Required, MaxLength(100)]
        public string ProviderReference { get; set; } = "";
        public DateOnly DocumentDate { get; set; }
        [Required]
        public bool? IncludesVat { get; set; }
        [MaxLength(500)]
        public string? Remarks { get; set; }
        public List<OtherTugChargeTicketViewModel> Tickets { get; set; } = [];
        public List<OtherTugChargeLineViewModel> Allocations { get; set; } = [];
        public OtherTugCharge? Charge { get; set; }
        public string? DocumentUrl { get; set; }
        public JobProgressViewModel? Progress { get; set; }
    }
}
