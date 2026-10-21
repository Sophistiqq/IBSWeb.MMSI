using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class WorkflowGapCheck
    {
        public static async Task RunAsync()
        {
            Check(!await new PostedPeriodRepository(null!).IsMonthClosedAsync(2026, 4), "Suspended month restrictions queried storage or blocked April.");
            var first = new VesselScheduleServiceEntry { VesselScheduleServiceEntryId = 1, TugboatId = 1, ServiceId = 1 };
            var second = new VesselScheduleServiceEntry { VesselScheduleServiceEntryId = 2, TugboatId = 1, ServiceId = 1 };
            var job = new CustomerOrderSlip { CustomerOrderSlipId = 1, CustomerOrderSlipNumber = "COS-CHECK", Status = MsapConstants.CustomerOrderSlipStatus.Open };
            var schedule = new VesselSchedule
            {
                VesselScheduleId = 1, CustomerOrderSlipId = 1, Revision = 1, ApprovedRevision = 1,
                Status = MsapConstants.VesselScheduleStatus.Confirmed, ServiceEntries = [first, second]
            };
            DispatchTicket? existing = new DispatchTicket { DispatchTicketId = 1, VesselScheduleServiceEntryId = 1 };
            int audits = 0;
            var schedules = Proxy<IVesselScheduleRepository>((_, _) => Task.FromResult<VesselSchedule?>(schedule));
            var jobs = Proxy<ICustomerOrderSlipRepository>((_, _) => Task.FromResult<CustomerOrderSlip?>(job));
            var tickets = Proxy<IDispatchTicketRepository>((_, _) => Task.FromResult(existing));
            var bills = Proxy<IBillingRepository>((_, _) => Task.FromResult<Billing?>(null));
            var audit = Proxy<IAuditTrailRepository>((_, _) => { audits++; return Task.CompletedTask; });
            var work = Proxy<IUnitOfWork>((method, args) => method.Name switch
            {
                "get_VesselSchedule" => schedules, "get_CustomerOrderSlip" => jobs, "get_DispatchTicket" => tickets,
                "get_Billing" => bills, "get_AuditTrail" => audit,
                "ExecuteInTransactionAsync" => ((Func<Task>)args![0]!)(), "SaveAsync" => Task.CompletedTask,
                _ => throw new NotSupportedException(method.Name)
            });
            var service = new CustomerOrderSlipService(work, NullLogger<CustomerOrderSlipService>.Instance);
            Check(!(await service.CancelServiceAsync(1, 1, "", "check", "id", default)).IsSuccess, "Missing cancellation reason accepted.");
            Check(!(await service.CancelServiceAsync(1, 1, "Not performed", "check", "id", default)).IsSuccess, "Recorded service could be cancelled.");
            existing = null;
            Check((await service.CancelServiceAsync(1, 1, "Vessel request withdrawn", "check", "id", default)).IsSuccess
                && first.CancelledAt.HasValue && first.CancelledByUserId == "id" && audits == 1, "Cancellation lost its reason, identity or audit.");
            Check(JobProgressCalculator.Calculate(job, schedule, [], []).ExpectedTicketCount == 1, "Cancelled service still requires a ticket.");
            Check((await service.CancelServiceAsync(1, 1, "Duplicate click", "check", "id", default)).IsSuccess && audits == 1, "Repeated cancellation duplicated the audit.");
            Check((await service.CancelServiceAsync(1, 2, "Not required", "check", "id", default)).IsSuccess
                && job.Status == MsapConstants.CustomerOrderSlipStatus.Cancelled && schedule.Status == MsapConstants.VesselScheduleStatus.Cancelled,
                "Cancelling every unperformed service left an impossible empty job.");
            var ticket = new DispatchTicket { DispatchTicketId = 10, BAFRate = 35000m, TotalBilling = 35000m };
            var preview = new DispatchTicket { DispatchTicketId = 10, BAFRate = 35000.0000m, TotalBilling = 35000.0000m };
            Check(!ScheduledTicketCharges.HasChanges(ticket, preview), "Database decimal scale created a false rate change.");
            preview.BAFRate = preview.TotalBilling = 29000m;
            Check(ScheduledTicketCharges.HasChanges(ticket, preview), "BAF correction did not require charge review.");
            string fingerprint = ScheduledTicketCharges.ReviewFingerprint(ticket, preview, schedule);
            preview.BAFRate = 30000m;
            Check(fingerprint != ScheduledTicketCharges.ReviewFingerprint(ticket, preview, schedule), "Changed rates reused the reviewed fingerprint.");
            Console.WriteLine("PASS: month suspension, audited unperformed-service cancellation, completeness and stale charge-review detection (no database).");
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)proxy).Handler = handler;
            return proxy;
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
