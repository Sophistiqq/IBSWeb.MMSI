using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.Filpride.AccountsPayable
{
    public class FilprideMsapCommissionPayment
    {
        public int Id { get; set; }

        public int CheckVoucherHeaderId { get; set; }

        public FilprideCheckVoucherHeader Voucher { get; set; } = null!;

        public int MsapBillingId { get; set; }

        public int RepresentativeId { get; set; }

        [MaxLength(30)]
        public string Category { get; set; } = string.Empty;

        public int BillingRevision { get; set; }

        [MaxLength(20)]
        public string BillingNumber { get; set; } = string.Empty;

        public string SourceSnapshot { get; set; } = string.Empty;

        [Column(TypeName = "numeric(18,4)")]
        public decimal GrossAmount { get; set; }

        [Column(TypeName = "numeric(18,4)")]
        public decimal WithholdingAmount { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime? ReleasedAt { get; set; }
    }
}
