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

            var splitCreated = await billing.CreateBillingAsync(split, "Billing check", "MMSI", default);
            Check(splitCreated.IsSuccess, splitCreated.Message ?? "Split preparation failed.");
            int basicId = splitCreated.Data;
            int bafId = await db.MsapBillings.Where(x => x.BasicBillingId == basicId).Select(x => x.MsapBillingId).SingleAsync();
            Check((await billing.DeleteBillingAsync(bafId, "Billing check", default)).IsSuccess, "A split draft could not be discarded from its BAF bill.");
            db.ChangeTracker.Clear();
            Check(!await db.MsapBillings.AnyAsync(x => x.MsapBillingId == basicId || x.MsapBillingId == bafId)
                && await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId).AllAsync(x => x.BillingId == null && x.Status == MsapConstants.DispatchTicketStatus.ForBilling)
                && await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId).SumAsync(x => x.TotalNetRevenue) == total
                && await db.AuditTrails.CountAsync(x => x.DocumentType == "Billing" && (x.RecordId == basicId || x.RecordId == bafId) && x.Activity.StartsWith("Discarded draft billing #")) == 2,
                "Split discard lost actual charges, left a draft/ticket link or omitted an audit.");
            Check(await db.AuditTrails.AnyAsync(x => x.DocumentType == "COS" && x.RecordId == cosId
                && x.Activity.Contains("#" + number) && x.Activity.StartsWith("Discarded draft billing preparation")),
                "Discard disappeared from the COS timeline after removing the billing records.");

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

            Check((await billing.DeleteBillingAsync(id, "Billing check", default)).IsSuccess, "An unapproved single preparation could not be discarded.");
            db.ChangeTracker.Clear();
            Check(!await db.MsapBillings.AnyAsync(x => x.MsapBillingId == id)
                && await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId).AllAsync(x => x.BillingId == null),
                "Discard retained the single draft or its ticket links.");
            requested = new Billing
            {
                MsapBillingNumber = number, Date = split.Date, CustomerId = job.CustomerId,
                CustomerOrderSlipId = cosId, BilledTo = "LOCAL", COSNumber = "Forged COS", Amount = 99999m
            };
            created = await billing.CreateBillingAsync(requested, "Billing check", "MMSI", default);
            Check(created.IsSuccess, created.Message ?? "Discarded tickets/billing number could not be prepared again.");
            id = created.Data;
            db.ChangeTracker.Clear();
            saved = await db.MsapBillings.SingleAsync(x => x.MsapBillingId == id);
            int revision = saved.Revision;

            var closed = new MsapPostedPeriod { Year = saved.Date.Year, Month = saved.Date.Month, IsClosed = true, ClosedBy = "Billing check" };
            db.Add(closed);
            await db.SaveChangesAsync();
            Check(!(await billing.PostBillingAsync(id, "Billing check", default, actor, revision)).IsSuccess,
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
            Check(!(await billing.DeleteBillingAsync(id, "Billing check", default)).IsSuccess,
                "An approved billing was discarded.");
            db.ChangeTracker.Clear();
            saved = await db.MsapBillings.SingleAsync(x => x.MsapBillingId == id);
            Check(saved.ApprovedRevision == revision && saved.Status == MsapConstants.BillingStatus.ForCollection
                && await db.MsapSalesBooks.CountAsync(x => x.DocumentId == id && x.VoidedAt == null) == 1
                && await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId).AllAsync(x => x.BillingId == id && x.Status == MsapConstants.DispatchTicketStatus.Billed),
                "Rejected discard changed the approved billing, Sales Book or ticket links.");
            Check(sbma.OrderBy(x => x.Key).SequenceEqual((await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == cosId)
                    .ToDictionaryAsync(x => x.DispatchTicketId, x => x.SbmaShareAmount)).OrderBy(x => x.Key))
                && await shared.FilprideGeneralLedgerBooks.CountAsync() == ledgerCount,
                "Billing recalculated stored SBMA or introduced unapproved IBS GL recognition.");
            Console.WriteLine("PASS: billing uses saved ticket amounts/canonical COS; closed periods and denied/stale/repeated approvals rejected; single/split discard and reprepare, actual Sales Book posting, approved-bill retention and immutable SBMA verified.");
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
