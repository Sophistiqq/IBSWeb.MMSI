using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IBS.Models.MSAP.Enums;

namespace IBS.Models.MSAP
{
    public class RateUpload
    {
        [Key]
        public Guid RateUploadId { get; set; } = Guid.NewGuid();
        [MaxLength(20)]
        public string Company { get; set; } = "MMSI";
        [MaxLength(255)]
        public string FileName { get; set; } = "";
        [MaxLength(450)]
        public string UploadedByUserId { get; set; } = "";
        [MaxLength(255)]
        public string UploadedBy { get; set; } = "";
        [Column(TypeName = "timestamp without time zone")]
        public DateTime UploadedAt { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? SubmittedAt { get; set; }
        public RateTableKind RateKind { get; set; } = RateTableKind.Tariff;
        [MaxLength(255)]
        public string? ApprovedBy { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? ApprovedAt { get; set; }
        public int TotalRows { get; set; }
        public int ChangedRows { get; set; }
        [Column(TypeName = "jsonb")]
        public string RowsJson { get; set; } = "[]";
        [NotMapped]
        public string Status => ApprovedAt.HasValue ? "Approved" : SubmittedAt.HasValue ? "Awaiting review" : "Draft";
    }
}
