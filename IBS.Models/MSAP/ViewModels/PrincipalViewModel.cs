using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP.ViewModels
{
    public class PrincipalViewModel : Principal
    {
        public PrincipalViewModel() { }

        public PrincipalViewModel(Principal entity)
        {
            PrincipalId = entity.PrincipalId;
            PrincipalNumber = entity.PrincipalNumber;
            PrincipalName = entity.PrincipalName;
            Address = entity.Address;
            BusinessType = entity.BusinessType;
            Terms = entity.Terms;
            TIN = entity.TIN;
            IsActive = entity.IsActive;
            IsVatable = entity.IsVatable;
            CustomerId = entity.CustomerId;
            MsapRecId = entity.MsapRecId;
        }
    }
}
