using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP
{
    public class OtherTugCharge
    {
        public int OtherTugChargeId { get; set; }
        public int TugboatOwnerId { get; set; }
        public TugboatOwner TugboatOwner { get; set; } = null!;
        [MaxLength(200)]
        public string ProviderName { get; set; } = "";
        [MaxLength(100)]
        public string ProviderReference { get; set; } = "";
        [Column(TypeName = "date")]
        public DateOnly DocumentDate { get; set; }
        public bool IncludesVat { get; set; }
        [MaxLength(500)]
        public string? DocumentName { get; set; }
        [MaxLength(500)]
        public string? Remarks { get; set; }
        public int Revision { get; set; } = 1;
        [MaxLength(450)]
        public string PreparedByUserId { get; set; } = "";
        [MaxLength(255)]
        public string PreparedByName { get; set; } = "";
        [Column(TypeName = "timestamp without time zone")]
        public DateTime UpdatedAt { get; set; }
        [MaxLength(450)]
        public string? ConfirmedByUserId { get; set; }
        [MaxLength(255)]
        public string? ConfirmedByName { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? ConfirmedAt { get; set; }
        public List<OtherTugChargeLine> Lines { get; set; } = [];
    }
}
