using System.ComponentModel.DataAnnotations;

namespace IBS.Models.MSAP.Enums
{
    public enum TariffChargeBasis
    {
        [Display(Name = "Per hour")]
        PerHour = 1,
        [Display(Name = "Per Move")]
        PerMove = 2
    }
}
