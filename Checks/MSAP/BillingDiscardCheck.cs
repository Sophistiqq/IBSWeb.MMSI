using System.Linq.Expressions;
using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class BillingDiscardCheck
    {
        public static async Task RunAsync()
        {
            (string Name, Action<Billing> Change)[] blockers =
            [
                ("approved status", b => b.Status = MsapConstants.BillingStatus.ForCollection),
                ("collected status", b => b.Status = MsapConstants.BillingStatus.Collected),
                ("approved revision", b => b.ApprovedRevision = 1),
                ("approval date", b => b.ApprovedAt = DateTime.Now),
                ("approver", b => b.ApprovedByUserId = "approver"),
                ("historical reversal date", b => b.UnpostedDate = DateTime.UtcNow),
                ("historical reversal actor", b => b.UnpostedBy = "legacy"),
                ("payment", b => b.AmountPaid = 1),
                ("paid flag", b => b.IsPaid = true),
                ("collection", b => b.CollectionId = 1)
            ];
            foreach ((string name, Action<Billing> change) in blockers)
            {
                foreach (bool split in new[] { false, true })
                {
                    var fixture = new Fixture(split);
                    change(fixture.Baf ?? fixture.Basic);
                    await fixture.AssertHiddenAsync();
                    await fixture.AssertRejectedAsync(name);
                }
            }
            foreach (int id in new[] { 1, 2 })
            {
                var fixture = new Fixture(true);
                fixture.History.Add(id);
                await fixture.AssertHiddenAsync();
                await fixture.AssertRejectedAsync("Sales Book history");
            }
            var closed = new Fixture(true) { Closed = true };
            await closed.AssertRejectedAsync("closed period");
            var changed = new Fixture(true) { ApproveOnLock = true };
            await changed.AssertRejectedAsync("approval while waiting for locks");
            var missing = new Fixture(false) { MissingOnLock = true };
            await missing.AssertRejectedAsync("concurrently removed draft");
            foreach (bool split in new[] { false, true })
            {
                foreach (bool selectBaf in split ? new[] { false, true } : new[] { false })
                {
                    var fixture = new Fixture(split);
                    var (rows, _, _) = await fixture.Service.GetPagedBillingsAsync(new DataTablesParameters(), default);
                    Check(rows.All(b => b.CanDiscard), "Eligible drafts were hidden.");
                    ServiceResult result = await fixture.Service.DeleteBillingAsync(selectBaf ? 2 : 1, "discard-check", default);
                    Check(result.IsSuccess, result.Message ?? "Discard failed.");
                    Check(fixture.Removed.SequenceEqual(split ? new[] { 2, 1 } : new[] { 1 }), "A split preparation was not removed together, BAF first.");
                    Check(fixture.Locks.SequenceEqual(split ? new[] { 1, 2 } : new[] { 1 }), "Billing locks are not taken in approval order.");
                    Check(fixture.Ticket.BillingId == null && fixture.Ticket.Billing == null
                        && fixture.Ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling
                        && fixture.Ticket.TotalNetRevenue == 42m && fixture.Ticket.SbmaShareAmount == 7m
                        && fixture.Ticket.CustomerOrderSlipId == 10, "Discard changed actual service charges or retained a billing link.");
                    Check(fixture.Job.Status == MsapConstants.CustomerOrderSlipStatus.Open, "Discard did not reopen the COS for preparation.");
                    Check(fixture.Audits.Count(a => a.DocumentType == "Billing" && a.Activity.StartsWith("Discarded draft billing #", StringComparison.Ordinal)) == fixture.Removed.Count
                        && fixture.Audits.Count(a => a.DocumentType == "COS" && a.RecordId == 10 && a.Activity.Contains("#BASIC", StringComparison.Ordinal)) == 1
                        && fixture.Saves == 1 && fixture.Transactions == 1, "Discard did not audit each draft and retain a COS timeline entry in one transaction.");
                }
            }
            Console.WriteLine("PASS: draft discard eligibility, split partners, history, approval evidence, payments, closed periods, concurrent changes, ticket/charge retention, lock order and audits.");
        }

        private sealed class Fixture
        {
            public Billing Basic { get; } = new() { MsapBillingId = 1, MsapBillingNumber = "BASIC", Status = MsapConstants.BillingStatus.ForPosting, Date = new DateOnly(2026, 1, 1), CustomerOrderSlipId = 10 };
            public Billing? Baf { get; }
            public DispatchTicket Ticket { get; } = new() { BillingId = 1, Status = MsapConstants.DispatchTicketStatus.ForBilling, CustomerOrderSlipId = 10, TotalNetRevenue = 42m, SbmaShareAmount = 7m };
            public CustomerOrderSlip Job { get; } = new() { CustomerOrderSlipId = 10, Status = MsapConstants.CustomerOrderSlipStatus.Closed };
            public List<int> History { get; } = [];
            public List<int> Removed { get; } = [];
            public List<int> Locks { get; } = [];
            public List<AuditTrail> Audits { get; } = [];
            public bool Closed { get; init; }
            public bool ApproveOnLock { get; init; }
            public bool MissingOnLock { get; init; }
            public int Saves { get; private set; }
            public int Transactions { get; private set; }
            public BillingService Service { get; }

            public Fixture(bool split)
            {
                Ticket.Billing = Basic;
                if (split)
                {
                    Baf = new Billing { MsapBillingId = 2, MsapBillingNumber = "BAF", Status = Basic.Status, Date = Basic.Date, BasicBillingId = 1, BasicBilling = Basic };
                    Basic.BafBilling = Baf;
                }
                List<Billing> bills = Baf == null ? [Basic] : [Basic, Baf];
                var billings = Proxy<IBillingRepository>((method, args) =>
                {
                    switch (method.Name)
                    {
                        case "GetAsync":
                            return Task.FromResult(bills.FirstOrDefault(((Expression<Func<Billing, bool>>)args![0]!).Compile()));
                        case "GetForUpdateAsync":
                            int id = (int)args![0]!;
                            Locks.Add(id);
                            if (ApproveOnLock && id == 2)
                            {
                                Baf!.Status = MsapConstants.BillingStatus.ForCollection;
                            }
                            return Task.FromResult(MissingOnLock ? null : bills.Single(b => b.MsapBillingId == id));
                        case "GetBillingIdsWithSalesBookAsync":
                            return Task.FromResult(History.Where(((List<int>)args![0]!).Contains).ToList());
                        case "RemoveAsync":
                            Removed.Add(((Billing)args![0]!).MsapBillingId);
                            return Task.CompletedTask;
                        case "GetPagedBillingsAsync":
                            return Task.FromResult(((IEnumerable<Billing>)bills, bills.Count, bills.Count));
                        default:
                            throw new NotSupportedException(method.Name);
                    }
                });
                var tickets = Proxy<IDispatchTicketRepository>((_, _) => Task.FromResult<IEnumerable<DispatchTicket>>([Ticket]));
                var periods = Proxy<IPostedPeriodRepository>((_, _) => Task.FromResult(Closed ? new MsapPostedPeriod { IsClosed = true } : null));
                var jobs = Proxy<ICustomerOrderSlipRepository>((_, _) => Task.FromResult<CustomerOrderSlip?>(Job));
                var audit = Proxy<IAuditTrailRepository>((_, args) => { Audits.Add((AuditTrail)args![0]!); return Task.CompletedTask; });
                var work = Proxy<IUnitOfWork>((method, args) =>
                {
                    switch (method.Name)
                    {
                        case "get_Billing":
                            return billings;
                        case "get_DispatchTicket":
                            return tickets;
                        case "get_PostedPeriod":
                            return periods;
                        case "get_CustomerOrderSlip":
                            return jobs;
                        case "get_AuditTrail":
                            return audit;
                        case "ExecuteInTransactionAsync":
                            Transactions++;
                            return ((Func<Task>)args![0]!)();
                        case "SaveAsync":
                            Saves++;
                            return Task.CompletedTask;
                        default:
                            throw new NotSupportedException(method.Name);
                    }
                });
                Service = new BillingService(work, null!, NullLogger<BillingService>.Instance, null!);
            }

            public async Task AssertHiddenAsync()
            {
                var (rows, _, _) = await Service.GetPagedBillingsAsync(new DataTablesParameters(), default);
                Check(rows.All(b => !b.CanDiscard), "An ineligible preparation still offers discard.");
            }

            public async Task AssertRejectedAsync(string reason)
            {
                Check(!(await Service.DeleteBillingAsync(Baf?.MsapBillingId ?? 1, "discard-check", default)).IsSuccess, $"Discard accepted {reason}.");
                Check(Removed.Count == 0 && Audits.Count == 0 && Saves == 0 && Ticket.BillingId == 1 && Ticket.Billing == Basic
                    && Ticket.TotalNetRevenue == 42m && Ticket.SbmaShareAmount == 7m && Job.Status == MsapConstants.CustomerOrderSlipStatus.Closed,
                    $"Rejected discard changed records: {reason}.");
            }
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
