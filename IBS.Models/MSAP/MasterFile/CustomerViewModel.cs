namespace IBS.Models.MSAP.MasterFile
{
    public class CustomerViewModel : Customer
    {
        public CustomerViewModel() { }

        public CustomerViewModel(Customer entity)
        {
            CustomerId = entity.CustomerId;
            CustomerCode = entity.CustomerCode;
            CustomerName = entity.CustomerName;
            CustomerAddress = entity.CustomerAddress;
            Address1 = entity.Address1;
            Address2 = entity.Address2;
            Address3 = entity.Address3;
            CustomerTin = entity.CustomerTin;
            BusinessStyle = entity.BusinessStyle;
            CustomerTerms = entity.CustomerTerms;
            CustomerType = entity.CustomerType;
            VatType = entity.VatType;
            WithHoldingVat = entity.WithHoldingVat;
            WithHoldingTax = entity.WithHoldingTax;
            IsActive = entity.IsActive;
            CreatedBy = entity.CreatedBy;
            CreatedDate = entity.CreatedDate;
            EditedBy = entity.EditedBy;
            EditedDate = entity.EditedDate;
            Company = entity.Company;
            StationCode = entity.StationCode;
            CreditLimit = entity.CreditLimit;
            CreditLimitAsOfToday = entity.CreditLimitAsOfToday;
            ZipCode = entity.ZipCode;
            RetentionRate = entity.RetentionRate;
            HasMultipleTerms = entity.HasMultipleTerms;
            Type = entity.Type;
            RequiresPriceAdjustment = entity.RequiresPriceAdjustment;
            CommissioneeId = entity.CommissioneeId;
            CommissionRate = entity.CommissionRate;
            DefaultCosDiscountPercent = entity.DefaultCosDiscountPercent;
            MsapRecId = entity.MsapRecId;
        }
    }
}
