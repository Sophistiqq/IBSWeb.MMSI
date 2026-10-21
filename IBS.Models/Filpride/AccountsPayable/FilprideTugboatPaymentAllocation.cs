using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.Filpride.AccountsPayable
{
    public class FilprideTugboatPaymentAllocation
    {
        public int Id { get; set; }
        public int TugboatPaymentId { get; set; }
        public FilprideTugboatPayment Payment { get; set; } = null!;
        public int OtherTugChargeLineId { get; set; }
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? ReleasedAt { get; set; }
    }
}
