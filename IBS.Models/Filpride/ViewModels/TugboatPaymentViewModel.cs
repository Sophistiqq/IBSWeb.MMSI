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
        public int ChargeId { get; set; }
        [Required, MinLength(1, ErrorMessage = "Select at least one ticket for payment.")]
        public int[] AllocationIds { get; set; } = [];
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
        [StringLength(100)]
        public string? OldCVNo { get; set; }
        public int[] CosIds { get; set; } = [];
        [BindNever, ValidateNever]
        public List<SelectListItem> CustomerOrderSlips { get; set; } = [];
        public string? Payee => Owner?.TugboatOwnerName;
        public string? Address => Owner?.Address;
        public string? Tin => Owner?.Tin;
        [Display(Name = "Payment Amount")]
        public decimal DefaultPayableAmount => Net;
        public CheckVoucherDocumentationViewModel Documentation { get; set; } = new();
        [BindNever, ValidateNever]
        public List<SelectListItem> Owners { get; set; } = [];
        [BindNever, ValidateNever]
        public List<TugboatPaymentBillViewModel> Bills { get; set; } = [];
        [BindNever, ValidateNever]
        public List<SelectListItem> Banks { get; set; } = [];
        [BindNever, ValidateNever]
        public TugboatOwner? Owner { get; set; }
        [BindNever, ValidateNever]
        public string? OwnerProfileError { get; set; }
        [BindNever, ValidateNever]
        public OtherTugCharge? Charge { get; set; }
        [BindNever, ValidateNever]
        public decimal Gross { get; set; }
        [BindNever, ValidateNever]
        public decimal Withholding { get; set; }
        [BindNever, ValidateNever]
        public string? WithholdingAccountNumber { get; set; }
        public decimal Net => Gross - Withholding;
    }
}
