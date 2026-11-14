using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace IBS.Models.MSAP.ViewModels
{
    public class BillingAdjustViewModel
    {
        public int MsapBillingId { get; set; }

        public string MsapBillingNumber { get; set; } = string.Empty;

        public string? COSNumber { get; set; }

        public string? CustomerName { get; set; }

        public string? VesselName { get; set; }

        public string? RejectionReason { get; set; }

        public decimal CosDefaultDiscountPercent { get; set; }

        [Range(0, 100)]
        public decimal CosDiscountPercent { get; set; }

        public bool SkipCosDiscount { get; set; }

        public bool DiscountOnTariff { get; set; } = true;

        public bool DiscountOnBaf { get; set; } = true;

        [Required]
        public string DiscountType { get; set; } = "Percentage";

        [Range(0, 999999999999.99)]
        public decimal FlatDiscountTariff { get; set; }

        [Range(0, 999999999999.99)]
        public decimal FlatDiscountBaf { get; set; }

        public bool IncludeSubconInDiscount { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 5)]
        public string Reason { get; set; } = string.Empty;

        public IFormFile? AuthorizationFile { get; set; }

        public List<BillingAdjustTicketItemViewModel> Tickets { get; set; } = [];
    }

    public class BillingAdjustTicketItemViewModel
    {
        public int DispatchTicketId { get; set; }

        public string DispatchNumber { get; set; } = string.Empty;

        public string TugboatName { get; set; } = string.Empty;

        public string ServiceName { get; set; } = string.Empty;

        public decimal TotalHours { get; set; }

        public decimal EffectiveHours { get; set; }

        [Required]
        public string DispatchChargeType { get; set; } = "Per hour";

        [Range(0, 999999999999.99)]
        public decimal DispatchRate { get; set; }

        [Required]
        public string BAFChargeType { get; set; } = "Per hour";

        [Range(0, 999999999999.99)]
        public decimal BAFRate { get; set; }

        public bool IsSubcon { get; set; }

        public decimal? SubconCost { get; set; }
    }
}
