using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP
{
    public class BafRate
    {
        [Key]
        public int BafRateId { get; set; }
        public DateOnly AsOfDate { get; set; }
        public int CustomerId { get; set; }
        public Customer Customer { get; set; } = null!;
        public int PortId { get; set; }
        public Port Port { get; set; } = null!;
        public int TerminalId { get; set; }
        public Terminal Terminal { get; set; } = null!;
        public int ServiceId { get; set; }
        public Service Service { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? ApprovedBy { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? ApprovedAt { get; set; }

        public string? CreatedBy { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? CreatedDate { get; set; }
        public string? UpdateBy { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? UpdateDate { get; set; }
    }
}
