using System.ComponentModel.DataAnnotations;
using IBS.Models.MSAP.Enums;
using Microsoft.AspNetCore.Http;

namespace IBS.Models.MSAP.ViewModels
{
    public class SpotQuotationViewModel
    {
        [Required]
        public int VesselScheduleId { get; set; }

        [Required]
        public int VesselScheduleServiceEntryId { get; set; }

        public string? VesselName { get; set; }

        public string? TugboatName { get; set; }

        public string? ServiceName { get; set; }

        public string? CustomerName { get; set; }

        [Required(ErrorMessage = "Quotation reference number is required.")]
        [Display(Name = "Quotation Reference #")]
        [StringLength(50)]
        public string QuotationReference { get; set; } = string.Empty;

        [Required(ErrorMessage = "Approved Basic Tariff Rate is required.")]
        [Range(0.01, 9999999999.9999, ErrorMessage = "Tariff Rate must be greater than 0.")]
        [Display(Name = "Basic Tariff Rate (₱)")]
        public decimal TariffRate { get; set; }

        [Required]
        [Display(Name = "Tariff Basis")]
        public TariffChargeBasis TariffBasis { get; set; } = TariffChargeBasis.PerHour;

        [Required(ErrorMessage = "Approved BAF Rate is required.")]
        [Range(0, 9999999999.9999, ErrorMessage = "BAF Rate cannot be negative.")]
        [Display(Name = "BAF Rate (₱)")]
        public decimal BafRate { get; set; }

        [Required]
        [Display(Name = "BAF Method")]
        public BafCalculationMethod BafMethod { get; set; } = BafCalculationMethod.PerHour;

        [Display(Name = "Special Terms / Remarks")]
        [StringLength(500)]
        public string? Remarks { get; set; }

        [Display(Name = "Supporting Quotation Document")]
        public IFormFile? QuotationFile { get; set; }

        public string? ExistingAttachmentUrl { get; set; }
    }
}
