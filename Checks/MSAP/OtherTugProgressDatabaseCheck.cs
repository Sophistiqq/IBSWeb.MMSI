using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class OtherTugProgressDatabaseCheck
    {
        public static async Task RunAsync()
        {
            string connection = TugboatOwnerCheck.TestConnection();
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql(connection).Options);
            await using var shared = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var work = new UnitOfWork(db, shared);
            var job = await db.MsapCustomerOrderSlips.Where(j => j.DispatchTickets.Count >= 3
                    && db.MsapBillings.Any(b => b.CustomerOrderSlipId == j.CustomerOrderSlipId))
                .OrderByDescending(j => j.CustomerOrderSlipId).FirstAsync();
            int cosId = job.CustomerOrderSlipId;
            var external = await db.MsapTugboats.FirstAsync(t => !t.IsCompanyOwned && t.TugboatOwnerId != null);
            var tickets = await db.MsapDispatchTickets.Where(t => t.CustomerOrderSlipId == cosId)
                .Include(t => t.ExpectedServiceEntry).OrderBy(t => t.DispatchTicketId).Take(3).ToListAsync();
            Check(tickets.Count == 3 && tickets.All(t => t.VesselScheduleServiceEntryId.HasValue), "Use a clone with the billing-integrity fixture.");
            foreach (var ticket in tickets.Take(2))
            {
                ticket.TugBoatId = external.TugboatId;
                ticket.ExpectedServiceEntry!.TugboatId = external.TugboatId;
            }
            await db.SaveChangesAsync();
            var initial = await ProgressAsync();
            Check(initial.OtherTugTicketCount == 2 && initial.MissingOtherTugTicketCount == 2
                && initial.OtherTugChargeReviewCount == 0 && initial.MissingOtherTugTicketId == tickets[0].DispatchTicketId,
                "Missing external costs were overlooked or a company-owned ticket was counted.");
            int stage = initial.Stage;
            var dashboard = await DashboardAsync();
            int missingBefore = dashboard.MissingOtherTugTicketCount;
            int draftsBefore = dashboard.OtherTugChargeReviewCount;
            Check(missingBefore >= 2 && dashboard.MissingOtherTugTickets.Any(t => t.CustomerOrderSlipId == cosId),
                "Dashboard did not surface actionable billed tickets missing provider costs.");

            var charge = new OtherTugCharge
            {
                TugboatOwnerId = external.TugboatOwnerId!.Value, ProviderName = "Notifier fixture",
                ProviderReference = "NOTIFY-" + Guid.NewGuid().ToString("N"), DocumentDate = DateOnly.FromDateTime(DateTime.Today),
                PreparedByUserId = "notifier-check", PreparedByName = "Notifier check", UpdatedAt = DateTimeHelper.GetCurrentPhilippineTime(),
                Lines = [new OtherTugChargeLine { DispatchTicketId = tickets[0].DispatchTicketId, Amount = 0m }]
            };
            db.Add(charge);
            await db.SaveChangesAsync();
            var draft = await ProgressAsync();
            Check(draft.Stage == stage && draft.MissingOtherTugTicketCount == 1 && draft.OtherTugChargeReviewCount == 1
                && draft.ConfirmedOtherTugTicketCount == 0 && draft.OtherTugChargeReviewId == charge.OtherTugChargeId,
                "Draft zero cost was treated as confirmed, or provider follow-up blocked customer progress.");
            dashboard = await DashboardAsync();
            Check(dashboard.MissingOtherTugTicketCount == missingBefore - 1 && dashboard.OtherTugChargeReviewCount == draftsBefore + 1,
                "Dashboard did not move the recorded cost from missing input to confirmation.");
            db.Add(new OtherTugChargeLine { OtherTugChargeId = charge.OtherTugChargeId, DispatchTicketId = tickets[1].DispatchTicketId, Amount = 0m });
            await db.SaveChangesAsync();
            Check((await ProgressAsync()).OtherTugChargeReviewCount == 1, "A provider bill covering two tickets was counted twice.");
            charge.ConfirmedAt = DateTimeHelper.GetCurrentPhilippineTime();
            charge.ConfirmedByUserId = "notifier-check";
            charge.ConfirmedByName = "Notifier check";
            await db.SaveChangesAsync();
            var confirmed = await ProgressAsync();
            Check(confirmed.OtherTugTicketCount == 2 && confirmed.MissingOtherTugTicketCount == 0
                && confirmed.OtherTugChargeReviewCount == 0 && confirmed.ConfirmedOtherTugTicketCount == 2,
                "Confirmed zero allocations continued to appear as missing or awaiting review.");
            dashboard = await DashboardAsync();
            Check(dashboard.MissingOtherTugTicketCount == missingBefore - 2 && dashboard.OtherTugChargeReviewCount == draftsBefore,
                "Confirmed zero costs remained in the dashboard follow-up queues.");
            tickets[0].ExpectedServiceEntry!.CancelledAt = DateTimeHelper.GetCurrentPhilippineTime();
            tickets[0].ExpectedServiceEntry!.CancelledByUserId = "notifier-check";
            tickets[0].ExpectedServiceEntry!.CancellationReason = "Notifier exclusion fixture";
            await db.SaveChangesAsync();
            Check((await ProgressAsync()).OtherTugTicketCount == 1, "Cancelled expected services remained actionable provider-cost reminders.");
            tickets[1].Status = MsapConstants.DispatchTicketStatus.Deleted;
            await db.SaveChangesAsync();
            Check((await ProgressAsync()).OtherTugTicketCount == 0, "Deleted tickets remained in provider-cost follow-up.");
            job.Status = MsapConstants.CustomerOrderSlipStatus.Cancelled;
            await db.SaveChangesAsync();
            Check((await ProgressAsync()).OtherTugTicketCount == 0, "Cancelled COS exposed provider-cost actions.");
            await transaction.RollbackAsync();
            Console.WriteLine("PASS: AP Other Tugs progress/dashboard missing -> draft -> confirmed zero, distinct provider bills, billed-ticket visibility, owned/deleted/cancelled exclusions and unchanged customer progress (clone fixture rolled back).");

            async Task<JobProgressViewModel> ProgressAsync()
            {
                var schedule = await work.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == cosId, default);
                return await JobProgressCalculator.LoadAsync(work, job, schedule, default);
            }

            async Task<DashboardCountViewModel> DashboardAsync()
            {
                var model = new DashboardCountViewModel
                {
                    ShowDashboard = true, CanManageOtherTugCharges = true, CanConfirmOtherTugCharges = true,
                    UpdatedAt = DateTimeHelper.GetCurrentPhilippineTime()
                };
                await new DashboardService(db, null!, NullLogger<DashboardService>.Instance).LoadAsync(model, default);
                Check(model.DataError == null, "Provider-cost dashboard query failed: " + model.DataError);
                return model;
            }
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
