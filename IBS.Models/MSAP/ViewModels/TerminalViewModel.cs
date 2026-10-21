using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP.ViewModels
{
    public class TerminalViewModel : Terminal
    {
        public TerminalViewModel() { }

        public TerminalViewModel(Terminal entity)
        {
            TerminalId = entity.TerminalId;
            TerminalNumber = entity.TerminalNumber;
            TerminalName = entity.TerminalName;
            IsIoc = entity.IsIoc;
            PortId = entity.PortId;
            MsapRecId = entity.MsapRecId;
        }
    }
}
