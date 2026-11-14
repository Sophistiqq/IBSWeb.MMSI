using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.MSAP.MasterFile
{
    public class MsapRepresentative
    {
        [Key]
        public int RepresentativeId { get; set; }

        [Required]
        [MaxLength(120)]
        [Column(TypeName = "varchar(120)")]
        public string RepresentativeName { get; set; } = null!;

        [Required]
        [MaxLength(30)]
        [Column(TypeName = "varchar(30)")]
        public string Category { get; set; } = "AgentCommission";

        public int? CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        public int? PrincipalId { get; set; }

        [ForeignKey(nameof(PrincipalId))]
        public Principal? Principal { get; set; }

        [Required]
        [MaxLength(20)]
        [Column(TypeName = "varchar(20)")]
        public string RateMode { get; set; } = "Percentage";

        [Column(TypeName = "numeric(18,4)")]
        public decimal RateValue { get; set; }

        [Required]
        [MaxLength(30)]
        [Column(TypeName = "varchar(30)")]
        public string CalculationBase { get; set; } = "NetOfDiscount";

        public bool IsActive { get; set; } = true;

        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime CreatedDate { get; set; }

        [MaxLength(100)]
        public string? EditedBy { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime? EditedDate { get; set; }
    }
}
