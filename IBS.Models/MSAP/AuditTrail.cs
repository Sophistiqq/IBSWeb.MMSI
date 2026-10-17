using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.MSAP
{
    public class AuditTrail(string username, string activity, string documentType, int? recordId = null, string? referenceNumber = null)
    {
        public Guid Id { get; set; }

        public string Username { get; set; } = username;

        [MaxLength(450)]
        public string? UserId { get; set; }

        [MaxLength(255)]
        public string? ActorName { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime Date { get; set; } = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

        [Display(Name = "Machine Name")]
        public string MachineName { get; set; } = Environment.MachineName;

        public string Activity { get; set; } = activity;

        [Display(Name = "Document Type")]
        public string DocumentType { get; set; } = documentType;

        [Display(Name = "Record Id")]
        public int? RecordId { get; set; } = recordId;

        [Display(Name = "Reference Number")]
        public string? ReferenceNumber { get; set; } = referenceNumber;
    }
}
