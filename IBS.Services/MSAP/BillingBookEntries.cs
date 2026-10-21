using IBS.Models.MSAP;
using IBS.Models.MSAP.Books;
using IBS.Utility.MSAP.Constants;
using static IBS.Utility.MSAP.Constants.TaxConstants;

namespace IBS.Services.MSAP
{
    public static class BillingBookEntries
    {
        public static SalesBook Create(Billing bill, DateTime approvedAt, string actor, string actorId)
        {
            decimal amount = Math.Round(bill.Amount - bill.Discount, 2);
            if (amount < 0)
            {
                throw new InvalidOperationException("The approved billing amount cannot be negative.");
            }
            decimal net = bill.IsVatable ? Math.Round(amount / VatMultiplier, 2) : amount;
            bool exempt = !bill.IsVatable && bill.Customer.VatType == MsapConstants.VatType_Exempt;
            return new SalesBook
            {
                DocumentId = bill.MsapBillingId, IsUndocumented = bill.IsUndocumented, BillingRevision = bill.Revision, BillingDate = bill.Date,
                TransactionDate = DateOnly.FromDateTime(approvedAt), SerialNo = bill.MsapBillingNumber,
                SoldTo = bill.Principal?.PrincipalName ?? bill.Customer.CustomerName,
                TinNo = bill.Principal?.TIN ?? bill.Customer.CustomerTin,
                Address = bill.Principal?.Address1 ?? bill.Customer.CustomerAddress,
                Description = bill.Vessel.VesselName, Amount = amount, Discount = bill.Discount,
                VatableSales = bill.IsVatable ? net : 0m, VatAmount = bill.IsVatable ? amount - net : 0m,
                VatExemptSales = exempt ? net : 0m, ZeroRated = !bill.IsVatable && !exempt ? net : 0m,
                NetSales = net, DueDate = bill.DueDate, Company = bill.Company,
                CreatedBy = actor, CreatedDate = approvedAt, ApprovedByUserId = actorId
            };
        }
    }
}
