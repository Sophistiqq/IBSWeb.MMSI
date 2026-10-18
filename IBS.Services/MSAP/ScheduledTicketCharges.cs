using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public static class ScheduledTicketCharges
    {
        public static async Task<string?> ApplyAsync(IUnitOfWork unitOfWork, DispatchTicket ticket, VesselSchedule schedule, string username, CancellationToken ct)
        {
            var entry = schedule.ServiceEntries.Single(e => e.VesselScheduleServiceEntryId == ticket.VesselScheduleServiceEntryId);
            var (rate, bafRate) = await ResolveRatesAsync(unitOfWork, ticket, ct);
            var blocker = Apply(ticket, schedule, entry, rate, bafRate);
            ticket.TariffBy = username;
            ticket.TariffDate = DateTimeHelper.GetCurrentPhilippineTime();
            return blocker;
        }

        private static async Task<(TariffRate? Tariff, BafRate? Baf)> ResolveRatesAsync(IUnitOfWork unitOfWork, DispatchTicket ticket, CancellationToken ct)
        {
            var rate = ticket.DateLeft.HasValue
                ? await unitOfWork.TariffTable.GetApplicableRateAsync(ticket.CustomerId, ticket.TerminalId, ticket.ServiceId, ticket.DateLeft.Value, ct)
                : null;
            var bafRate = ticket.DateLeft.HasValue
                ? await unitOfWork.BafTable.GetApplicableRateAsync(ticket.CustomerId, ticket.TerminalId, ticket.ServiceId, ticket.DateLeft.Value, ct)
                : null;
            return (rate, bafRate);
        }

        public static async Task<(DispatchTicket Ticket, TariffRate? Tariff, BafRate? Baf)> PreviewAsync(IUnitOfWork work, DispatchTicket ticket, VesselSchedule schedule, CancellationToken ct)
        {
            var preview = new DispatchTicket
            {
                DispatchTicketId = ticket.DispatchTicketId, CustomerOrderSlipId = ticket.CustomerOrderSlipId,
                VesselScheduleServiceEntryId = ticket.VesselScheduleServiceEntryId,
                CustomerId = ticket.CustomerId, TerminalId = ticket.TerminalId, TugBoatId = ticket.TugBoatId, ServiceId = ticket.ServiceId,
                Date = ticket.Date, DateLeft = ticket.DateLeft, DateArrived = ticket.DateArrived,
                TimeLeft = ticket.TimeLeft, TimeArrived = ticket.TimeArrived
            };
            var (tariff, baf) = await ResolveRatesAsync(work, ticket, ct);
            var entry = schedule.ServiceEntries.Single(e => e.VesselScheduleServiceEntryId == ticket.VesselScheduleServiceEntryId);
            Apply(preview, schedule, entry, tariff, baf);
            return (preview, tariff, baf);
        }

        private static string ChargeSnapshot(DispatchTicket ticket)
        {
            static string Amount(decimal value)
            {
                return value.ToString("G29", CultureInfo.InvariantCulture);
            }
            return JsonSerializer.Serialize(new
            {
                DispatchRate = Amount(ticket.DispatchRate), BafRate = Amount(ticket.BAFRate), ticket.DispatchChargeType, ticket.BAFChargeType,
                DispatchDiscount = Amount(ticket.DispatchDiscount), BafDiscount = Amount(ticket.BAFDiscount),
                DispatchGross = Amount(ticket.DispatchBillingAmount), BafGross = Amount(ticket.BAFBillingAmount),
                DispatchNet = Amount(ticket.DispatchNetRevenue), BafNet = Amount(ticket.BAFNetRevenue),
                Gross = Amount(ticket.TotalBilling), Net = Amount(ticket.TotalNetRevenue), ticket.IsManualTariffRate, ticket.IsManualBafRate
            });
        }

        public static bool HasChanges(DispatchTicket ticket, DispatchTicket preview)
        {
            return ChargeSnapshot(ticket) != ChargeSnapshot(preview);
        }

        public static string ReviewFingerprint(DispatchTicket ticket, DispatchTicket preview, VesselSchedule schedule)
        {
            string snapshot = JsonSerializer.Serialize(new
            {
                ticket.DispatchTicketId, ticket.Status, ticket.EditedDate, ticket.DateLeft, ticket.DateArrived, ticket.TimeLeft, ticket.TimeArrived,
                schedule.Revision, schedule.ApprovedRevision, Stored = ChargeSnapshot(ticket), Proposed = ChargeSnapshot(preview)
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        }

        public static string? Apply(DispatchTicket ticket, VesselSchedule schedule, VesselScheduleServiceEntry entry, TariffRate? rate, BafRate? bafRate)
        {
            ticket.Status = MsapConstants.DispatchTicketStatus.CalculationPending;
            ticket.DispatchChargeType = null;
            ticket.BAFChargeType = null;
            ticket.DispatchDiscount = 0;
            ticket.BAFDiscount = 0;
            ticket.IsManualTariffRate = entry.ManualTariffRate.HasValue;
            ticket.IsManualBafRate = entry.ManualBafRate.HasValue;
            ticket.DispatchRate = 0;
            ticket.BAFRate = 0;
            ticket.DispatchBillingAmount = 0;
            ticket.BAFBillingAmount = 0;
            ticket.DispatchNetRevenue = 0;
            ticket.BAFNetRevenue = 0;
            ticket.TotalBilling = 0;
            ticket.TotalNetRevenue = 0;
            var blocker = GetBlocker(ticket, schedule, entry, rate, bafRate);
            if (blocker != null)
            {
                return blocker;
            }
            var duration = ticket.DateArrived!.Value.ToDateTime(ticket.TimeArrived!.Value)
                - ticket.DateLeft!.Value.ToDateTime(ticket.TimeLeft!.Value);
            ticket.RawDurationMinutes = (int)duration.TotalMinutes;
            ticket.RawDurationHours = (decimal)duration.TotalHours;
            ticket.TotalHours = Math.Round(Math.Max(ticket.RawDurationHours.Value, 1m), 2);
            var dispatchRate = entry.ManualTariffRate ?? rate!.Dispatch;
            var bafAmount = entry.ManualBafRate ?? bafRate!.Amount;
            var discount = schedule.SkipCosDiscount ? 0m : schedule.CosDiscountPercent.GetValueOrDefault();
            var hours = Math.Max(ticket.RawDurationHours.Value, 1m);
            ticket.DispatchChargeType = entry.TariffBasis == TariffChargeBasis.PerHour ? "Per hour" : "Per move";
            ticket.BAFChargeType = entry.BafMethod switch
            {
                BafCalculationMethod.PerHour => "Per hour",
                BafCalculationMethod.PerMove => "Per move",
                BafCalculationMethod.Lumpsum => "Lumpsum",
                BafCalculationMethod.Industry => "Industry",
                _ => throw new InvalidOperationException("Invalid BAF method.")
            };
            ticket.DispatchRate = dispatchRate;
            ticket.BAFRate = bafAmount;
            ticket.IsManualTariffRate = entry.ManualTariffRate.HasValue;
            ticket.IsManualBafRate = entry.ManualBafRate.HasValue;
            ticket.DispatchDiscount = discount;
            ticket.BAFDiscount = discount;
            var dispatchGross = dispatchRate * (entry.TariffBasis == TariffChargeBasis.PerHour ? hours : 1m);
            var bafGross = bafAmount * (entry.BafMethod == BafCalculationMethod.PerHour ? hours : 1m);
            ticket.DispatchBillingAmount = Math.Round(dispatchGross, 2);
            ticket.BAFBillingAmount = Math.Round(bafGross, 2);
            ticket.DispatchNetRevenue = Math.Round(dispatchGross * (1m - discount / 100m), 2);
            ticket.BAFNetRevenue = Math.Round(bafGross * (1m - discount / 100m), 2);
            ticket.TotalBilling = Math.Round(dispatchGross + bafGross, 2);
            ticket.TotalNetRevenue = Math.Round((dispatchGross + bafGross) * (1m - discount / 100m), 2);
            ticket.Status = MsapConstants.DispatchTicketStatus.ForBilling;
            return null;
        }

        public static DateOnly BafCycleStart(DateOnly serviceDate)
        {
            int daysSinceTuesday = ((int)serviceDate.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
            return serviceDate.AddDays(-daysSinceTuesday);
        }

        public static string? GetBlocker(DispatchTicket ticket, VesselSchedule schedule, VesselScheduleServiceEntry entry, TariffRate? rate, BafRate? bafRate)
        {
            if (entry.CancelledAt.HasValue)
            {
                return "This planned service was cancelled and cannot receive charges.";
            }
            if (entry.VesselScheduleServiceEntryId != ticket.VesselScheduleServiceEntryId
                || entry.TugboatId != ticket.TugBoatId || entry.ServiceId != ticket.ServiceId)
            {
                return "The ticket does not match its approved service requirement.";
            }
            if (schedule.ApprovedRevision != schedule.Revision || schedule.Status == MsapConstants.VesselScheduleStatus.Tentative)
            {
                return "The current COS revision requires approval.";
            }
            if (!ticket.DateLeft.HasValue || !ticket.TimeLeft.HasValue || !ticket.DateArrived.HasValue || !ticket.TimeArrived.HasValue)
            {
                return "Complete the actual start and end dates/times.";
            }
            var start = ticket.DateLeft.Value.ToDateTime(ticket.TimeLeft.Value);
            var end = ticket.DateArrived.Value.ToDateTime(ticket.TimeArrived.Value);
            if (end <= start || end > DateTimeHelper.GetCurrentPhilippineTime())
            {
                return "Actual end must follow start and cannot be in the future.";
            }
            if (!entry.TariffBasis.HasValue || !Enum.IsDefined(entry.TariffBasis.Value))
            {
                return "The approved service has no valid tariff charging basis.";
            }
            if (!entry.BafMethod.HasValue || !Enum.IsDefined(entry.BafMethod.Value))
            {
                return "The approved service has no valid BAF method.";
            }
            var dispatchRate = entry.ManualTariffRate ?? rate?.Dispatch;
            var bafAmount = entry.ManualBafRate ?? bafRate?.Amount;
            if (!dispatchRate.HasValue || dispatchRate < 0)
            {
                return $"No valid tariff rate applies on {ticket.DateLeft:dd MMM yyyy}. Check rate maintenance or the approved manual tariff.";
            }
            if (!bafAmount.HasValue || bafAmount < 0)
            {
                return $"No valid BAF rate applies on {ticket.DateLeft:dd MMM yyyy}. Check rate maintenance or the approved manual BAF.";
            }
            if (!entry.ManualBafRate.HasValue && (bafRate == null || !bafRate.ApprovedAt.HasValue
                || bafRate.AsOfDate < BafCycleStart(ticket.DateLeft.Value)))
            {
                return $"BAF coverage is awaiting approval for the week beginning {BafCycleStart(ticket.DateLeft.Value):dd MMM yyyy}. The actual ticket is retained; approve the applicable BAF and recalculate charges before billing.";
            }
            var discount = schedule.SkipCosDiscount ? 0m : schedule.CosDiscountPercent.GetValueOrDefault();
            return discount is < 0 or > 100 ? "The approved COS discount must be between 0% and 100%." : null;
        }
    }
}
