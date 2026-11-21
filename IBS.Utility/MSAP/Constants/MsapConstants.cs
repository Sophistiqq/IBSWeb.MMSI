using System.Globalization;

namespace IBS.Utility.MSAP.Constants
{
    public static class MsapConstants
    {
        public const string Company_MMSI = "MMSI";
        public const int WateringServiceId = 14;

        public static string FormatServiceDisplay(int serviceId, string serviceName, decimal? tons)
        {
            return serviceId == WateringServiceId && tons is > 0m
                ? $"{serviceName} - {tons.Value.ToString("0.####", CultureInfo.InvariantCulture)} TONS"
                : serviceName;
        }

        public static decimal GetEffectiveUnitCount(int serviceId, decimal? tons, decimal totalHours)
        {
            return serviceId == WateringServiceId && tons is > 0m ? tons.Value : totalHours;
        }

        // MSAP month closing policy: closing stamps finalized records and blocks closed-period backdating while allowing open-month downstream progression.
        public static bool EnforceClosedPeriods => true;


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
            public const string AwaitingRate = "Awaiting Rate";
            public const string ForTariff = "For Tariff";
            public const string ForApproval = "For Approval";
            public const string Disapproved = "Disapproved";
            public const string ForBilling = "For Billing";
            public const string Billed = "Billed";
            public const string Deleted = "Deleted";

            public static readonly string[] All = [AwaitingRate, ForTariff, ForApproval, Disapproved, ForBilling, Billed, Deleted];
        }

        public static class BillingStatus
        {
            public const string ForPosting = "For Posting";
            public const string Reviewed = "Reviewed";
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

        public const string CommissionPayableAccount = "201010200";
        // TODO(IBS-122): Confirm final GL expense/contra-revenue account number for MSAP Agency Incentive & Agent Commission with Ma'am Kim.
        public const string CommissionExpenseAccount = "503010500";
        public const string InputVatAccount = "101060200";

        public static class RepresentativeCategory
        {
            public const string AgencyIncentive = "AgencyIncentive";
            public const string AgentCommission = "AgentCommission";
        }

        #endregion

    }
}
