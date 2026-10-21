using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP.ViewModels
{
    public class PortViewModel : Port
    {
        public PortViewModel() { }

        public PortViewModel(Port entity)
        {
            PortId = entity.PortId;
            PortNumber = entity.PortNumber;
            PortName = entity.PortName;
            MsapRecId = entity.MsapRecId;
        }
    }
}
