using System.Text.Json;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;

namespace IBS.Services.MSAP
{
    public static class JobProgressCalculator
    {
        public const int TariffStage = 3;
        public const int ApprovalStage = 4;
        public const int BillingStage = 5;
        public const int PostingStage = 6;
        public const int CollectionStage = 7;
        public const int DepositStage = 8;
        public const int ClearingStage = 9;
        public const int CompletedStage = 10;

        public static async Task<JobProgressViewModel> LoadAsync(IUnitOfWork unitOfWork, JobOrder? job, VesselSchedule? schedule, CancellationToken ct)
        {
            List<DispatchTicket> tickets = job == null ? [] : (await unitOfWork.DispatchTicket.GetAllAsync(t => t.JobOrderId == job.JobOrderId, ct)).ToList();
            List<int> billingIds = tickets.Where(t => t.BillingId.HasValue).Select(t => t.BillingId!.Value).Distinct().ToList();
            List<Billing> billings = job == null ? [] : (await unitOfWork.Billing.GetAllAsync(b => b.JobOrderId == job.JobOrderId || billingIds.Contains(b.MsapBillingId), ct)).ToList();
            List<int> collectionIds = billings.Where(b => b.CollectionId.HasValue).Select(b => b.CollectionId!.Value).Distinct().ToList();
            IEnumerable<Collection> collections = collectionIds.Count == 0 ? []
                : await unitOfWork.Collection.GetAllAsync(c => collectionIds.Contains(c.MsapCollectionId), ct);
            return Calculate(job, schedule, tickets, billings, collections);
        }

        public static async Task<List<JobProgressViewModel>> LoadForBillingsAsync(IUnitOfWork unitOfWork, IEnumerable<int> billingIds, CancellationToken ct)
        {
            List<int> ids = billingIds.Distinct().ToList();
            IEnumerable<Billing> billings = await unitOfWork.Billing.GetAllAsync(b => ids.Contains(b.MsapBillingId), ct);
            IEnumerable<DispatchTicket> tickets = await unitOfWork.DispatchTicket.GetAllAsync(t => t.BillingId.HasValue && ids.Contains(t.BillingId.Value), ct);
            List<int> jobIds = billings.Select(b => b.JobOrderId).Concat(tickets.Select(t => t.JobOrderId))
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().OrderBy(id => id).ToList();
            if (jobIds.Count == 0 && billings.Any())
            {
                List<int> collectionIds = billings.Where(b => b.CollectionId.HasValue).Select(b => b.CollectionId!.Value).Distinct().ToList();
                IEnumerable<Collection> collections = collectionIds.Count == 0 ? []
                    : await unitOfWork.Collection.GetAllAsync(c => collectionIds.Contains(c.MsapCollectionId), ct);
                var progress = Calculate(new JobOrder { Status = MsapConstants.JobOrderStatus.Closed }, null, [], billings, collections);
                progress.JobOrderId = null;
                return [progress];
            }
            return await LoadForJobsAsync(unitOfWork, jobIds, ct);
        }

        public static async Task<List<JobProgressViewModel>> LoadForJobsAsync(IUnitOfWork unitOfWork, IEnumerable<int> jobIds, CancellationToken ct)
        {
            var result = new List<JobProgressViewModel>();
            foreach (int jobId in jobIds.Distinct().OrderBy(id => id))
            {
                JobOrder? job = await unitOfWork.JobOrder.GetAsync(j => j.JobOrderId == jobId, ct);
                VesselSchedule? schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.JobOrderId == jobId, ct);
                result.Add(await LoadAsync(unitOfWork, job, schedule, ct));
            }
            return result;
        }

        public static async Task<string?> GetBlockerAsync(IUnitOfWork unitOfWork, int? jobOrderId, int requiredStage, CancellationToken ct)
        {
            if (!jobOrderId.HasValue)
            {
                return null;
            }
            JobOrder? job = await unitOfWork.JobOrder.GetAsync(j => j.JobOrderId == jobOrderId.Value, ct);
            if (job == null)
            {
                return "Linked Job Order not found.";
            }
            VesselSchedule? schedule = await unitOfWork.VesselSchedule.GetAsync(s => s.JobOrderId == jobOrderId.Value, ct);
            JobProgressViewModel progress = await LoadAsync(unitOfWork, job, schedule, ct);
            return progress.Stopped || progress.Stage < requiredStage ? progress.Guidance : null;
        }

        public static async Task<string?> GetBillingBlockerAsync(IUnitOfWork unitOfWork, Billing billing, int requiredStage, CancellationToken ct)
        {
            IEnumerable<DispatchTicket> tickets = await unitOfWork.DispatchTicket.GetAllAsync(t => t.BillingId == billing.MsapBillingId, ct);
            IEnumerable<int?> jobIds = tickets.Select(t => t.JobOrderId).Append(billing.JobOrderId).Where(id => id.HasValue).Distinct();
            foreach (var jobId in jobIds)
            {
                string? blocker = await GetBlockerAsync(unitOfWork, jobId, requiredStage, ct);
                if (blocker != null)
                {
                    return blocker;
                }
            }
            return null;
        }

        public static JobProgressViewModel Calculate(JobOrder? job, VesselSchedule? schedule, IEnumerable<DispatchTicket> tickets, IEnumerable<Billing> billings, IEnumerable<Collection>? collections = null)
        {
            var result = new JobProgressViewModel { HasSchedule = schedule != null, JobOrderId = job?.JobOrderId, JobOrderNumber = job?.JobOrderNumber };
            List<DispatchTicket> active = tickets.Where(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted).OrderBy(t => t.DispatchTicketId).ToList();
            List<Billing> bills = billings.OrderBy(b => b.MsapBillingId).ToList();
            if (schedule?.Status == MsapConstants.VesselScheduleStatus.Cancelled || job?.Status == MsapConstants.JobOrderStatus.Cancelled)
            {
                result.Stopped = true;
                result.IsCancelled = true;
                result.Guidance = "This booking is cancelled. Existing records remain available for reference.";
                return result;
            }
            if (job?.Status == MsapConstants.JobOrderStatus.Invalidated)
            {
                result.Stopped = true;
                result.IsInvalidated = true;
                result.Guidance = "This Job Order was invalidated after a schedule revision and is retained for reference. Continue through the revised schedule.";
                return result;
            }
            if (job == null)
            {
                if (schedule == null || schedule.JobOrderId.HasValue)
                {
                    result.Stopped = true;
                    result.Guidance = "The linked Job Order is unavailable. Ask an authorized user to review the booking.";
                    return result;
                }
                var assigned = new List<int>();
                try
                {
                    assigned = JsonSerializer.Deserialize<List<int>>(schedule.AssignedTugboatIds ?? "[]") ?? [];
                }
                catch (JsonException)
                {
                    // Invalid legacy assignments must be corrected before confirmation.
                }
                bool ready = schedule.CustomerId > 0 && schedule.VesselId > 0 && schedule.PortId > 0
                    && schedule.TerminalId > 0 && schedule.PlannedEnd > schedule.PlannedStart && assigned.Any(id => id > 0);
                result.Stage = ready ? 1 : 0;
                result.Guidance = ready ? "Review the booking and assigned tugboats. Confirmation creates its Job Order." : "Complete booking details and assign at least one tugboat before confirmation.";
                SetAction(result, ready ? "Review and Confirm" : "Complete Booking", "VesselSchedule", ready ? "Confirm" : "Edit", schedule.VesselScheduleId,
                    ready ? ProcedureEnum.CreateJobOrder : ProcedureEnum.EditJobOrder, ready ? "schedule confirmation" : "booking preparation");
                return result;
            }
            List<DispatchTicket> unknown = active.Where(t => !MsapConstants.DispatchTicketStatus.All.Contains(t.Status)).ToList();
            List<DispatchTicket> missingBills = active.Where(t => (t.Status == MsapConstants.DispatchTicketStatus.Billed || t.BillingId.HasValue) && !bills.Any(b => b.MsapBillingId == t.BillingId)).ToList();
            if (unknown.Count > 0 || missingBills.Count > 0
                || job.Status is not (MsapConstants.JobOrderStatus.Open or MsapConstants.JobOrderStatus.Closed)
                || (job.Status == MsapConstants.JobOrderStatus.Closed && active.Any(t => t.Status != MsapConstants.DispatchTicketStatus.Billed))
                || bills.Any(b => b.Status is not (MsapConstants.BillingStatus.ForPosting or MsapConstants.BillingStatus.ForCollection or MsapConstants.BillingStatus.Collected)
                    || (b.Status == MsapConstants.BillingStatus.Collected && b.Balance > 0)))
            {
                result.Stage = 2;
                result.Stopped = true;
                result.Tickets = unknown.Concat(missingBills).Distinct().ToList();
                result.Billings = bills;
                result.Guidance = "Linked records have an unresolved status or billing reference. Ask an authorized user to review them before proceeding.";
                return result;
            }
            List<DispatchTicket> incomplete = active.Where(t => !HasActualTimes(t)).ToList();
            if ((active.Count == 0 && bills.Count == 0) || incomplete.Count > 0)
            {
                result.Stage = 2;
                result.Tickets = incomplete;
                result.Guidance = active.Count == 0 ? "Record a Dispatch Ticket to continue. No active tickets have been recorded." : $"{incomplete.Count} Dispatch Ticket(s) need valid actual start and end times.";
                if (incomplete.Count > 0)
                {
                    if (job.Status != MsapConstants.JobOrderStatus.Open || active.Any(t => t.Status == MsapConstants.DispatchTicketStatus.Billed))
                    {
                        result.Stopped = true;
                        result.Billings = bills;
                        result.Guidance += " Resolve linked billing through its existing correction or reversal controls before editing service details.";
                    }
                    else
                    {
                        SetAction(result, "Complete Dispatch Details", "DispatchTicket", "EditTicket", incomplete[0].DispatchTicketId, ProcedureEnum.EditDispatchTicket, "service details");
                    }
                }
                else if (job.Status == MsapConstants.JobOrderStatus.Open && schedule?.Status != MsapConstants.VesselScheduleStatus.Completed && bills.Count == 0)
                {
                    SetAction(result, "Record Dispatch Ticket", "DispatchTicket", "Create", null, ProcedureEnum.CreateDispatchTicket, "service recording");
                }
                else
                {
                    result.Stopped = true;
                    result.Billings = bills;
                    result.Guidance = "No active Dispatch Tickets remain, and this booking cannot accept new tickets. Review its booking status and linked billing before continuing.";
                }
                return result;
            }
            List<DispatchTicket> tariff = active.Where(t => t.Status is MsapConstants.DispatchTicketStatus.ForTariff or MsapConstants.DispatchTicketStatus.Disapproved).ToList();
            if (tariff.Count > 0)
            {
                result.Stage = TariffStage;
                result.Tickets = tariff;
                result.Guidance = $"{tariff.Count} Dispatch Ticket(s) need tariff preparation or correction. Finish these before approval.";
                SetAction(result, tariff[0].Status == MsapConstants.DispatchTicketStatus.Disapproved ? "Correct Tariff" : "Set Tariff", "DispatchTicket",
                    tariff[0].Status == MsapConstants.DispatchTicketStatus.Disapproved ? "EditTariff" : "SetTariff", tariff[0].DispatchTicketId, ProcedureEnum.SetTariff, "tariff preparation");
                return result;
            }
            List<DispatchTicket> approval = active.Where(t => t.Status == MsapConstants.DispatchTicketStatus.ForApproval).ToList();
            if (approval.Count > 0)
            {
                result.Stage = ApprovalStage;
                result.Tickets = approval;
                result.Guidance = $"{approval.Count} Dispatch Ticket(s) are awaiting tariff approval. All active tickets must be approved before billing.";
                SetAction(result, "Review Charges", "DispatchTicket", "Preview", approval[0].DispatchTicketId, ProcedureEnum.ApproveTariff, "tariff approval");
                return result;
            }
            List<DispatchTicket> unbilled = active.Where(t => t.Status == MsapConstants.DispatchTicketStatus.ForBilling && !t.BillingId.HasValue).ToList();
            if (unbilled.Count > 0)
            {
                result.Stage = BillingStage;
                result.Tickets = unbilled;
                result.Guidance = $"{unbilled.Count} approved Dispatch Ticket(s) still need billing. Include every eligible ticket before posting.";
                SetAction(result, "Create Billing", "Billing", "Create", null, ProcedureEnum.CreateBilling, "billing preparation");
                return result;
            }
            List<Billing> posting = bills.Where(b => b.Status == MsapConstants.BillingStatus.ForPosting).ToList();
            if (posting.Count > 0)
            {
                result.Stage = PostingStage;
                result.Billings = posting;
                result.Guidance = $"{posting.Count} billing(s) need review and posting before collection.";
                SetAction(result, "Review and Post Billing", "Billing", "Index", posting[0].MsapBillingId, ProcedureEnum.CreateBilling, "billing posting");
                return result;
            }
            if (bills.Count == 0 || active.Any(t => t.Status != MsapConstants.DispatchTicketStatus.Billed || !t.BillingId.HasValue))
            {
                result.Stage = BillingStage;
                result.Stopped = true;
                result.Guidance = "Review the linked billing references before proceeding. Every active ticket must have a billing record.";
                result.Tickets = active;
                return result;
            }
            List<Billing> outstanding = bills.Where(b => b.Balance > 0).ToList();
            if (outstanding.Count > 0)
            {
                result.Stage = CollectionStage;
                result.Billings = outstanding;
                result.Guidance = $"{outstanding.Count} billing(s) await collection. Outstanding balance: ₱{outstanding.Sum(b => Math.Max(0, b.Balance)):N2}.";
                SetAction(result, "Record Collection", "Collection", "Create", null, ProcedureEnum.CreateCollection, "collection recording");
                return result;
            }
            result.Billings = bills;
            List<Collection> receipts = (collections ?? bills.Where(b => b.Collection != null).Select(b => b.Collection!))
                .Where(c => bills.Any(b => b.CollectionId == c.MsapCollectionId))
                .DistinctBy(c => c.MsapCollectionId).OrderBy(c => c.MsapCollectionId).ToList();
            if (bills.Any(b => b.CollectionId.HasValue && receipts.All(c => c.MsapCollectionId != b.CollectionId)))
            {
                result.Stage = DepositStage;
                result.Stopped = true;
                result.Guidance = "A linked collection receipt is unavailable. Review the collection references before depositing.";
                return result;
            }
            Collection? deposit = receipts.FirstOrDefault(c => !c.DepositDate.HasValue && !c.ClearedDate.HasValue);
            Collection? clearing = receipts.FirstOrDefault(c => !c.ClearedDate.HasValue);
            result.BankingCollection = deposit ?? clearing;
            if (result.BankingCollection != null)
            {
                result.Stage = deposit != null ? DepositStage : ClearingStage;
                string action = deposit != null ? (deposit.ReturnedDate.HasValue ? "Redeposit" : "Record Deposit") : "Apply Clearing Date";
                result.Guidance = $"Receipt #{result.BankingCollection.MsapCollectionNumber}: {result.BankingCollection.PaymentStatus}. Complete banking for every linked receipt before finishing the job.";
                SetAction(result, action, "Collection", "Banking", result.BankingCollection.MsapCollectionId,
                    ProcedureEnum.CreateCollection, deposit != null ? "collection deposit" : "collection clearing");
                return result;
            }
            result.Stage = CompletedStage;
            result.Guidance = "All active tickets are billed, every linked billing has no outstanding balance, and all collection receipts have cleared.";
            return result;
        }

        private static bool HasActualTimes(DispatchTicket ticket)
        {
            return ticket is { DateLeft: not null, TimeLeft: not null, DateArrived: not null, TimeArrived: not null }
                && ticket.DateArrived.Value.ToDateTime(ticket.TimeArrived.Value) > ticket.DateLeft.Value.ToDateTime(ticket.TimeLeft.Value);
        }

        private static void SetAction(JobProgressViewModel result, string label, string controller, string action, int? id, ProcedureEnum permission, string waitingFor)
        {
            result.ActionLabel = label;
            result.ActionController = controller;
            result.ActionName = action;
            result.TargetId = id;
            result.Permission = permission;
            result.WaitingFor = waitingFor;
        }
    }
}
