using System.Linq.Expressions;
using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.Extensions.Logging.Abstractions;

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
                VesselScheduleId = 1, CustomerOrderSlipId = 1, Revision = 1, PlannedStart = new DateTime(2026, 1, 1),
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
                "An unapproved preview must block charges and clear calculated amounts.");
            var progress = JobProgressCalculator.Calculate(job, schedule, [ticket], []);
            Check(progress.Stage == 1 && progress.Permission == ProcedureEnum.ApproveSchedule,
                "An unapproved preview must not skip COS approval or unlock billing.");
            schedule.ApprovedRevision = schedule.Revision;
            schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
            Check(ScheduledTicketCharges.Apply(ticket, schedule, entry, null, null) == null
                && ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling && ticket.TotalBilling == 120,
                "An approved preview must calculate charges using the normal minimum-hour rule.");
            Check(JobProgressCalculator.Calculate(job, schedule, [ticket], []).Stage == JobProgressCalculator.BillingStage,
                "A complete approved job should progress to billing.");
            schedule.Revision++;
            Check(JobProgressCalculator.Calculate(job, schedule, [ticket], []).Stage == 1,
                "A pending revision must block billing even when ticket charges were previously ready.");
            Console.WriteLine("Approval readiness and charge preview checks passed (no database).");
        }

        public static async Task RunSaveAsync()
        {
            var job = new CustomerOrderSlip { CustomerOrderSlipId = 1, CustomerOrderSlipNumber = "URGENT-COS", Status = MsapConstants.CustomerOrderSlipStatus.Open };
            var entry = new VesselScheduleServiceEntry
            {
                VesselScheduleServiceEntryId = 1,
                TugboatId = 1,
                ServiceId = 1,
                TariffBasis = TariffChargeBasis.PerHour,
                BafMethod = BafCalculationMethod.PerMove
            };
            var schedule = new VesselSchedule
            {
                VesselScheduleId = 1,
                CustomerOrderSlipId = 1,
                Revision = 1,
                PlannedStart = new DateTime(2026, 1, 1),
                Status = MsapConstants.VesselScheduleStatus.Tentative,
                ServiceEntries = [entry],
                AssignedTugboatIds = "[1]",
                SkipCosDiscount = true
            };
            List<DispatchTicket> saved = [];
            int audits = 0;
            var schedules = Proxy<IVesselScheduleRepository>((_, _) => Task.FromResult<VesselSchedule?>(schedule));
            var jobs = Proxy<ICustomerOrderSlipRepository>((_, _) => Task.FromResult<CustomerOrderSlip?>(job));
            var tickets = Proxy<IDispatchTicketRepository>((method, args) =>
            {
                switch (method.Name)
                {
                    case "GetAsync":
                        var predicate = (Expression<Func<DispatchTicket, bool>>)args![0]!;
                        return Task.FromResult(saved.FirstOrDefault(predicate.Compile()));
                    case "GetAllAsync":
                        return Task.FromResult<IEnumerable<DispatchTicket>>(saved);
                    case "IsCustomerOrderSlipEditableAsync":
                        return Task.FromResult(job.Status == MsapConstants.CustomerOrderSlipStatus.Open);
                    case "AddAsync":
                        var ticket = (DispatchTicket)args![0]!;
                        ticket.DispatchTicketId = saved.Count + 1;
                        saved.Add(ticket);
                        return Task.CompletedTask;
                    default:
                        throw new NotSupportedException(method.Name);
                }
            });
            var periods = Proxy<IPostedPeriodRepository>((_, _) => Task.FromResult<MsapPostedPeriod?>(null));
            var captains = Proxy<ITugMasterRepository>((_, _) => Task.FromResult<TugMaster?>(new TugMaster { TugMasterId = 1 }));
            var tariffs = Proxy<ITariffTableRepository>((_, _) => Task.FromResult<TariffRate?>(null));
            var bafs = Proxy<IBafTableRepository>((_, _) => Task.FromResult<BafRate?>(null));
            var bills = Proxy<IBillingRepository>((_, _) => Task.FromResult<Billing?>(null));
            var audit = Proxy<IAuditTrailRepository>((_, _) => { audits++; return Task.CompletedTask; });
            var work = Proxy<IUnitOfWork>((method, args) => method.Name switch
            {
                "get_VesselSchedule" => schedules,
                "get_CustomerOrderSlip" => jobs,
                "get_DispatchTicket" => tickets,
                "get_PostedPeriod" => periods,
                "get_TugMaster" => captains,
                "get_TariffTable" => tariffs,
                "get_BafTable" => bafs,
                "get_Billing" => bills,
                "get_AuditTrail" => audit,
                "ExecuteInTransactionAsync" => ((Func<Task>)args![0]!)(),
                "SaveAsync" => Task.CompletedTask,
                _ => throw new NotSupportedException(method.Name)
            });
            var service = new DispatchTicketService(work, null!, NullLogger<DispatchTicketService>.Instance);
            var input = new DispatchTicketViewModel
            {
                CustomerOrderSlipId = 1,
                VesselScheduleServiceEntryId = 1,
                DispatchNumber = "URGENT-1",
                TugBoatId = 1,
                ServiceId = 1,
                TugMasterId = 1,
                Date = new DateOnly(2026, 1, 1),
                DateLeft = new DateOnly(2026, 1, 1),
                DateArrived = new DateOnly(2026, 1, 1),
                TimeLeft = new TimeOnly(8, 0),
                TimeArrived = new TimeOnly(8, 30)
            };
            var created = await service.CreateDispatchTicketAsync(input, null, null, "Check", default);
            Check(created.IsSuccess, $"Urgent actual ticket must save before approval: {created.Message}");
            var actual = saved.Single();
            Check(actual.Status == MsapConstants.DispatchTicketStatus.CalculationPending && actual.TotalBilling == 0
                && actual.RawDurationMinutes == 30 && audits == 1, "Urgent save lost its pending status, actual duration or audit.");
            Check(JobProgressCalculator.Calculate(job, schedule, saved, []).Stage == 1, "Urgent save unlocked billing before approval.");
            Check(!(await service.CreateDispatchTicketAsync(input, null, null, "Check", default)).IsSuccess, "Duplicate urgent service was accepted.");
            input.FromEntity(actual);
            input.TimeArrived = new TimeOnly(8, 45);
            Check((await service.UpdateDispatchTicketAsync(input, null, null, "Check", default)).IsSuccess
                && actual.RawDurationMinutes == 45 && actual.Status == MsapConstants.DispatchTicketStatus.CalculationPending,
                "Urgent actual-ticket edit must save while approval is pending.");
            Check(!(await service.RecalculateChargesAsync(actual.DispatchTicketId, 1, "Check", default)).IsSuccess,
                "Recalculation bypassed COS approval.");
            actual.Status = MsapConstants.DispatchTicketStatus.Deleted;
            Check((await service.RestoreTicketAsync(actual.DispatchTicketId, "Check", default)).IsSuccess
                && actual.Status == MsapConstants.DispatchTicketStatus.CalculationPending, "Restoring urgent actuals required charge readiness.");
            schedule.ApprovedRevision = schedule.Revision;
            schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
            input.FromEntity(actual);
            input.TimeArrived = new TimeOnly(9, 0);
            Check((await service.UpdateDispatchTicketAsync(input, null, null, "Check", default)).IsSuccess
                && actual.Status == MsapConstants.DispatchTicketStatus.CalculationPending, "Missing rates prevented saving valid actual details.");
            Check(JobProgressCalculator.Calculate(job, schedule, saved, []).Stage < JobProgressCalculator.BillingStage,
                "Approval alone unlocked billing before charge calculation.");
            entry.ManualTariffRate = 100;
            entry.ManualBafRate = 20;
            var review = await service.PreviewChargesAsync(actual.DispatchTicketId, 1, default);
            Check(review.IsSuccess && (await service.RecalculateChargesAsync(actual.DispatchTicketId, 1, "Check", default, review.Data!.Fingerprint)).IsSuccess
                && actual.Status == MsapConstants.DispatchTicketStatus.ForBilling && actual.TotalBilling == 120,
                "Approved urgent ticket could not be reviewed and recalculated for billing.");
            entry.CancelledAt = DateTime.Now;
            Check(!(await service.UpdateDispatchTicketAsync(input, null, null, "Check", default)).IsSuccess, "Cancelled service accepted actual-ticket changes.");
            Console.WriteLine("PASS: urgent create/edit/restore, duplicate and cancelled-service guards, approval blocking and reviewed recalculation (no database).");
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)proxy).Handler = handler;
            return proxy;
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
