using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.Filpride.AccountsPayable
{
    public class FilprideTugboatPayment
    {
        public int Id { get; set; }
        public int CheckVoucherHeaderId { get; set; }
        public FilprideCheckVoucherHeader Voucher { get; set; } = null!;
        public int OtherTugChargeId { get; set; }
        public int TugboatOwnerId { get; set; }
        public int? LiabilityJournalVoucherId { get; set; }
        public FilprideJournalVoucherHeader? LiabilityJournalVoucher { get; set; }
        public int ChargeRevision { get; set; }
        [MaxLength(100)]
        public string ProviderReference { get; set; } = "";
        public string SourceSnapshot { get; set; } = "";
        public string LiabilitySnapshot { get; set; } = "";
        [Column(TypeName = "numeric(18,4)")]
        public decimal GrossAmount { get; set; }
        [Column(TypeName = "numeric(18,4)")]
        public decimal WithholdingAmount { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? ReleasedAt { get; set; }
        public List<FilprideTugboatPaymentAllocation> Allocations { get; set; } = [];
    }
}
