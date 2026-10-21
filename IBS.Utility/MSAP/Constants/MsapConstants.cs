namespace IBS.Utility.MSAP.Constants
{
    public static class MsapConstants
    {
        public const string Company_MMSI = "MMSI";

        // shortcut: MSAP month locks are suspended until the late-ticket closing policy is agreed.
        public static bool EnforceClosedPeriods => false;


        #region Vat Type

        public const string VatType_Vatable = "Vatable";
        public const string VatType_ZeroRated = "Zero-Rated";
        public const string VatType_Exempt = "Exempt";

        #endregion Vat Type

        #region Tax Type

        public const string TaxType_WithTax = "Withholding Tax";
        public const string TaxType_WithVat = "Withholding Vat";
        public const string TaxType_Exempt = "Exempt";

        #endregion Tax Type

        #region Filpride Department

        public const string Department_Accounting = "Accounting";
        public const string Department_CreditAndCollection = "Credit and Collection";
        public const string Department_Engineering = "Engineering";
        public const string Department_Finance = "Finance";
        public const string Department_HRAndAdminOrLegal = "HR and Admin/Legal";
        public const string Department_Logistics = "Logistics";
        public const string Department_Marketing = "Marketing";
        public const string Department_ManagementAccounting = "Management Accounting";
        public const string Department_MIS = "MIS";
        public const string Department_Operation = "Operation";
        public const string Department_RCD = "RCD";
        public const string Department_RetailAdmin = "Retail Admin";
        public const string Department_RetailAudit = "Retail Audit";
        public const string Department_SiteDevAndSiteAcquisition = "Site Dev and Acquisition";
        public const string Department_StationCashier = "Station Cashier";
        public const string Department_TradeAndSupply = "Trade and Supply";
        public const string Department_TrainingAndCompliance = "Training and Compliance";

        #endregion Filpride Department


        #region Format string

        public const string Date_Format = "MMM dd, yyyy";

        #endregion Format string

        public static class CustomerOrderSlipStatus
        {
            public const string Open = "Open";
            public const string Closed = "Closed";
            public const string Cancelled = "Cancelled";
            public const string Invalidated = "Invalidated";

            public static string GetDisplayName(string status)
            {
                return status == Closed ? "Fully Billed" : status;
            }
        }

        public static class DispatchTicketStatus
        {
            public const string CalculationPending = "Calculation pending";
            public const string ForTariff = "For Tariff";
            public const string ForApproval = "For Approval";
            public const string Disapproved = "Disapproved";
            public const string ForBilling = "For Billing";
            public const string Billed = "Billed";
            public const string Deleted = "Deleted";

            public static readonly string[] All = [CalculationPending, ForTariff, ForApproval, Disapproved, ForBilling, Billed, Deleted];
        }

        public static class BillingStatus
        {
            public const string ForPosting = "For Posting";
            public const string ForCollection = "For Collection";
            public const string Collected = "Collected";
        }

        public static class VesselScheduleStatus
        {
            public const string Tentative = "Tentative";
            public const string Confirmed = "Confirmed";
            public const string InProgress = "In Progress";
            public const string Completed = "Completed";

            public static string GetDisplayName(string status)
            {
                return status == Completed ? "Service Completed" : status;
            }
            public const string Cancelled = "Cancelled";
        }

        public const string BilledToLocal = "LOCAL";
        public const string BilledToForeign = "FOREIGN";

        #region MSAP Accounting Accounts

        #endregion

    }
}
