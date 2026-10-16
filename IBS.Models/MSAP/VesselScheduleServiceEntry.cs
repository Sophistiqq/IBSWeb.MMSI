using System.ComponentModel.DataAnnotations;
using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP
{
    public class VesselScheduleServiceEntry
    {
        [Key]
        public int VesselScheduleServiceEntryId { get; set; }

        public int VesselScheduleId { get; set; }

        public VesselSchedule VesselSchedule { get; set; } = null!;

        public int TugboatId { get; set; }

        public Tugboat Tugboat { get; set; } = null!;

        public int ServiceId { get; set; }

        public Service Service { get; set; } = null!;
    }
}
