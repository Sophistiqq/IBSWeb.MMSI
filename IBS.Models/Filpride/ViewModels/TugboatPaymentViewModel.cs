using System.ComponentModel.DataAnnotations;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IBS.Models.Filpride.ViewModels
{
    public class TugboatPaymentViewModel
    {
        [Range(1, int.MaxValue)]
        [Display(Name = "Tugboat owner")]
        public int OwnerId { get; set; }
        [Range(1, int.MaxValue)]
        [Display(Name = "Confirmed provider bill")]
        public int ChargeId { get; set; }
        [Range(1, int.MaxValue)]
        [Display(Name = "Posted AP journal")]
        public int LiabilityJournalId { get; set; }
        [Required]
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        [Required, StringLength(13)]
        public string Type { get; set; } = "Documented";
        [Range(1, int.MaxValue)]
        [Display(Name = "Bank account")]
        public int BankId { get; set; }
        [Required, StringLength(50)]
        [Display(Name = "Check number")]
        public string CheckNo { get; set; } = "";
        [Required]
        [Display(Name = "Check date")]
        public DateOnly CheckDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        [Required, StringLength(1000)]
        public string Particulars { get; set; } = "";
        public CheckVoucherDocumentationViewModel Documentation { get; set; } = new();
        [BindNever, ValidateNever]
        public List<SelectListItem> Owners { get; set; } = [];
        [BindNever, ValidateNever]
        public List<SelectListItem> Charges { get; set; } = [];
        [BindNever, ValidateNever]
        public List<SelectListItem> Journals { get; set; } = [];
        [BindNever, ValidateNever]
        public List<SelectListItem> Banks { get; set; } = [];
        [BindNever, ValidateNever]
        public TugboatOwner? Owner { get; set; }
        [BindNever, ValidateNever]
        public OtherTugCharge? Charge { get; set; }
        [BindNever, ValidateNever]
        public decimal Gross { get; set; }
        [BindNever, ValidateNever]
        public decimal Withholding { get; set; }
        public decimal Net => Gross - Withholding;
    }
}
