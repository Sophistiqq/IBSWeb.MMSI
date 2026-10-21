namespace IBS.Models.MSAP.Enums
{
    public enum ProcedureEnum
    {
        #region -- MSAP Workflow --

        CreateDispatchTicket,
        EditDispatchTicket,
        DeleteDispatchTicket,
        SetTariff,
        ApproveTariff,
        CreateBilling,
        EditBilling,
        DeleteBilling,
        ReverseBilling,
        CreateCollection,
        CreateCustomerOrderSlip,
        EditCustomerOrderSlip,
        DeleteCustomerOrderSlip,
        CloseCustomerOrderSlip,

        #endregion -- MSAP Workflow --

        #region -- Treasury --

        AccessTreasury,
        CreateDisbursement,

        #endregion -- Treasury --

        #region -- Posting Period --

        ManagePostedPeriod,

        #endregion -- Posting Period --

        #region -- MSAP Import --

        ManageMsapImport,

        #endregion -- MSAP Import --

        #region -- Master Files --

        ManageMaritimeMasterFile,

        #endregion -- Master Files --

        #region -- Reports --

        ViewInventoryReport = 20,
        ViewMaritimeReport,

        #endregion -- Reports --

        #region -- Individual Workflow Approvals --

        ApproveSchedule = 22,
        ApproveRateUpload = 23,
        ApproveFinalBilling = 24,

        #endregion -- Individual Workflow Approvals --

        ViewRates = 25,
        ManageRates = 26,
        ManageOtherTugCharges = 27,
        ConfirmOtherTugCharges = 28,

        // Derived read access; these do not introduce stored permission flags.
        ViewOperations = 29,
        ViewBillings = 30,
        ViewCollections = 31,
        AccessMsap = 32,
    }
}
