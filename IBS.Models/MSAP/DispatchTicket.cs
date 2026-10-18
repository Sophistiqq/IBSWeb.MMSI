using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IBS.Models.MSAP.MasterFile;

namespace IBS.Models.MSAP
{
    public class DispatchTicket
    {
        [Key]
        [Column("RECID")]
        public int DispatchTicketId { get; set; }

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid AccountingSourceId { get; set; } = Guid.NewGuid();

        [Column("DATE")]
        [Display(Name = "Date")]
        public DateOnly Date { get; set; }

        [Column("NUMBER", TypeName = "varchar(20)")]
        public string DispatchNumber
        {
            get => _dispatchNumber;
            set => _dispatchNumber = value.Trim();
        }

        private string _dispatchNumber = null!;

        [Column(TypeName = "varchar(20)")]
        public string? COSNumber
        {
            get => _cosNumber;
            set => _cosNumber = value?.Trim();
        }

        private string? _cosNumber;

        [Display(Name = "Date Left")]
        public DateOnly? DateLeft { get; set; }

        [Display(Name = "Date Arrived")]
        public DateOnly? DateArrived { get; set; }

        [Display(Name = "Time Left")]
        public TimeOnly? TimeLeft { get; set; }

        [Display(Name = "Time Arrived")]
        public TimeOnly? TimeArrived { get; set; }

        [Column(TypeName = "varchar(100)")]
        public string? Remarks
        {
            get => _remarks;
            set => _remarks = value?.Trim();
        }

        private string? _remarks;

        [Column(TypeName = "varchar(100)")]
        public string? BaseOrStation { get; set; }

        [Column(TypeName = "varchar(100)")]
        public string? VoyageNumber
        {
            get => _voyageNumber;
            set => _voyageNumber = value?.Trim();
        }

        private string? _voyageNumber;

        public string? DispatchChargeType { get; set; }

        public string? BAFChargeType { get; set; }

        public decimal TotalHours { get; set; }

        [Display(Name = "Raw Duration (Hours)")]
        [Column(TypeName = "numeric(18,4)")]
        public decimal? RawDurationHours { get; set; }

        [Display(Name = "Raw Duration (Minutes)")]
        public int? RawDurationMinutes { get; set; }

        public string Status { get; set; } = null!;

        public string CreatedBy { get; set; } = null!;

        [Column(TypeName = "timestamp without time zone")]
        public DateTime CreatedDate { get; set; }

        public string? EditedBy { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime? EditedDate { get; set; }

        #region == Tariff ==

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]
        public decimal DispatchRate { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]

        public decimal DispatchBillingAmount { get; set; }

        public decimal DispatchDiscount { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]

        public decimal DispatchNetRevenue { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]

        public decimal BAFRate { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]
        public decimal BAFBillingAmount { get; set; }

        public decimal BAFDiscount { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]
        public decimal BAFNetRevenue { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalBilling { get; set; }

        [DisplayFormat(DataFormatString = "{0:#,##0.00;(#,##0.00)}", ApplyFormatInEditMode = false)]
        [Column(TypeName = "numeric(18,2)")]
        public decimal TotalNetRevenue { get; set; }

        public OtherTugChargeLine? OtherTugChargeLine { get; set; }

        [NotMapped]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool OtherTugCostPending => !Tugboat.IsCompanyOwned && ApOtherTugs == 0
            && OtherTugChargeLine?.Charge.ConfirmedAt == null;

        public decimal ApOtherTugs { get; set; }
        public bool ApOtherTugsIncludesVat { get; set; } = true;

        public bool IsManualTariffRate { get; set; }

        public bool IsManualBafRate { get; set; }

        public string? TariffBy { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime TariffDate { get; set; }

        public string? TariffEditedBy { get; set; }

        [Column(TypeName = "timestamp without time zone")]
        public DateTime? TariffEditedDate { get; set; }

        #endregion == Tariff ==

        public string? ImageName { get; set; }

        public string? ImageSavedUrl { get; set; }

        public string? ImageSignedUrl { get; set; }

        public string? VideoName { get; set; }

        public string? VideoSavedUrl { get; set; }

        public string? VideoSignedUrl { get; set; }

        public int? CustomerOrderSlipId { get; set; }
        [ForeignKey(nameof(CustomerOrderSlipId))]
        public CustomerOrderSlip? CustomerOrderSlip { get; set; }

        [Column("BILLNUM")]
        public int? BillingId { get; set; }
        [ForeignKey(nameof(BillingId))]
        public Billing? Billing { get; set; }

        [Display(Name = "Expected Service Entry")]
        public int? VesselScheduleServiceEntryId { get; set; }
        [ForeignKey(nameof(VesselScheduleServiceEntryId))]
        public VesselScheduleServiceEntry? ExpectedServiceEntry { get; set; }

        #region ---Columns with Table relations---

        [Column("CUSTNO")]
        public int CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public Customer Customer { get; set; } = null!;

        [Column("TUGNUM")]
        public int TugBoatId { get; set; }
        [ForeignKey(nameof(TugBoatId))]
        public Tugboat Tugboat { get; set; } = null!; //carries the columns of one record

        [Column("MASTERNO")]
        public int? TugMasterId { get; set; }
        [ForeignKey(nameof(TugMasterId))]
        public TugMaster? TugMaster { get; set; } //carries the columns of one record

        [Column("VESSELNUM")]
        public int VesselId { get; set; }
        [ForeignKey(nameof(VesselId))]
        public Vessel Vessel { get; set; } = null!; //carries the columns of one record

        [Column("PORTNUM")]
        public int PortId { get; set; }
        [ForeignKey(nameof(PortId))]
        public Port Port { get; set; } = null!;

        [Column("TERMINAL")]
        public int TerminalId { get; set; }
        [ForeignKey(nameof(TerminalId))]
        public Terminal Terminal { get; set; } = null!; //carries the columns of one record

        public int ServiceId { get; set; }
        [ForeignKey(nameof(ServiceId))]
        public Service Service { get; set; } = null!; //carries the columns of one record

        #endregion ---Columns with Table relations---

        [NotMapped]
        public bool IsMonthClosed { get; set; }
    }
}
