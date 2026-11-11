using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.MSAP
{
    [Table("mmsi_adjustment_logs")]
    public class MsapAdjustmentLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Column(TypeName = "varchar(40)")]
        [MaxLength(40)]
        public string DocumentType { get; set; } = string.Empty;

        public int RecordId { get; set; }

        [Column(TypeName = "varchar(50)")]
        [MaxLength(50)]
        public string ReferenceNumber { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "varchar(500)")]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;

        [Column(TypeName = "jsonb")]
        public string ChangesJson { get; set; } = "[]";

        [Required]
        [Column(TypeName = "varchar(100)")]
        [MaxLength(100)]
        public string EditedBy { get; set; } = string.Empty;

        [Column(TypeName = "timestamp without time zone")]
        public DateTime EditedDate { get; set; }
    }

    public class MsapFieldChange
    {
        public string Field { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Before { get; set; }
        public string? After { get; set; }
    }
}
