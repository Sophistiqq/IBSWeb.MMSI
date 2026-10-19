using System.Text.Json;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class DashboardService
    {
        private readonly MsapDbContext _dbContext;
        private readonly IVesselScheduleService _scheduleService;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(MsapDbContext dbContext, IVesselScheduleService scheduleService, ILogger<DashboardService> logger)
        {
            _dbContext = dbContext;
            _scheduleService = scheduleService;
            _logger = logger;
        }

        public async Task SetAccessAsync(DashboardCountViewModel model, string userId, IAccessControlService access)
        {
            model.ShowDashboard = await access.HasAccessAsync(userId, ProcedureEnum.AccessMsap);
            model.CanCreateSchedule = await access.HasAccessAsync(userId, ProcedureEnum.CreateCustomerOrderSlip);
            model.CanCreateBilling = await access.HasAccessAsync(userId, ProcedureEnum.CreateBilling);
            model.CanCreateCollection = await access.HasAccessAsync(userId, ProcedureEnum.CreateCollection);
            model.CanApproveSchedule = await access.HasAccessAsync(userId, ProcedureEnum.ApproveSchedule);
            model.CanApproveBilling = await access.HasAccessAsync(userId, ProcedureEnum.ApproveFinalBilling);
            model.CanApproveRates = await access.HasAccessAsync(userId, ProcedureEnum.ApproveRateUpload);
            model.CanManageRates = await access.HasAccessAsync(userId, ProcedureEnum.ManageRates);
            model.CanViewRates = await access.HasAccessAsync(userId, ProcedureEnum.ViewRates);
            model.CanViewReports = await access.HasAccessAsync(userId, ProcedureEnum.ViewMaritimeReport);
            model.CanManageReferences = await access.HasAccessAsync(userId, ProcedureEnum.ManageMaritimeMasterFile);
            model.CanManageOtherTugCharges = await access.HasAccessAsync(userId, ProcedureEnum.ManageOtherTugCharges);
            model.CanConfirmOtherTugCharges = await access.HasAccessAsync(userId, ProcedureEnum.ConfirmOtherTugCharges);
            model.CanReadCos = await access.HasAccessAsync(userId, ProcedureEnum.ViewOperations);
            model.CanViewDispatch = await access.HasAnyAccessAsync(userId, ProcedureEnum.CreateCustomerOrderSlip,
                ProcedureEnum.EditCustomerOrderSlip, ProcedureEnum.ApproveSchedule, ProcedureEnum.CreateDispatchTicket,
                ProcedureEnum.EditDispatchTicket, ProcedureEnum.ViewMaritimeReport);
            model.CanFollowUpTickets = await access.HasAnyAccessAsync(userId, ProcedureEnum.CreateDispatchTicket,
                ProcedureEnum.EditDispatchTicket, ProcedureEnum.ViewMaritimeReport);
            model.CanViewBilling = await access.HasAnyAccessAsync(userId, ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling,
                ProcedureEnum.DeleteBilling, ProcedureEnum.ApproveFinalBilling, ProcedureEnum.ViewMaritimeReport);
            model.CanViewCollections = await access.HasAnyAccessAsync(userId, ProcedureEnum.CreateCollection,
                ProcedureEnum.AccessTreasury, ProcedureEnum.ViewMaritimeReport);
            model.CanViewBanking = await access.HasAnyAccessAsync(userId, ProcedureEnum.AccessTreasury, ProcedureEnum.ViewMaritimeReport);
            model.CanReviewCalculations = await access.HasAnyAccessAsync(userId, ProcedureEnum.EditDispatchTicket,
                ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling, ProcedureEnum.ApproveFinalBilling,
                ProcedureEnum.ManageRates, ProcedureEnum.ApproveRateUpload, ProcedureEnum.ViewMaritimeReport);
            model.CanViewFinance = model.CanViewBilling || model.CanViewCollections;
        }

        public async Task LoadAsync(DashboardCountViewModel model, CancellationToken ct)
        {
            if (!model.ShowDashboard)
            {
                return;
            }

            var today = DateOnly.FromDateTime(model.UpdatedAt);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            try
            {
                if (model.CanApproveRates)
                {
                    model.RateUploadReviews = await _dbContext.MsapRateUploads.AsNoTracking()
                        .CountAsync(upload => upload.Company == MsapConstants.Company_MMSI && upload.SubmittedAt != null && upload.ApprovedAt == null, ct);
                }
                var bills = _dbContext.MsapBillings.AsNoTracking()
                    .Where(b => b.Company == MsapConstants.Company_MMSI && b.CanceledDate == null && b.VoidedDate == null);
                if (model.CanApproveSchedule)
                {
                    var reviews = _dbContext.MsapVesselSchedules.AsNoTracking()
                        .Where(s => s.Status == MsapConstants.VesselScheduleStatus.Tentative
                            && (s.CustomerId == null || s.Customer!.Company == MsapConstants.Company_MMSI));
                    model.ScheduleReviews = await reviews.CountAsync(ct);
                    model.ScheduleReapprovals = await reviews.CountAsync(s => s.ApprovedRevision != null, ct);
                    model.ScheduleReviewItems = await reviews.Include(s => s.Customer).Include(s => s.Vessel)
                        .OrderBy(s => s.CreatedDate).ThenBy(s => s.VesselScheduleId).Take(6).ToListAsync(ct);
                }
                if (model.CanApproveBilling)
                {
                    var reviews = bills.Where(b => b.Status == MsapConstants.BillingStatus.ForPosting);
                    model.ForPosting = await reviews.CountAsync(ct);
                    model.BillingReviewItems = await reviews.Include(b => b.Customer).Include(b => b.Vessel).Include(b => b.BafBilling)
                        .OrderBy(b => b.CreatedDate).ThenBy(b => b.MsapBillingId).Take(6).ToListAsync(ct);
                }

                var tickets = _dbContext.MsapDispatchTickets.AsNoTracking()
                    .Where(t => t.Customer.Company == MsapConstants.Company_MMSI
                        && t.Status != MsapConstants.DispatchTicketStatus.Deleted
                        && (t.CustomerOrderSlipId == null || t.CustomerOrderSlip!.Status != MsapConstants.CustomerOrderSlipStatus.Cancelled
                            && t.CustomerOrderSlip.Status != MsapConstants.CustomerOrderSlipStatus.Invalidated));
                if (model.CanFollowUpTickets)
                {
                    var missing = _dbContext.MsapVesselSchedules.AsNoTracking()
                        .Where(s => s.Customer!.Company == MsapConstants.Company_MMSI
                            && s.CustomerOrderSlipId != null && s.CustomerOrderSlip!.Status == MsapConstants.CustomerOrderSlipStatus.Open
                            && s.Status != MsapConstants.VesselScheduleStatus.Cancelled
                            && s.ApprovedRevision == s.Revision && s.PlannedStart <= model.UpdatedAt
                            && s.ServiceEntries.Where(e => !e.CancelledAt.HasValue).Any(e => e.DispatchTickets.Count(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted
                                && t.TugBoatId == e.TugboatId && t.ServiceId == e.ServiceId) != 1
                                || e.DispatchTickets.Any(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted
                                    && (t.DateLeft == null || t.TimeLeft == null || t.DateArrived == null || t.TimeArrived == null
                                        || t.DateArrived < t.DateLeft || t.DateArrived == t.DateLeft && t.TimeArrived <= t.TimeLeft))));
                    model.MissingTicketCount = await missing.CountAsync(ct);
                    model.MissingTicketSchedules = await missing.Include(s => s.Customer).Include(s => s.Vessel)
                        .Include(s => s.CustomerOrderSlip).Include(s => s.ServiceEntries)
                        .ThenInclude(e => e.DispatchTickets.Where(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted))
                        .AsSplitQuery().OrderBy(s => s.PlannedStart).ThenBy(s => s.VesselScheduleId).Take(6).ToListAsync(ct);
                    foreach (var schedule in model.MissingTicketSchedules)
                    {
                        model.CompleteTicketsBySchedule[schedule.VesselScheduleId] = schedule.ServiceEntries.Count(e => !e.CancelledAt.HasValue &&
                            e.DispatchTickets.Count(t => t.TugBoatId == e.TugboatId && t.ServiceId == e.ServiceId) == 1
                            && e.DispatchTickets.Any(t => t.TugBoatId == e.TugboatId && t.ServiceId == e.ServiceId
                                && t.DateLeft.HasValue && t.TimeLeft.HasValue && t.DateArrived.HasValue && t.TimeArrived.HasValue
                                && t.DateArrived.Value.ToDateTime(t.TimeArrived.Value) > t.DateLeft.Value.ToDateTime(t.TimeLeft.Value)));
                    }
                }
                if (model.CanViewBilling || model.CanViewCollections)
                {
                    var posted = bills.Where(b => b.Status == MsapConstants.BillingStatus.ForCollection || b.Status == MsapConstants.BillingStatus.Collected);
                    model.OutstandingBalance = await posted.SumAsync(b => (decimal?)b.Balance, ct) ?? 0m;
                    if (model.CanViewBilling)
                    {
                        model.BilledMonth = await posted.Where(b => b.Date >= monthStart && b.Date <= today)
                            .SumAsync(b => (decimal?)b.Amount, ct) ?? 0m;
                        // Pending calculations have no reliable amount.
                        model.KnownUnbilledCharges = await tickets.Where(t => t.BillingId == null && t.Status == MsapConstants.DispatchTicketStatus.ForBilling)
                            .SumAsync(t => (decimal?)t.TotalNetRevenue, ct) ?? 0m;
                    }
                }
                if (model.CanReviewCalculations)
                {
                    var pending = tickets.Where(t => t.BillingId == null && t.Status == MsapConstants.DispatchTicketStatus.CalculationPending);
                    model.PendingCalculationCount = await pending.Select(t => t.CustomerOrderSlipId).Where(id => id != null).Distinct().CountAsync(ct);
                    model.PendingCalculationTickets = await pending.Include(t => t.CustomerOrderSlip).Include(t => t.ExpectedServiceEntry)
                        .Include(t => t.Customer).Include(t => t.Terminal).Include(t => t.Service)
                        .OrderBy(t => t.CustomerOrderSlipId).ThenBy(t => t.DispatchTicketId).Take(6).ToListAsync(ct);
                }
                if (model.CanViewDispatch || model.CanViewReports)
                {
                    var activity = await tickets.Where(t => t.DateLeft >= monthStart && t.DateLeft <= today
                            && (t.Status == MsapConstants.DispatchTicketStatus.CalculationPending
                                || t.Status == MsapConstants.DispatchTicketStatus.ForBilling || t.Status == MsapConstants.DispatchTicketStatus.Billed)
                            && t.DateLeft != null && t.TimeLeft != null && t.DateArrived != null && t.TimeArrived != null
                            && (t.DateArrived > t.DateLeft || t.DateArrived == t.DateLeft && t.TimeArrived > t.TimeLeft))
                        .GroupBy(t => 1).Select(g => new { Dispatches = g.Count(), Vessels = g.Select(t => t.VesselId).Distinct().Count() })
                        .FirstOrDefaultAsync(ct);
                    model.DispatchesMonth = activity?.Dispatches ?? 0;
                    model.VesselsMonth = activity?.Vessels ?? 0;
                }
                if (model.CanViewCollections || model.CanViewBanking)
                {
                    var receipts = _dbContext.MsapCollections.AsNoTracking()
                        .Where(c => c.Company == MsapConstants.Company_MMSI && c.CanceledDate == null && c.VoidedDate == null);
                    if (model.CanViewCollections)
                    {
                        model.ReceivedMonth = await receipts.Where(c => c.Date >= monthStart && c.Date <= today)
                            .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
                    }
                    if (model.CanViewBanking)
                    {
                        var banking = await receipts.Where(c => c.ClearedDate == null)
                            .GroupBy(c => c.DepositDate != null ? "Clearing" : c.ReturnedDate != null ? "Returned" : "Deposit")
                            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(c => c.Amount) }).ToListAsync(ct);
                        foreach (var group in banking)
                        {
                            model.BankingCounts[group.Status] = group.Count;
                            model.BankingAmounts[group.Status] = group.Amount;
                        }
                    }
                }
                if (model.CanManageOtherTugCharges)
                {
                    var missingCosts = tickets.Where(t => !t.Tugboat.IsCompanyOwned && t.CustomerOrderSlipId != null
                        && t.DateLeft != null && t.TimeLeft != null && t.DateArrived != null && t.TimeArrived != null
                        && (t.DateArrived > t.DateLeft || t.DateArrived == t.DateLeft && t.TimeArrived > t.TimeLeft)
                        && t.OtherTugChargeLine == null);
                    model.MissingOtherTugTicketCount = await missingCosts.CountAsync(ct);
                    model.MissingOtherTugTickets = await missingCosts.Include(t => t.Tugboat).Include(t => t.CustomerOrderSlip)
                        .OrderBy(t => t.DateLeft).ThenBy(t => t.DispatchTicketId).Take(6).ToListAsync(ct);
                }
                if (model.CanManageOtherTugCharges || model.CanConfirmOtherTugCharges)
                {
                    var drafts = _dbContext.MsapOtherTugCharges.AsNoTracking().Where(c => c.ConfirmedAt == null
                        && c.Lines.Any(l => l.Ticket.Customer.Company == MsapConstants.Company_MMSI
                            && l.Ticket.CustomerOrderSlipId != null && l.Ticket.Status != MsapConstants.DispatchTicketStatus.Deleted
                            && l.Ticket.CustomerOrderSlip!.Status != MsapConstants.CustomerOrderSlipStatus.Cancelled
                            && l.Ticket.CustomerOrderSlip.Status != MsapConstants.CustomerOrderSlipStatus.Invalidated));
                    model.OtherTugChargeReviewCount = await drafts.CountAsync(ct);
                    model.OtherTugChargeReviews = await drafts.Include(c => c.Lines.Where(l => l.Ticket.Customer.Company == MsapConstants.Company_MMSI
                            && l.Ticket.CustomerOrderSlipId != null && l.Ticket.Status != MsapConstants.DispatchTicketStatus.Deleted
                            && l.Ticket.CustomerOrderSlip!.Status != MsapConstants.CustomerOrderSlipStatus.Cancelled
                            && l.Ticket.CustomerOrderSlip.Status != MsapConstants.CustomerOrderSlipStatus.Invalidated))
                        .ThenInclude(l => l.Ticket).OrderBy(c => c.UpdatedAt).ThenBy(c => c.OtherTugChargeId).Take(6).ToListAsync(ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to load dashboard workflow and financial data");
                model.DataError = "Workflow and financial data could not be loaded. Refresh to try again.";
            }

            if (!model.CanViewDispatch)
            {
                return;
            }

            try
            {
                var day = model.UpdatedAt.Date;
                var window = (await _scheduleService.GetSchedulesAsync(day, model.UpdatedAt.AddDays(7), ct))
                    .Where(s => s.Status != MsapConstants.VesselScheduleStatus.Cancelled
                        && (s.CustomerId == null || s.Customer?.Company == MsapConstants.Company_MMSI)).ToList();
                var schedules = window.Where(s => s.PlannedStart < day.AddDays(1) && s.PlannedEnd > day).ToList();
                model.UpcomingSchedules = window
                    .Where(s => s.PlannedStart >= model.UpdatedAt && s.PlannedStart < model.UpdatedAt.AddDays(7)
                        && s.Status != MsapConstants.VesselScheduleStatus.Completed)
                    .OrderBy(s => s.PlannedStart).ThenBy(s => s.VesselScheduleId).Take(5).ToList();
                var names = await _dbContext.MsapTugboats.AsNoTracking().ToDictionaryAsync(t => t.TugboatId, t => t.TugboatName, ct);
                foreach (var schedule in schedules.Concat(model.UpcomingSchedules).DistinctBy(s => s.VesselScheduleId))
                {
                    model.AssignedTugs[schedule.VesselScheduleId] =
                        (string.IsNullOrEmpty(schedule.AssignedTugboatIds) ? [] : JsonSerializer.Deserialize<List<int>>(schedule.AssignedTugboatIds) ?? [])
                        .Distinct().Count(names.ContainsKey);
                }

                // shortcut: pairwise checks for today's plans; index resource intervals if daily volume grows.
                foreach (var schedule in schedules.Where(s => s.Status != MsapConstants.VesselScheduleStatus.Completed))
                {
                    var conflicts = await _scheduleService.CheckConflictsAsync(schedule, ct, schedules, names);
                    if (conflicts.Any(c => c.ConflictStart < day.AddDays(1) && c.ConflictEnd > day))
                    {
                        model.ConflictingScheduleIds.Add(schedule.VesselScheduleId);
                    }
                }
                model.ScheduleCount = schedules.Count;
                model.ConfirmedToday = schedules.Count(s => s.Status != MsapConstants.VesselScheduleStatus.Tentative);
                model.NeedingTugs = schedules.Count(s => s.Status != MsapConstants.VesselScheduleStatus.Completed
                    && model.AssignedTugs[s.VesselScheduleId] < s.RequiredTugCount);
                model.DueBookings = schedules.Count(s => s.Status == MsapConstants.VesselScheduleStatus.Tentative);
                model.Schedules = schedules.OrderBy(s => s.Status == MsapConstants.VesselScheduleStatus.Completed)
                    .ThenBy(s => s.PlannedStart).Take(6).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to load dashboard vessel schedules");
                model.ScheduleError = "Vessel schedules could not be loaded. Open the calendar or refresh to try again.";
            }
        }
    }
}
