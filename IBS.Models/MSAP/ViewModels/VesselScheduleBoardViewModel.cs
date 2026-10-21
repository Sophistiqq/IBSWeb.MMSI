using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.Models.MSAP.ViewModels
{
    public class VesselScheduleBoardViewModel
    {
        public DateTime Date { get; set; }
        public DateTime? SelectedDate { get; set; }
        public int? PortId { get; set; }
        public int? TerminalId { get; set; }
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string LocationHeading { get; set; } = "All ports / All terminals";
        public List<SelectListItem> Ports { get; set; } = [];
        public List<SelectListItem> Terminals { get; set; } = [];
        public List<VesselSchedule> Schedules { get; set; } = [];
        public List<VesselSchedule> TugBookings { get; set; } = [];
        public Dictionary<DateTime, HashSet<int>> ConflictsByDay { get; set; } = [];
        public List<Tugboat> Tugboats { get; set; } = [];
    }
}
