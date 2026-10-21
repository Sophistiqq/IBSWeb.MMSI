using System.ComponentModel.DataAnnotations;
using IBS.Models.MSAP.Enums;

namespace IBS.Models.MSAP.ViewModels
{
    public class VesselScheduleServiceEntryViewModel
    {
        public int VesselScheduleServiceEntryId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Please select a tugboat")]
        public int TugboatId { get; set; }

        public string? TugboatName { get; set; }

        [Required(ErrorMessage = "Please select a service")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select a service")]
        public int ServiceId { get; set; }

        public string? ServiceName { get; set; }

        [EnumDataType(typeof(TariffChargeBasis))]
        public TariffChargeBasis? TariffBasis { get; set; } = TariffChargeBasis.PerHour;

        [EnumDataType(typeof(BafCalculationMethod))]
        public BafCalculationMethod? BafMethod { get; set; } = BafCalculationMethod.PerHour;

        [Range(typeof(decimal), "0", "99999999999999.9999", ErrorMessage = "Manual rate must be non-negative")]
        public decimal? ManualTariffRate { get; set; }

        [Range(typeof(decimal), "0", "99999999999999.9999", ErrorMessage = "Manual rate must be non-negative")]
        public decimal? ManualBafRate { get; set; }
    }
}
