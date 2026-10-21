using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP.ViewModels
{
    public class TugboatOwnerViewModel : TugboatOwner
    {
        public TugboatOwnerViewModel() { }

        public TugboatOwnerViewModel(TugboatOwner entity)
        {
            TugboatOwnerId = entity.TugboatOwnerId;
            TugboatOwnerNumber = entity.TugboatOwnerNumber;
            TugboatOwnerName = entity.TugboatOwnerName;
            FixedRate = entity.FixedRate;
            Address = entity.Address;
            Tin = entity.Tin;
            VatType = entity.VatType;
            TaxType = entity.TaxType;
            WithholdingTaxPercent = entity.WithholdingTaxPercent;
            MsapRecId = entity.MsapRecId;
        }
    }
}
