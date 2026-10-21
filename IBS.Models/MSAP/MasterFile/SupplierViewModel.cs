namespace IBS.Models.MSAP.MasterFile
{
    public class SupplierViewModel : Supplier
    {
        public SupplierViewModel() { }

        public SupplierViewModel(Supplier entity)
        {
            SupplierId = entity.SupplierId;
            SupplierCode = entity.SupplierCode;
            SupplierName = entity.SupplierName;
            SupplierAddress = entity.SupplierAddress;
            SupplierTin = entity.SupplierTin;
            SupplierTerms = entity.SupplierTerms;
            VatType = entity.VatType;
            TaxType = entity.TaxType;
            ProofOfRegistrationFilePath = entity.ProofOfRegistrationFilePath;
            ProofOfRegistrationFileName = entity.ProofOfRegistrationFileName;
            ProofOfExemptionFilePath = entity.ProofOfExemptionFilePath;
            ProofOfExemptionFileName = entity.ProofOfExemptionFileName;
            IsActive = entity.IsActive;
            CreatedBy = entity.CreatedBy;
            CreatedDate = entity.CreatedDate;
            EditedBy = entity.EditedBy;
            EditedDate = entity.EditedDate;
            Category = entity.Category;
            TradeName = entity.TradeName;
            Branch = entity.Branch;
            WithholdingTaxPercent = entity.WithholdingTaxPercent;
            ReasonOfExemption = entity.ReasonOfExemption;
            Validity = entity.Validity;
            ValidityDate = entity.ValidityDate;
            Company = entity.Company;
            ZipCode = entity.ZipCode;
            RequiresPriceAdjustment = entity.RequiresPriceAdjustment;
        }
    }
}
