using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP
{
    public class VesselScheduleServiceEntry
    {
        [Key]
        public int VesselScheduleServiceEntryId { get; set; }

        public int VesselScheduleId { get; set; }

        public VesselSchedule VesselSchedule { get; set; } = null!;

        public int TugboatId { get; set; }

        public Tugboat Tugboat { get; set; } = null!;

        public int ServiceId { get; set; }

        public Service Service { get; set; } = null!;

        [EnumDataType(typeof(TariffChargeBasis))]
        public TariffChargeBasis? TariffBasis { get; set; }

        [EnumDataType(typeof(BafCalculationMethod))]
        public BafCalculationMethod? BafMethod { get; set; }

        [Range(typeof(decimal), "0", "99999999999999.9999")]
        [Column(TypeName = "numeric(18,4)")]
        public decimal? ManualTariffRate { get; set; }

        [Range(typeof(decimal), "0", "99999999999999.9999")]
        [Column(TypeName = "numeric(18,4)")]
        public decimal? ManualBafRate { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime? CancelledAt { get; set; }

        [MaxLength(450)]
        public string? CancelledByUserId { get; set; }

        [MaxLength(500)]
        public string? CancellationReason { get; set; }

        public List<DispatchTicket> DispatchTickets { get; set; } = [];
    }
}
