using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.Models.Filpride.ViewModels
{
    public class CommissionPaymentViewModel
    {
        public const string SourceFilprideDr = "FilprideDr";
        public const string SourceMsapBilling = "MsapBilling";

        public int CvId { get; set; }

        public string Source { get; set; } = SourceFilprideDr;

        public List<SelectListItem>? Suppliers { get; set; }

        [BindNever, ValidateNever]
        public List<SelectListItem>? Representatives { get; set; }

        [Display(Name = "Representative")]
        public int? RepresentativeId { get; set; }

        [BindNever, ValidateNever]
        public string? RepresentativeProfileError { get; set; }

        [Required]
        [StringLength(150)]
        public string Payee { get; set; } = null!;

        [Required]
        [Display(Name = "Supplier Address")]
        public string SupplierAddress { get; set; } = null!;

        [Required]
        [Display(Name = "Supplier Tin Number")]
        public string SupplierTinNo { get; set; } = null!;

        [Display(Name = "Supplier No")]
        public int? SupplierId { get; set; }

        [Required]
        [Display(Name = "Transaction Date")]
        public DateOnly TransactionDate { get; set; }

        public List<SelectListItem>? BankAccounts { get; set; }

        [Required]
        [Display(Name = "Bank Accounts")]
        public int? BankId { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Check #")]
        public string CheckNo { get; set; } = null!;

        [Required]
        [Display(Name = "Check Date")]
        public DateOnly CheckDate { get; set; }

        [StringLength(1000)]
        public string Particulars { get; set; } = null!;

        public List<SelectListItem>? COA { get; set; }

        [Required]
        public string[] AccountNumber { get; set; } = null!;

        [Required]
        public string[] AccountTitle { get; set; } = null!;

        [Required]
        public decimal[] Debit { get; set; } = null!;

        [Required]
        public decimal[] Credit { get; set; } = null!;

        public List<CheckVoucherTradeAccountingEntryViewModel> AdditionalAccountingEntries { get; set; } = [];

        public decimal DefaultPayableAmount { get; set; }

        public decimal CashInBankAmount { get; set; }

        //others
        public string? CreatedBy { get; set; }

        public List<DRDetailsViewModel> DRs { get; set; } = [];

        public List<MsapCommissionItemInput> MsapItems { get; set; } = [];

        public string? OldCVNo { get; set; }

        public string? Type { get; set; }

        public CheckVoucherDocumentationViewModel Documentation { get; set; } = new();

        public DateTime MinDate { get; set; }

        [Display(Name = "SI No")]
        public string? SiNo { get; set; }
    }

    public class MsapCommissionItemInput
    {
        public int BillingId { get; set; }

        public string Category { get; set; } = string.Empty;
    }
}
