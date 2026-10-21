using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class BillingIntegrityDatabaseCheck
    {
        public static async Task RunAsync(MsapDbContext db, ApplicationDbContext shared, IUnitOfWork work,
            DispatchTicketService dispatch, int cosId, int unperformedServiceId)
        {
            db.ChangeTracker.Clear();
            string actor = await shared.Users.Where(x => x.IsActive && !string.IsNullOrEmpty(x.Name)).OrderBy(x => x.Id)
                .Select(x => x.Id).FirstAsync();
            var cosService = new CustomerOrderSlipService(work, NullLogger<CustomerOrderSlipService>.Instance);
            var billing = new BillingService(work, cosService, NullLogger<BillingService>.Instance, new ActorAccess(actor));
            var cancelled = await cosService.CancelServiceAsync(cosId, unperformedServiceId,
                "Unperformed service in billing test fixture", "Billing check", actor, default);
            Check(cancelled.IsSuccess, cancelled.Message ?? "Could not cancel the unperformed fixture service.");
            var job = await db.MsapCustomerOrderSlips.SingleAsync(x => x.CustomerOrderSlipId == cosId);
            List<DispatchTicket> tickets = await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId).ToListAsync();
            Dictionary<int, decimal> sbma = tickets.ToDictionary(x => x.DispatchTicketId, x => x.SbmaShareAmount);
            decimal total = tickets.Sum(x => x.TotalNetRevenue);
            Check(total == 110m, "Billing fixture did not retain its approved nonzero and zero ticket amounts.");
            int ledgerCount = await shared.FilprideGeneralLedgerBooks.CountAsync();
            var header = await billing.GetDispatchTicketsByCustomerOrderSlipAsync(cosId, default);
            Check(header.IsSuccess && header.Data!.Header.COSNumber == job.CustomerOrderSlipNumber,
                "Billing preview still exposes the legacy COS text.");

            string number = "B" + Guid.NewGuid().ToString("N")[..9];
            var split = new Billing
            {
                MsapBillingNumber = number, BafBillingNumber = "F" + number[1..], SeparateBaf = true,
                Date = DateOnly.FromDateTime(DateTime.Today), CustomerId = job.CustomerId,
                CustomerOrderSlipId = cosId, BilledTo = "LOCAL", COSNumber = "Forged COS"
            };
            var preview = await billing.PrepareBillingAsync(split, "Billing check", "MMSI", default);
            Check(preview.IsSuccess && preview.Data!.Count == 2 && preview.Data.Sum(x => x.Amount) == total
                && preview.Data.All(x => x.COSNumber == job.CustomerOrderSlipNumber)
                && tickets.All(x => x.BillingId == null), "Split preparation lost canonical COS, duplicated amounts or changed actual tickets.");

            var requested = new Billing
            {
                MsapBillingNumber = number, Date = split.Date, CustomerId = job.CustomerId,
                CustomerOrderSlipId = cosId, BilledTo = "LOCAL", COSNumber = "Forged COS", Amount = 99999m
            };
            var created = await billing.CreateBillingAsync(requested, "Billing check", "MMSI", default);
            Check(created.IsSuccess, created.Message ?? "Billing creation failed.");
            int id = created.Data;
            db.ChangeTracker.Clear();
            Billing saved = await db.MsapBillings.SingleAsync(x => x.MsapBillingId == id);
            Check(saved.COSNumber == job.CustomerOrderSlipNumber && saved.Amount == total && saved.Balance == total,
                "Billing creation trusted submitted COS/amounts instead of saved actual tickets.");

            Billing Edit()
            {
                return new Billing
                {
                    MsapBillingId = id, Date = saved.Date, CustomerId = job.CustomerId, CustomerOrderSlipId = cosId,
                    BilledTo = "LOCAL", COSNumber = "Forged COS", VesselId = int.MaxValue,
                    PortId = int.MaxValue, TerminalId = int.MaxValue, VoyageNumber = "Forged voyage",
                    ToBillDispatchTickets = tickets.Select(x => x.DispatchTicketId.ToString()).ToList()
                };
            }
            var undated = Edit();
            undated.Date = default;
            Check(!(await billing.UpdateBillingAsync(undated, "Billing check", default)).IsSuccess, "A billing edit without a date was accepted.");
            var partial = Edit();
            partial.ToBillDispatchTickets = [tickets[0].DispatchTicketId.ToString()];
            Check(!(await billing.UpdateBillingAsync(partial, "Billing check", default)).IsSuccess, "Partial billing edit was accepted.");
            var moved = Edit();
            moved.CustomerOrderSlipId = int.MaxValue;
            Check(!(await billing.UpdateBillingAsync(moved, "Billing check", default)).IsSuccess, "Billing moved to a supplied COS.");
            var invalidPrincipal = Edit();
            invalidPrincipal.BilledTo = "PRINCIPAL";
            invalidPrincipal.PrincipalId = int.MaxValue;
            Check(!(await billing.UpdateBillingAsync(invalidPrincipal, "Billing check", default)).IsSuccess, "An unrelated or nonexistent principal was accepted.");
            var valid = Edit();
            valid.Date = saved.Date.AddDays(1);
            Check((await billing.UpdateBillingAsync(valid, "Billing check", default)).IsSuccess, "A valid whole-COS billing edit failed.");
            db.ChangeTracker.Clear();
            saved = await db.MsapBillings.SingleAsync(x => x.MsapBillingId == id);
            Check(saved.COSNumber == job.CustomerOrderSlipNumber && saved.VesselId == job.VesselId && saved.PortId == job.PortId
                && saved.TerminalId == job.TerminalId && saved.VoyageNumber == job.VoyageNumber && saved.Amount == total,
                "Billing edit accepted supplied booking identity or changed saved amounts.");
            string terms = (await db.Customers.SingleAsync(x => x.CustomerId == job.CustomerId)).CustomerTerms ?? "COD";
            Check(saved.Terms == terms && saved.DueDate == await work.Terms.ComputeDueDateAsync(terms, saved.Date, default),
                "Billing edit left stale terms or due date after changing its billing date.");
            int revision = saved.Revision;
            var changedBaf = Edit();
            changedBaf.BafRates = new Dictionary<int, decimal> { [tickets[0].DispatchTicketId] = 999m };
            Check(!(await billing.UpdateBillingAsync(changedBaf, "Billing check", default)).IsSuccess,
                "Billing edited charges from an approved scheduled ticket.");
            Check(await db.MsapBillings.Where(x => x.MsapBillingId == id).Select(x => x.Revision).SingleAsync() == revision,
                "Rejected billing changes partially committed a revision.");

            var closed = new MsapPostedPeriod { Year = saved.Date.Year, Month = saved.Date.Month, IsClosed = true, ClosedBy = "Billing check" };
            db.Add(closed);
            await db.SaveChangesAsync();
            Check(!(await billing.UpdateBillingAsync(Edit(), "Billing check", default)).IsSuccess
                && !(await billing.PostBillingAsync(id, "Billing check", default, actor, revision)).IsSuccess,
                "Billing ignored an actual closed-period record.");
            db.ChangeTracker.Clear();
            db.Remove(await db.MsapPostedPeriods.SingleAsync(x => x.Id == closed.Id));
            await db.SaveChangesAsync();
            Check(!(await billing.PostBillingAsync(id, "Billing check", default, "denied", revision)).IsSuccess,
                "Unauthorized final approval was accepted.");
            Check(!(await billing.PostBillingAsync(id, "Billing check", default, actor, revision + 1)).IsSuccess,
                "A stale final billing review was accepted.");
            Check(!await db.MsapSalesBooks.AnyAsync(x => x.DocumentId == id), "A rejected approval created a Sales Book snapshot.");
            var posted = await billing.PostBillingAsync(id, "Billing check", default, actor, revision);
            Check(posted.IsSuccess, posted.Message ?? "Final approval failed.");
            db.ChangeTracker.Clear();
            saved = await db.MsapBillings.SingleAsync(x => x.MsapBillingId == id);
            var book = await db.MsapSalesBooks.SingleAsync(x => x.DocumentId == id && x.VoidedAt == null);
            Check(BillingService.CanPrint(saved) && book.Amount == total && book.BillingRevision == revision
                && book.ApprovedByUserId == actor && book.BillingDate == saved.Date
                && await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId).AllAsync(x => x.Status == MsapConstants.DispatchTicketStatus.Billed),
                "Final approval did not atomically persist its book/approval/ticket state.");
            Check(!(await billing.PostBillingAsync(id, "Billing check", default, actor, revision)).IsSuccess
                && await db.MsapSalesBooks.CountAsync(x => x.DocumentId == id) == 1, "Repeated approval duplicated the Sales Book.");
            var ticketEdit = new DispatchTicketViewModel();
            ticketEdit.FromEntity(tickets[0]);
            Check(!(await dispatch.UpdateDispatchTicketAsync(ticketEdit, null, null, "Billing check", default)).IsSuccess,
                "A billed ticket was editable.");
            var reversed = await billing.ReverseBillingAsync(id, "Billing check", "Fixture correction", default);
            Check(reversed.IsSuccess, reversed.Message ?? "Billing reversal failed.");
            db.ChangeTracker.Clear();
            saved = await db.MsapBillings.SingleAsync(x => x.MsapBillingId == id);
            Check(saved.Revision == revision + 1 && saved.ApprovedRevision == null && saved.UnpostedDate?.Kind == DateTimeKind.Utc
                && await db.MsapSalesBooks.AnyAsync(x => x.DocumentId == id && x.VoidedAt != null),
                "Reversal lost its historical snapshot or retained final approval.");
            Check((await billing.PostBillingAsync(id, "Billing check", default, actor, saved.Revision)).IsSuccess,
                "Corrected revision could not be approved again.");
            Check(await db.MsapSalesBooks.CountAsync(x => x.DocumentId == id) == 2
                && await db.MsapSalesBooks.CountAsync(x => x.DocumentId == id && x.VoidedAt == null) == 1,
                "Reapproval did not preserve one historical and one active snapshot.");
            Check(sbma.OrderBy(x => x.Key).SequenceEqual((await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId)
                    .ToDictionaryAsync(x => x.DispatchTicketId, x => x.SbmaShareAmount)).OrderBy(x => x.Key))
                && await shared.FilprideGeneralLedgerBooks.CountAsync() == ledgerCount,
                "Billing recalculated stored SBMA or introduced unapproved IBS GL recognition.");
            Console.WriteLine("PASS: billing uses saved ticket amounts/canonical COS; partial/moved/forged edits, charge overrides, closed periods, denied/stale/repeated approvals rejected; actual Sales Book posting, reversal/reapproval and immutable SBMA verified.");
            Console.WriteLine($"Billing browser fixture: COS {cosId}, bill {id}, number {number} (test database only).");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class ActorAccess : IAccessControlService
        {
            private readonly string _actor;

            public ActorAccess(string actor)
            {
                _actor = actor;
            }

            public Task<bool> HasAccessAsync(string userId, params ProcedureEnum[] procedures)
            {
                return Task.FromResult(userId == _actor);
            }

            public Task<bool> HasAnyAccessAsync(string userId, params ProcedureEnum[] procedures)
            {
                return HasAccessAsync(userId, procedures);
            }
        }
    }
}
