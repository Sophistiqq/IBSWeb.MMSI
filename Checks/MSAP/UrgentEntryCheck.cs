using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;

namespace Checks.MSAP
{
    internal static class UrgentEntryCheck
    {
        public static void Run()
        {
            var job = new CustomerOrderSlip { CustomerOrderSlipId = 1, Status = MsapConstants.CustomerOrderSlipStatus.Open };
            var entry = new VesselScheduleServiceEntry
            {
                VesselScheduleServiceEntryId = 1, TugboatId = 1, ServiceId = 1,
                TariffBasis = TariffChargeBasis.PerHour, BafMethod = BafCalculationMethod.PerMove,
                ManualTariffRate = 100, ManualBafRate = 20
            };
            var schedule = new VesselSchedule
            {
                VesselScheduleId = 1, CustomerOrderSlipId = 1, Revision = 1,
                Status = MsapConstants.VesselScheduleStatus.Tentative, ServiceEntries = [entry], SkipCosDiscount = true
            };
            var ticket = new DispatchTicket
            {
                DispatchTicketId = 1, CustomerOrderSlipId = 1, VesselScheduleServiceEntryId = 1, TugBoatId = 1, ServiceId = 1,
                DateLeft = new DateOnly(2026, 1, 1), DateArrived = new DateOnly(2026, 1, 1),
                TimeLeft = new TimeOnly(8, 0), TimeArrived = new TimeOnly(8, 30), TotalBilling = 999
            };
            Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, null, null) != null
                && ticket.Status == MsapConstants.DispatchTicketStatus.CalculationPending && ticket.TotalBilling == 0,
                "Pending approval must retain factual tickets without calculated charges.");
            var progress = JobProgressCalculator.Calculate(job, schedule, [ticket], []);
            Check(progress.Stage == 1 && progress.Permission == ProcedureEnum.ApproveSchedule,
                "Recorded urgent tickets must not skip COS approval or unlock billing.");
            schedule.ApprovedRevision = schedule.Revision;
            schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
            Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, null, null) == null
                && ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling && ticket.TotalBilling == 120,
                "Approval must calculate the existing urgent ticket using the normal minimum-hour rule.");
            Check(JobProgressCalculator.Calculate(job, schedule, [ticket], []).Stage == JobProgressCalculator.BillingStage,
                "A complete approved urgent job should progress to billing.");
            schedule.Revision++;
            Check(JobProgressCalculator.Calculate(job, schedule, [ticket], []).Stage == 1,
                "A pending revision must block billing even when ticket charges were previously ready.");
            Console.WriteLine("Urgent entry approval and charge checks passed (no database).");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
