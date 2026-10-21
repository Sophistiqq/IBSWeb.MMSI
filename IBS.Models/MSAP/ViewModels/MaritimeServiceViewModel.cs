using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP.ViewModels
{
    public class MaritimeServiceViewModel : Service
    {
        public MaritimeServiceViewModel() { }

        public MaritimeServiceViewModel(Service entity)
        {
            ServiceId = entity.ServiceId;
            ServiceNumber = entity.ServiceNumber;
            ServiceName = entity.ServiceName;
            MsapRecId = entity.MsapRecId;
        }
    }
}
