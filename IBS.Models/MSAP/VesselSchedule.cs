using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.ModelBinding;


namespace IBS.Models.MSAP
{
    public class VesselSchedule
    {
        [Key]
        public int VesselScheduleId { get; set; }

        [Display(Name = "Customer")]
        public int? CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        [Required]
        public int VesselId { get; set; }

        [ForeignKey(nameof(VesselId))]
        public Vessel Vessel { get; set; } = null!;

        [Required]
        public int PortId { get; set; }

        [ForeignKey(nameof(PortId))]
        public Port Port { get; set; } = null!;

        [Required]
        public int TerminalId { get; set; }

        [ForeignKey(nameof(TerminalId))]
        public Terminal Terminal { get; set; } = null!;

        [Display(Name = "Planned Start")]
        [Column(TypeName = "timestamp without time zone")]
        public DateTime PlannedStart { get; set; }

        [Display(Name = "Planned End")]
        [Column(TypeName = "timestamp without time zone")]
        public DateTime PlannedEnd { get; set; }

        [Display(Name = "Required Tug Count")]
        public int RequiredTugCount { get; set; } = 1;

        [Display(Name = "Assigned Tugboats")]
        [Column(TypeName = "text")]
        public string? AssignedTugboatIds { get; set; }

        [Display(Name = "Voyage Number")]
        [Column(TypeName = "varchar(50)")]
        public string? VoyageNumber { get; set; }

        [Display(Name = "Vessel Type")]
        [Column(TypeName = "varchar(20)")]
        public string? VesselType { get; set; }

        [Display(Name = "Status")]
        [Column(TypeName = "varchar(20)")]
        public string Status { get; set; } = "Tentative";

        [Column(TypeName = "text")]
        public string? Notes { get; set; }

        public int? CustomerOrderSlipId { get; set; }

        [ForeignKey(nameof(CustomerOrderSlipId))]
        public CustomerOrderSlip? CustomerOrderSlip { get; set; }

        [Column(TypeName = "varchar(100)")]
        public string? CreatedBy { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime CreatedDate { get; set; }

        [Column(TypeName = "varchar(50)")]
        public string? EditedBy { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime? EditedDate { get; set; }

        public List<VesselScheduleServiceEntry> ServiceEntries { get; set; } = [];

        [Range(typeof(decimal), "0", "100")]
        [Column(TypeName = "numeric(18,4)")]
        public decimal? CosDiscountPercent { get; set; }

        public bool SkipCosDiscount { get; set; }

        public bool SbmaEnabled { get; set; }

        [BindNever, JsonIgnore]
        public int Revision { get; set; } = 1;

        [BindNever, JsonIgnore]
        public int? ApprovedRevision { get; set; }

        [BindNever, JsonIgnore]
        public string? ApprovedByUserId { get; set; }

        [BindNever, JsonIgnore]
        public ApplicationUser? ApprovedByUser { get; set; }

        [BindNever, JsonIgnore]
        [Column(TypeName = "timestamp without time zone")]
        public DateTime? ApprovedAt { get; set; }
    }
}
