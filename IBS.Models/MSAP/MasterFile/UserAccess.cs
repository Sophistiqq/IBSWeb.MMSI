using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.Models.MSAP.MasterFile
{
    public class UserAccess
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = null!;

        [Column(TypeName = "varchar(100)")]
        public string? UserName { get; set; }

        [NotMapped]
        public string DisplayName { get; set; } = "Name unavailable";

        #region -- MSAP Workflow --

        public bool CanCreateDispatchTicket { get; set; }

        public bool CanEditDispatchTicket { get; set; }

        [Column("can_cancel_dispatch_ticket")]
        public bool CanDeleteDispatchTicket { get; set; }

        public bool CanSetTariff { get; set; }

        public bool CanApproveTariff { get; set; }

        public bool CanCreateBilling { get; set; }

        public bool CanEditBilling { get; set; }

        public bool CanDeleteBilling { get; set; }

        public bool CanReverseBilling { get; set; }

        public bool CanCreateCollection { get; set; }

        public bool CanCreateCustomerOrderSlip { get; set; }

        public bool CanEditCustomerOrderSlip { get; set; }

        public bool CanDeleteCustomerOrderSlip { get; set; }

        public bool CanCloseCustomerOrderSlip { get; set; }

        public bool CanApproveSchedule { get; set; }

        public bool CanViewRates { get; set; }

        public bool CanManageRates { get; set; }

        public bool CanApproveRateUpload { get; set; }

        public bool CanManageOtherTugCharges { get; set; }

        public bool CanConfirmOtherTugCharges { get; set; }

        public bool CanApproveFinalBilling { get; set; }

        #endregion -- MSAP Workflow --

        #region -- Treasury --

        public bool CanAccessTreasury { get; set; }

        public bool CanCreateDisbursement { get; set; }

        #endregion -- Treasury --

        #region -- MSAP Import --

        public bool CanManageMsapImport { get; set; }

        #endregion -- MSAP Import --

        #region -- Master Files --

        public bool CanManageMaritimeMasterFile { get; set; }

        #endregion -- Master Files --

        #region -- Reports --


        public bool CanViewInventoryReport { get; set; }

        public bool CanViewMaritimeReport { get; set; }

        #endregion -- Reports --

        [NotMapped]
        public List<SelectListItem>? Users { get; set; }
    }
}
