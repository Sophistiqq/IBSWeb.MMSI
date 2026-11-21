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
        private static bool IsWatering(DispatchTicket ticket, VesselScheduleServiceEntry? entry = null)
        {
            var serviceNumber = ticket.Service?.ServiceNumber
                ?? entry?.Service?.ServiceNumber
                ?? ticket.ExpectedServiceEntry?.Service?.ServiceNumber;
            return MsapConstants.IsWateringService(serviceNumber);
        }

        public static async Task<string?> ApplyAsync(IUnitOfWork unitOfWork, DispatchTicket ticket, VesselSchedule schedule, string username, CancellationToken ct)
        {
            var entry = schedule.ServiceEntries.Single(e => e.VesselScheduleServiceEntryId == ticket.VesselScheduleServiceEntryId);
            if (ticket.Service == null && entry.Service == null && ticket.ExpectedServiceEntry?.Service == null)
            {
                var service = await unitOfWork.Service.GetAsync(s => s.ServiceId == ticket.ServiceId, ct);
                if (service != null)
                {
                    ticket.Service = service;
                }
            }
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
            var entry = schedule.ServiceEntries.Single(e => e.VesselScheduleServiceEntryId == ticket.VesselScheduleServiceEntryId);
            var preview = new DispatchTicket
            {
                DispatchTicketId = ticket.DispatchTicketId, CustomerOrderSlipId = ticket.CustomerOrderSlipId,
                VesselScheduleServiceEntryId = ticket.VesselScheduleServiceEntryId,
                CustomerId = ticket.CustomerId, TerminalId = ticket.TerminalId, TugBoatId = ticket.TugBoatId, ServiceId = ticket.ServiceId,
                Date = ticket.Date, DateLeft = ticket.DateLeft, DateArrived = ticket.DateArrived,
                TimeLeft = ticket.TimeLeft, TimeArrived = ticket.TimeArrived,
                Tons = ticket.Tons, DispatchRate = ticket.DispatchRate
            };
            var service = ticket.Service ?? entry.Service ?? ticket.ExpectedServiceEntry?.Service
                ?? await work.Service.GetAsync(s => s.ServiceId == ticket.ServiceId, ct);
            if (service != null)
            {
                preview.Service = service;
            }
            var (tariff, baf) = await ResolveRatesAsync(work, ticket, ct);
            Apply(preview, schedule, entry, tariff, baf);
            return (preview, tariff, baf);
        }

        private static string ChargeSnapshot(DispatchTicket ticket, bool includeTariffSource = true)
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
                Gross = Amount(ticket.TotalBilling), Net = Amount(ticket.TotalNetRevenue),
                Tons = Amount(ticket.Tons ?? 0m),
                IsManualTariffRate = includeTariffSource && ticket.IsManualTariffRate, ticket.IsManualBafRate
            });
        }

        public static bool HasChanges(DispatchTicket ticket, DispatchTicket preview)
        {
            return ChargeSnapshot(ticket, false) != ChargeSnapshot(preview, false);
        }

        public static string ReviewFingerprint(DispatchTicket ticket, DispatchTicket preview, VesselSchedule schedule)
        {
            string snapshot = JsonSerializer.Serialize(new
            {
                ticket.DispatchTicketId, ticket.Status, ticket.EditedDate, ticket.DateLeft, ticket.DateArrived, ticket.TimeLeft, ticket.TimeArrived,
                ticket.Tons, schedule.Revision, schedule.ApprovedRevision, Stored = ChargeSnapshot(ticket), Proposed = ChargeSnapshot(preview)
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        }

        public static string? Apply(DispatchTicket ticket, VesselSchedule schedule, VesselScheduleServiceEntry entry, TariffRate? rate, BafRate? bafRate)
        {
            var isWatering = IsWatering(ticket, entry);
            var wateringRate = ticket.DispatchRate;
            ticket.Status = MsapConstants.DispatchTicketStatus.AwaitingRate;
            ticket.DispatchChargeType = null;
            ticket.BAFChargeType = null;
            ticket.DispatchDiscount = 0;
            ticket.BAFDiscount = 0;
            ticket.IsManualTariffRate = !isWatering && ManualTariffForServiceDate(ticket, entry, rate).HasValue;
            ticket.IsManualBafRate = !isWatering && ManualBafForServiceDate(ticket, schedule, entry, bafRate).HasValue;
            ticket.DispatchRate = 0;
            ticket.BAFRate = 0;
            ticket.DispatchBillingAmount = 0;
            ticket.BAFBillingAmount = 0;
            ticket.DispatchNetRevenue = 0;
            ticket.BAFNetRevenue = 0;
            ticket.TotalBilling = 0;
            ticket.TotalNetRevenue = 0;
            if (isWatering)
            {
                ticket.DispatchRate = wateringRate;
            }
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
            var discount = schedule.SkipCosDiscount ? 0m : schedule.CosDiscountPercent.GetValueOrDefault();
            if (isWatering)
            {
                ticket.DispatchChargeType = "Per ton";
                ticket.BAFChargeType = null;
                ticket.DispatchRate = wateringRate;
                ticket.BAFRate = 0m;
                ticket.IsManualTariffRate = true;
                ticket.IsManualBafRate = false;
            }
            else
            {
                decimal dispatchRate = ManualTariffForServiceDate(ticket, entry, rate) ?? rate!.Dispatch;
                var bafAmount = ManualBafForServiceDate(ticket, schedule, entry, bafRate) ?? bafRate!.Amount;
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
                ticket.IsManualTariffRate = ManualTariffForServiceDate(ticket, entry, rate).HasValue;
                ticket.IsManualBafRate = ManualBafForServiceDate(ticket, schedule, entry, bafRate).HasValue;
            }
            ApplyDiscount(ticket, discount, isWateringOverride: isWatering);
            ticket.Status = MsapConstants.DispatchTicketStatus.ForBilling;
            return null;
        }

        public static void ApplyDiscount(DispatchTicket ticket, decimal discount, bool discountOnTariff = true, bool discountOnBaf = true, bool? isWateringOverride = null)
        {
            if (discount is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(discount), "Discount must be between 0% and 100%.");
            }
            var hours = Math.Max(ticket.RawDurationHours ?? ticket.TotalHours, 1m);
            var isWatering = isWateringOverride ?? IsWatering(ticket);
            var quantity = isWatering && ticket.Tons is > 0m
                ? ticket.Tons.Value
                : ticket.DispatchChargeType == "Per hour" ? hours : 1m;
            var dispatchGross = ticket.DispatchRate * quantity;
            var bafGross = isWatering
                ? 0m
                : ticket.BAFRate * (ticket.BAFChargeType == "Per hour" ? hours : 1m);
            var tariffDiscount = discountOnTariff ? discount : 0m;
            var bafDiscount = discountOnBaf ? discount : 0m;
            ticket.DispatchDiscount = tariffDiscount;
            ticket.BAFDiscount = bafDiscount;
            ticket.DispatchBillingAmount = Math.Round(dispatchGross, 2);
            ticket.BAFBillingAmount = Math.Round(bafGross, 2);
            ticket.DispatchNetRevenue = Math.Round(dispatchGross * (1m - tariffDiscount / 100m), 2);
            ticket.BAFNetRevenue = Math.Round(bafGross * (1m - bafDiscount / 100m), 2);
            ticket.TotalBilling = ticket.DispatchBillingAmount + ticket.BAFBillingAmount;
            ticket.TotalNetRevenue = ticket.DispatchNetRevenue + ticket.BAFNetRevenue;
        }

        public static void ApplyFlatDiscount(
            IReadOnlyList<DispatchTicket> allTickets,
            Func<DispatchTicket, bool> isEligibleForDiscount,
            decimal flatTariffDiscount,
            decimal flatBafDiscount,
            bool discountOnTariff,
            bool discountOnBaf)
        {
            foreach (var ticket in allTickets)
            {
                ApplyDiscount(ticket, 0m, false, false);
            }

            var eligible = allTickets.Where(isEligibleForDiscount).ToList();
            if (eligible.Count == 0)
            {
                return;
            }

            var targetTariff = discountOnTariff ? Math.Round(Math.Max(0m, flatTariffDiscount), 2) : 0m;
            var targetBaf = discountOnBaf ? Math.Round(Math.Max(0m, flatBafDiscount), 2) : 0m;

            DistributeFlatComponent(
                eligible,
                targetTariff,
                t => t.DispatchBillingAmount,
                (t, gross, allocated) =>
                {
                    t.DispatchNetRevenue = gross - allocated;
                    t.DispatchDiscount = gross > 0m ? Math.Round(allocated / gross * 100m, 4) : 0m;
                });

            DistributeFlatComponent(
                eligible,
                targetBaf,
                t => t.BAFBillingAmount,
                (t, gross, allocated) =>
                {
                    t.BAFNetRevenue = gross - allocated;
                    t.BAFDiscount = gross > 0m ? Math.Round(allocated / gross * 100m, 4) : 0m;
                });

            foreach (var ticket in eligible)
            {
                ticket.TotalNetRevenue = ticket.DispatchNetRevenue + ticket.BAFNetRevenue;
            }
        }

        private static void DistributeFlatComponent(
            List<DispatchTicket> eligible,
            decimal totalFlatDiscount,
            Func<DispatchTicket, decimal> getGross,
            Action<DispatchTicket, decimal, decimal> applyAllocation)
        {
            if (totalFlatDiscount <= 0m || eligible.Count == 0)
            {
                return;
            }

            var totalGross = eligible.Sum(getGross);
            if (totalGross <= 0m)
            {
                return;
            }

            var effectiveTotal = Math.Min(totalFlatDiscount, totalGross);
            var allocations = new decimal[eligible.Count];
            var allocatedSum = 0m;

            for (var i = 0; i < eligible.Count; i++)
            {
                var gross = getGross(eligible[i]);
                var share = Math.Round(effectiveTotal * gross / totalGross, 2, MidpointRounding.AwayFromZero);
                share = Math.Clamp(share, 0m, gross);
                allocations[i] = share;
                allocatedSum += share;
            }

            var remainder = effectiveTotal - allocatedSum;
            if (remainder != 0m)
            {
                var targetIndex = -1;
                var bestGross = -1m;
                for (var i = 0; i < eligible.Count; i++)
                {
                    var gross = getGross(eligible[i]);
                    var candidate = allocations[i] + remainder;
                    if (candidate >= 0m && candidate <= gross && gross > bestGross)
                    {
                        bestGross = gross;
                        targetIndex = i;
                    }
                }

                if (targetIndex >= 0)
                {
                    allocations[targetIndex] += remainder;
                }
            }

            for (var i = 0; i < eligible.Count; i++)
            {
                var gross = getGross(eligible[i]);
                applyAllocation(eligible[i], gross, allocations[i]);
            }
        }

        public static DateOnly BafCycleStart(DateOnly serviceDate)
        {
            int daysSinceTuesday = ((int)serviceDate.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
            return serviceDate.AddDays(-daysSinceTuesday);
        }

        public static decimal? ManualTariffForServiceDate(DispatchTicket ticket, VesselScheduleServiceEntry entry, TariffRate? rate)
        {
            // Missing-match manual amounts follow approved table rates; explicit and unknown legacy exceptions stay fixed.
            return entry.IsManualTariffOverride == false && ticket.DateLeft.HasValue
                && rate?.ApprovedAt != null && rate.AsOfDate <= ticket.DateLeft.Value
                    ? null : entry.ManualTariffRate;
        }

        private static decimal? ManualBafForServiceDate(DispatchTicket ticket, VesselSchedule schedule, VesselScheduleServiceEntry entry, BafRate? bafRate)
        {
            // Approved weekly table BAF supersedes manual BAF, including non-recurring exceptions.
            if (ticket.DateLeft.HasValue && bafRate?.ApprovedAt != null
                && bafRate.AsOfDate >= BafCycleStart(ticket.DateLeft.Value) && bafRate.AsOfDate <= ticket.DateLeft.Value)
            {
                return null;
            }
            return ticket.DateLeft.HasValue
                && BafCycleStart(ticket.DateLeft.Value) == BafCycleStart(DateOnly.FromDateTime(schedule.PlannedStart))
                    ? entry.ManualBafRate : null;
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
            if (schedule.ApprovedRevision != schedule.Revision
                || schedule.Status is MsapConstants.VesselScheduleStatus.Tentative or MsapConstants.VesselScheduleStatus.Cancelled)
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
            if (IsWatering(ticket, entry))
            {
                if (!ticket.Tons.HasValue || ticket.Tons.Value <= 0m)
                {
                    return "Enter the watering tons on the ticket entry.";
                }
                if (ticket.DispatchRate <= 0m)
                {
                    return "Enter the watering rate on the ticket entry.";
                }
                var wateringDiscount = schedule.SkipCosDiscount ? 0m : schedule.CosDiscountPercent.GetValueOrDefault();
                return wateringDiscount is < 0 or > 100 ? "The approved COS discount must be between 0% and 100%." : null;
            }
            if (!entry.TariffBasis.HasValue || !Enum.IsDefined(entry.TariffBasis.Value))
            {
                return "The approved service has no valid tariff charging basis.";
            }
            if (!entry.BafMethod.HasValue || !Enum.IsDefined(entry.BafMethod.Value))
            {
                return "The approved service has no valid BAF method.";
            }
            decimal? dispatchRate = ManualTariffForServiceDate(ticket, entry, rate) ?? rate?.Dispatch;
            decimal? manualBaf = ManualBafForServiceDate(ticket, schedule, entry, bafRate);
            var bafAmount = manualBaf ?? bafRate?.Amount;
            if (!dispatchRate.HasValue || dispatchRate < 0)
            {
                return $"No valid tariff rate applies on {ticket.DateLeft:dd MMM yyyy}. Check rate maintenance or the approved manual tariff.";
            }
            if (!bafAmount.HasValue || bafAmount < 0)
            {
                return $"No valid BAF rate applies on {ticket.DateLeft:dd MMM yyyy}. Check rate maintenance or the approved manual BAF.";
            }
            if (!manualBaf.HasValue && (bafRate == null || !bafRate.ApprovedAt.HasValue
                || bafRate.AsOfDate < BafCycleStart(ticket.DateLeft.Value)))
            {
                return $"Approve BAF coverage for the week beginning {BafCycleStart(ticket.DateLeft.Value):dd MMM yyyy} before calculating charges.";
            }
            var discount = schedule.SkipCosDiscount ? 0m : schedule.CosDiscountPercent.GetValueOrDefault();
            return discount is < 0 or > 100 ? "The approved COS discount must be between 0% and 100%." : null;
        }
    }
}
