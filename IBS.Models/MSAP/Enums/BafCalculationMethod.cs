using System.ComponentModel.DataAnnotations;

namespace IBS.Models.MSAP.Enums
{
    public enum BafCalculationMethod
    {
        Lumpsum = 1,
        Industry = 2,
        [Display(Name = "Per hour")]
        PerHour = 3,
        [Display(Name = "Per Move")]
        PerMove = 4
    }
}
