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
            ZipCode = entity.ZipCode;
            Type = entity.Type;
            DefaultCosDiscountPercent = entity.DefaultCosDiscountPercent;
            MsapRecId = entity.MsapRecId;
        }
    }
}
