using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP.ViewModels
{
    public class VesselViewModel : Vessel
    {
        public VesselViewModel() { }

        public VesselViewModel(Vessel entity)
        {
            VesselId = entity.VesselId;
            VesselNumber = entity.VesselNumber;
            VesselName = entity.VesselName;
            VesselType = entity.VesselType;
            MsapRecId = entity.MsapRecId;
        }
    }
}
