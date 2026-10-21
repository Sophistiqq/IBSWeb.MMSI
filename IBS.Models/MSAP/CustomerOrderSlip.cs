using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP
{
    public class CustomerOrderSlip : BaseEntity
    {
        [Key]
        public int CustomerOrderSlipId { get; set; }

        [Required]
        [Column(TypeName = "varchar(20)")]
        public string CustomerOrderSlipNumber { get; set; } = null!;

        [Required]
        [Display(Name = "COS Date")]
        public DateOnly Date { get; set; }

        [Required]
        [Column(TypeName = "varchar(20)")]
        public string Status { get; set; } = null!;

        [Column(TypeName = "varchar(20)")]
        [Display(Name = "COS Number")]
        public string? COSNumber { get; set; }

        [Column(TypeName = "varchar(100)")]
        [Display(Name = "Voyage Number")]
        public string? VoyageNumber { get; set; }

        public string? Remarks { get; set; }

        #region ---Foreign Keys---

        [Display(Name = "Customer")]
        public int CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public Customer Customer { get; set; } = null!;

        [Display(Name = "Vessel")]
        public int VesselId { get; set; }
        [ForeignKey(nameof(VesselId))]
        public Vessel Vessel { get; set; } = null!;

        [Display(Name = "Port")]
        public int PortId { get; set; }
        [ForeignKey(nameof(PortId))]
        public Port Port { get; set; } = null!;

        [Display(Name = "Terminal")]
        public int TerminalId { get; set; }
        [ForeignKey(nameof(TerminalId))]
        public Terminal Terminal { get; set; } = null!;

        #endregion

        [Display(Name = "Planned Start Time")]
        public DateTime? PlannedStartTime { get; set; }

        [Display(Name = "Planned End Time")]
        public DateTime? PlannedEndTime { get; set; }

        [Display(Name = "Required Tug Count")]
        public int RequiredTugCount { get; set; } = 1;

        [Display(Name = "Preferred Tugboat")]
        public int? PreferredTugboatId { get; set; }

        [ForeignKey(nameof(PreferredTugboatId))]
        public Tugboat? PreferredTugboat { get; set; }

        public virtual ICollection<DispatchTicket> DispatchTickets { get; set; } = new List<DispatchTicket>();

        [NotMapped]
        public bool IsMonthClosed { get; set; }
    }
}
