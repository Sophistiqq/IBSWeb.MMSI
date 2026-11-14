using IBS.Models.MSAP.Enums;

namespace IBS.Models.MSAP.ViewModels
{
    public class ScheduleRateSetupViewModel
    {
        public int ScheduleId { get; set; }
        public int? CustomerId { get; set; }
        public int PortId { get; set; }
        public int TerminalId { get; set; }
        public DateOnly PlannedDate { get; set; }
        public List<ScheduleRateSetupItemViewModel> Items { get; set; } = [];
        public bool HasActionableRates => Items.Any(item => item.NeedsSetup);
        public int PendingCount => Items.Count(item => item.TariffIsPending) + Items.Count(item => item.BafIsPending);
        public int AwaitingApprovalCount => Items.Count(item => item.PendingTariffUploadId.HasValue) + Items.Count(item => item.PendingBafUploadId.HasValue);
    }

    public class ScheduleRateSetupItemViewModel
    {
        public int ServiceId { get; set; }
        public string ServiceName { get; set; } = "";
        public int SourceEntryId { get; set; }
        public string TugboatNames { get; set; } = "";
        public string ChargingContext { get; set; } = "";

        public decimal? TariffTableRate { get; set; }
        public decimal? TariffManualAmount { get; set; }
        public bool TariffIsPending { get; set; }
        public bool TariffCanSetup { get; set; }
        public DateOnly TariffEffectiveDate { get; set; }
        public Guid? TariffPublishedUpdateId { get; set; }
        public Guid? PendingTariffUploadId { get; set; }
        public decimal? PendingTariffAmount { get; set; }

        public decimal? BafTableRate { get; set; }
        public decimal? BafManualAmount { get; set; }
        public bool BafIsPending { get; set; }
        public bool BafCanSetup { get; set; }
        public DateOnly BafEffectiveDate { get; set; }
        public Guid? BafPublishedUpdateId { get; set; }
        public Guid? PendingBafUploadId { get; set; }
        public decimal? PendingBafAmount { get; set; }

        public bool NeedsSetup => TariffIsPending || BafIsPending || TariffCanSetup || BafCanSetup;
    }
}
