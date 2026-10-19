using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;

namespace Checks.MSAP
{
    internal static class JobProgressCheck
    {
        public static void Run()
        {
            var job = new CustomerOrderSlip { CustomerOrderSlipId = 1, Status = MsapConstants.CustomerOrderSlipStatus.Open };
            var schedule = new VesselSchedule { VesselScheduleId = 1, Status = "Tentative" };
            Check(JobProgressCalculator.Calculate(null, schedule, [], []).Stage == 0, "Incomplete booking must remain in Planning.");
            schedule.CustomerId = schedule.VesselId = schedule.PortId = schedule.TerminalId = 1;
            schedule.PlannedStart = new DateTime(2030, 1, 1, 8, 0, 0);
            schedule.PlannedEnd = schedule.PlannedStart.AddHours(2);
            schedule.AssignedTugboatIds = "[1]";
            var preparedSchedule = JobProgressCalculator.Calculate(null, schedule, [], []);
            Check(preparedSchedule.Stage == 1 && preparedSchedule.VesselScheduleId == schedule.VesselScheduleId
                && preparedSchedule.ActionName == "Details", "Prepared booking must link to its canonical review page.");
            Check(JobProgressCalculator.Calculate(job, null, [], []).Stage == 2, "Direct COS must start at Service.");
            Check(MsapConstants.VesselScheduleStatus.GetDisplayName("Completed") == "Service Completed"
                && MsapConstants.CustomerOrderSlipStatus.GetDisplayName("Closed") == "Fully Billed", "Completion labels must distinguish service from billing.");
            Check(!CustomerOrderSlipService.CanCompleteService([]), "Service completion must require active tickets.");
            var first = Ticket(1, "For Billing");
            Check(CustomerOrderSlipService.CanCompleteService([first]), "Finished actual service must allow completion before billing.");
            var second = Ticket(2, "For Tariff");
            var deleted = new DispatchTicket { DispatchTicketId = 3, Status = "Deleted" };
            Check(JobProgressCalculator.Calculate(job, null, [first, second, deleted], []).Stopped, "Retired tariff states must block billing without exposing manual approval.");
            second.TimeArrived = null;
            Check(!CustomerOrderSlipService.CanCompleteService([first, second, deleted]), "Missing actual times must prevent service completion.");
            Check(JobProgressCalculator.Calculate(job, null, [first, second], []).Stage == 2, "Missing actual times must block tariff progression.");
            second = Ticket(2, "Disapproved");
            var rejected = JobProgressCalculator.Calculate(job, null, [first, second], []);
            Check(rejected.Stage == 2 && rejected.Stopped && string.IsNullOrEmpty(rejected.ActionName), "Retired disapproved charges must not expose the old editor.");
            second.Status = "For Approval";
            Check(JobProgressCalculator.Calculate(job, null, [first, second], []).Stopped, "Retired manual approval must block billing.");
            second.Status = "For Billing";
            Check(JobProgressCalculator.Calculate(job, null, [first, second], []).Stage == 5, "All approved tickets must advance to Billing.");
            var bill = new Billing { MsapBillingId = 1, Status = "For Posting", Balance = 100 };
            first.BillingId = 1;
            Check(JobProgressCalculator.Calculate(job, null, [first, second], [bill]).Stage == 5, "An unreserved approved ticket must block posting.");
            second.BillingId = 1;
            Check(JobProgressCalculator.Calculate(job, null, [first, second], [bill]).Stage == 6, "Reserved tickets must advance to Posting.");
            first.Status = second.Status = "Billed";
            bill.Status = "For Collection";
            var split = new Billing { MsapBillingId = 2, Status = "For Posting", Balance = 25 };
            var splitReview = JobProgressCalculator.Calculate(job, null, [first, second], [bill, split]);
            Check(splitReview.Stage == 6 && splitReview.ActionName == "Review" && splitReview.TargetId == split.MsapBillingId,
                "Pending split bills must link directly to the selected billing review page.");
            Check(splitReview.Permission == IBS.Models.MSAP.Enums.ProcedureEnum.ApproveFinalBilling
                && splitReview.LinkedBillings.Count == 2 && splitReview.RecordedTicketCount == 2,
                "Progress must preserve both split bills and require final-approval access.");
            split.Status = "For Collection";
            job.Status = MsapConstants.CustomerOrderSlipStatus.Closed;
            schedule.Status = MsapConstants.VesselScheduleStatus.Completed;
            schedule.ApprovedRevision = schedule.Revision;
            var collection = JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split]);
            Check(collection.Stage == 7 && collection.Billings.Count == 2 && collection.Guidance.Contains("125.00", StringComparison.Ordinal), "Collection must aggregate every outstanding billing.");
            bill.Status = "Collected";
            bill.Balance = 0;
            Check(JobProgressCalculator.Calculate(job, null, [first, second], [bill, split]).Stage == 7, "One paid bill must not complete a partially collected job.");
            split.Status = "Collected";
            split.Balance = 0;
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split]).Stage == JobProgressCalculator.CompletedStage, "All linked bills must be collected to finish.");
            split.Status = "For Collection";
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split]).Stage == JobProgressCalculator.CompletedStage, "A posted zero-balance bill must not require an impossible collection.");
            var receipt = new Collection { MsapCollectionId = 1, MsapCollectionNumber = "CR1" };
            var other = new Collection { MsapCollectionId = 2, MsapCollectionNumber = "CR2" };
            bill.CollectionId = 1;
            split.CollectionId = 2;
            var deposit = JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split], [receipt, other]);
            Check(deposit.Stage == JobProgressCalculator.DepositStage && deposit.TargetId == 1, "Paid billings must proceed to deposit, not completion.");
            receipt.DepositDate = new DateOnly(2026, 10, 12);
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split], [receipt, other]).TargetId == 2, "Every collection receipt must be deposited.");
            other.DepositDate = receipt.DepositDate;
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split], [receipt, other]).Stage == JobProgressCalculator.ClearingStage, "Deposited receipts must proceed to clearing.");
            receipt.DepositDate = null;
            receipt.ReturnedDate = new DateOnly(2026, 10, 12);
            var returned = JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split], [receipt, other]);
            Check(returned.Stage == JobProgressCalculator.DepositStage && returned.ActionLabel == "Redeposit", "Returned checks must return progress to redeposit.");
            receipt.DepositDate = receipt.ReturnedDate;
            receipt.ClearedDate = receipt.DepositDate;
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split], [receipt, other]).TargetId == 2, "Every receipt must clear before completion.");
            other.ClearedDate = other.DepositDate;
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split], [receipt, other]).Stage == JobProgressCalculator.CompletedStage, "Cleared receipts must complete the job.");
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split], [receipt]).Stopped, "Missing collection references must block completion.");
            bill.CollectionId = split.CollectionId = null;
            split.Status = "Collected";
            bill.Status = "For Posting";
            first.Status = second.Status = "For Billing";
            job.Status = MsapConstants.CustomerOrderSlipStatus.Open;
            Check(JobProgressCalculator.Calculate(job, schedule, [first, second], [bill, split]).Stage == 6
                && schedule.Status == MsapConstants.VesselScheduleStatus.Completed, "Reversal must return progress to Posting without reopening vessel service.");
            Check(JobProgressCalculator.Calculate(job, null, [deleted], [bill]).Stage == 6, "Terminal tickets must not block existing billing progression.");
            first.Status = "Unknown";
            Check(JobProgressCalculator.Calculate(job, null, [first], []).Stopped, "Unknown statuses must block progression.");
            first.Status = "Billed";
            Check(JobProgressCalculator.Calculate(job, null, [first], []).Stopped, "Missing billed references must block progression.");
            job.Status = MsapConstants.CustomerOrderSlipStatus.Invalidated;
            var invalidated = JobProgressCalculator.Calculate(job, null, [], []);
            Check(invalidated.Stopped && invalidated.IsInvalidated && invalidated.Permission == null, "Invalidated jobs must retain a reference-only status without a next action.");
            job.Status = "Cancelled";
            var cancelled = JobProgressCalculator.Calculate(job, schedule, [first], []);
            Check(cancelled.Stopped && cancelled.Permission == null, "Cancelled jobs must not expose a next action.");
            var printGate = new Billing { Status = MsapConstants.BillingStatus.ForPosting };
            Check(!BillingService.CanPrint(printGate), "Unposted billing must not be printable.");
            printGate.Status = MsapConstants.BillingStatus.ForCollection;
            Check(!BillingService.CanPrint(printGate), "Posting without final approval evidence must not enable printing.");
            printGate.ApprovedRevision = printGate.Revision;
            printGate.ApprovedByUserId = "approver";
            printGate.ApprovedAt = DateTime.Now;
            Check(BillingService.CanPrint(printGate), "Current final approval evidence must enable printing.");
            printGate.ApprovedRevision = 1;
            printGate.Revision = 2;
            Check(!BillingService.CanPrint(printGate), "A stale approved revision must not be printable.");
            Console.WriteLine("PASS: Job Progress covers planning, mixed tickets, deleted tickets, actual times, rejection, approval, billing, split posting, partial collection, deposit, clearing, returned checks, multiple receipts, reversal and cancellation.");
        }

        private static DispatchTicket Ticket(int id, string status)
        {
            return new DispatchTicket
            {
                DispatchTicketId = id,
                CustomerOrderSlipId = 1,
                Status = status,
                DateLeft = new DateOnly(2026, 1, 1),
                DateArrived = new DateOnly(2026, 1, 1),
                TimeLeft = new TimeOnly(8, 0),
                TimeArrived = new TimeOnly(10, 0)
            };
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }

        internal sealed class CheckAccess : IAccessControlService
        {
            public Task<bool> HasAccessAsync(string userId, params ProcedureEnum[] procedures)
            {
                return Task.FromResult(userId is "progress-allowed" or "shared-check");
            }

            public Task<bool> HasAnyAccessAsync(string userId, params ProcedureEnum[] procedures)
            {
                return HasAccessAsync(userId, procedures);
            }
        }
    }
}
