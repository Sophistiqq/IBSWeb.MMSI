using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.MSAP.MasterFile
{
    public class TugboatOwner
    {
        [Key]
        public int TugboatOwnerId { get; set; }

        [Required]
        [StringLength(3, MinimumLength = 3, ErrorMessage = "Tugboat owner number must be 3 characters long.")]
        [Column(TypeName = "varchar(3)")]
        public string TugboatOwnerNumber { get; set; } = null!;

        [Required]
        [StringLength(50, ErrorMessage = "Tugboat owner name cannot exceed 50 characters.")]
        [Column(TypeName = "varchar(50)")]
        public string TugboatOwnerName { get; set; } = null!;

        public decimal FixedRate { get; set; }

        [StringLength(200)]
        [Display(Name = "Payee Address")]
        [Column(TypeName = "varchar(200)")]
        public string? Address { get; set; }

        [StringLength(20)]
        [Display(Name = "TIN")]
        [Column(TypeName = "varchar(20)")]
        public string? Tin { get; set; }

        [StringLength(20)]
        [Display(Name = "VAT Classification")]
        [Column(TypeName = "varchar(20)")]
        public string? VatType { get; set; }

        [StringLength(20)]
        [Display(Name = "Withholding Classification")]
        [Column(TypeName = "varchar(20)")]
        public string? TaxType { get; set; }

        [Range(typeof(decimal), "0", "1")]
        [Column(TypeName = "numeric(18,4)")]
        [Display(Name = "Withholding Rate")]
        public decimal? WithholdingTaxPercent { get; set; }

        [StringLength(10)]
        [Column("msap_recid", TypeName = "varchar(10)")]
        public string? MsapRecId { get; set; }
    }
}
