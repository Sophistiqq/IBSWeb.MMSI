using System.ComponentModel.DataAnnotations.Schema;

namespace IBS.Models.MSAP
{
    public class OtherTugChargeLine
    {
        public int OtherTugChargeLineId { get; set; }
        public int OtherTugChargeId { get; set; }
        public OtherTugCharge Charge { get; set; } = null!;
        public int DispatchTicketId { get; set; }
        public DispatchTicket Ticket { get; set; } = null!;
        [Column(TypeName = "numeric(18,2)")]
        public decimal Amount { get; set; }
    }
}
