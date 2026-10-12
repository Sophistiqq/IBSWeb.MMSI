using System.Security.Claims;
using System.Text.Json;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Checks.MSAP
{
    internal static class SchedulingCheck
    {
        public static async Task RunAsync()
        {
            foreach (var method in typeof(IBSWeb.Areas.MSAP.Controllers.VesselScheduleController).GetMethods()
                .Where(m => m.Name is "Confirm" or "Edit" or "Cancel" or "Complete")
                .Concat(typeof(IBSWeb.Areas.MSAP.Controllers.JobOrderController).GetMethods()
                    .Where(m => m.Name is "Cancel" or "CompleteBooking")))
            {
                var filter = method.GetCustomAttributes(typeof(RequireAccessAttribute), true).Cast<RequireAccessAttribute>().Single();
                var permission = method.Name == "Confirm" ? ProcedureEnum.CreateJobOrder : ProcedureEnum.EditJobOrder;
                foreach (bool allowed in new[] { false, true })
                {
                    using var provider = new ServiceCollection().AddSingleton<IAccessControlService>(new CheckAccess(allowed ? permission : null)).BuildServiceProvider();
                    var http = new DefaultHttpContext { RequestServices = provider, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "check")], "check")) };
                    http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
                    var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
                    await filter.OnAuthorizationAsync(context);
                    Check(allowed ? context.Result == null : context.Result is JsonResult, $"{method.Name} ignored its Job Order permission.");
                }
                if (method.IsDefined(typeof(HttpPostAttribute), true))
                {
                    Check(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), $"{method.Name} has no anti-forgery protection.");
                }
            }
            var connection = Environment.GetEnvironmentVariable("MSAP_SCHEDULING_CHECK_CONNECTION")
                ?? throw new InvalidOperationException("Set MSAP_SCHEDULING_CHECK_CONNECTION to an isolated PostgreSQL server.");
            var settings = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres", Pooling = false };
            await using var server = new NpgsqlConnection(settings.ConnectionString);
            await server.OpenAsync();
            var database = "msap_schedule_check_" + Guid.NewGuid().ToString("N");
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", server))
            {
                await create.ExecuteNonQueryAsync();
            }
            try
            {
                settings.Database = database;
                var options = new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql(settings.ConnectionString,
                    pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options;
                await using var db = new MsapDbContext(options);
                await using var shared = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(settings.ConnectionString).Options);
                // MSAP notifications reference the shared directory, which these workflow checks do not use.
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE public.\"AspNetUsers\" (id text PRIMARY KEY)");
                await db.Database.MigrateAsync();
                Check(!db.Database.HasPendingModelChanges(), "Scheduling migration does not match the model.");
                var customer = new Customer { CustomerCode = "SCH01", CustomerName = "Schedule Check", CustomerAddress = "Check Address", CustomerTin = "000-000-000-00000", CustomerTerms = "COD", CustomerType = "Regular", VatType = "Vatable", ZipCode = "1000", Company = "MMSI" };
                var vessel = new Vessel { VesselNumber = "0001", VesselName = "Check Vessel", VesselType = "LOCAL" };
                var port = new Port { PortNumber = "001", PortName = "Check Port" };
                var terminal = new Terminal { TerminalNumber = "001", TerminalName = "Check Terminal", Port = port };
                var tug = new Tugboat { TugboatNumber = "001", TugboatName = "Check Tug", Port = port, IsCompanyOwned = true };
                var serviceType = new Service { ServiceNumber = "001", ServiceName = "Check Service" };
                db.AddRange(customer, vessel, port, terminal, tug, serviceType);
                await db.SaveChangesAsync();
                var work = new UnitOfWork(db, shared);
                var jobs = new JobOrderService(work, NullLogger<JobOrderService>.Instance);
                var schedules = new VesselScheduleService(work, jobs, NullLogger<VesselScheduleService>.Instance);
                VesselSchedule Booking(int day) => new()
                {
                    CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId,
                    PlannedStart = new DateTime(2030, 1, day, 8, 0, 0), PlannedEnd = new DateTime(2030, 1, day, 10, 0, 0),
                    AssignedTugboatIds = JsonSerializer.Serialize(new[] { tug.TugboatId }), VoyageNumber = "CHECK-VOYAGE", Notes = "Planning note"
                };
                async Task<VesselSchedule> Reload(int id)
                {
                    db.ChangeTracker.Clear();
                    return await db.MsapVesselSchedules.SingleAsync(s => s.VesselScheduleId == id);
                }
                var booking = Booking(1);
                booking.Status = MsapConstants.VesselScheduleStatus.Confirmed;
                booking.JobOrderId = 999;
                var saved = await schedules.CreateAsync(booking, "check");
                Check(saved.IsSuccess, saved.Message!);
                booking = await Reload(saved.Data);
                Check(booking.Status == MsapConstants.VesselScheduleStatus.Tentative && booking.JobOrderId == null && !await db.MsapJobOrders.AnyAsync(), "Saving a schedule bypassed confirmation.");
                var reviewedAt = booking.CreatedDate;
                var stale = await schedules.ConfirmAsync(booking.VesselScheduleId, reviewedAt.AddMinutes(-1), "check");
                Check(!stale.IsSuccess && !await db.MsapJobOrders.AnyAsync(), "A stale booking review created an order.");
                var closed = new MsapPostedPeriod { Year = 2030, Month = 1, IsClosed = true };
                db.MsapPostedPeriods.Add(closed);
                await db.SaveChangesAsync();
                Check(!(await schedules.ConfirmAsync(booking.VesselScheduleId, reviewedAt, "check")).IsSuccess, "Closed period confirmation succeeded.");
                booking = await Reload(booking.VesselScheduleId);
                Check(booking.Status == MsapConstants.VesselScheduleStatus.Tentative && booking.JobOrderId == null && !await db.MsapJobOrders.AnyAsync(), "Failed confirmation did not roll back.");
                db.Remove(await db.MsapPostedPeriods.SingleAsync());
                await db.SaveChangesAsync();
                var overlap = Booking(1);
                Check((await schedules.CreateAsync(overlap, "check", allowConflicts: true)).IsSuccess, "Could not prepare overlap check.");
                Check(!(await schedules.ConfirmAsync(booking.VesselScheduleId, reviewedAt, "check")).IsSuccess, "Confirmation ignored overlaps.");
                Check((await schedules.ChangeStatusAsync(overlap.VesselScheduleId, MsapConstants.VesselScheduleStatus.Cancelled, "check")).IsSuccess, "Cancellation failed.");
                var confirmed = await schedules.ConfirmAsync(booking.VesselScheduleId, reviewedAt, "check");
                Check(confirmed.IsSuccess, confirmed.Message!);
                var order = await db.MsapJobOrders.SingleAsync();
                Check(order.Status == MsapConstants.JobOrderStatus.Open && order.CustomerId == customer.CustomerId
                    && order.Date == DateOnly.FromDateTime(booking.PlannedStart) && order.PlannedStartTime == booking.PlannedStart
                    && order.PlannedEndTime == booking.PlannedEnd && order.PreferredTugboatId == tug.TugboatId && order.VoyageNumber == "CHECK-VOYAGE", "Confirmation did not copy the booking to the Job Order.");
                Check((await schedules.ConfirmAsync(booking.VesselScheduleId, reviewedAt, "check")).Data == order.JobOrderId
                    && await db.MsapJobOrders.CountAsync() == 1, "Repeated confirmation created another order.");
                var ticket = new DispatchTicket
                {
                    DispatchNumber = "CHECK-DT", Date = order.Date, JobOrderId = order.JobOrderId, CustomerId = customer.CustomerId,
                    VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId, TugBoatId = tug.TugboatId,
                    ServiceId = serviceType.ServiceId, Status = MsapConstants.DispatchTicketStatus.ForTariff, CreatedBy = "check",
                    DateLeft = order.Date, DateArrived = order.Date, TimeLeft = new TimeOnly(8, 15), TimeArrived = new TimeOnly(9, 15)
                };
                db.Add(ticket);
                await db.SaveChangesAsync();
                var progressDispatcher = new DispatchTicketService(work, null!, NullLogger<DispatchTicketService>.Instance);
                var progressBilling = new BillingService(work, jobs, NullLogger<BillingService>.Instance);
                var progressCollection = new CollectionService(work, NullLogger<CollectionService>.Instance);
                var unfinished = new DispatchTicket
                {
                    DispatchNumber = "CHECK-NEXT", Date = order.Date, JobOrderId = order.JobOrderId, CustomerId = customer.CustomerId,
                    VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId, TugBoatId = tug.TugboatId,
                    ServiceId = serviceType.ServiceId, Status = MsapConstants.DispatchTicketStatus.ForTariff, CreatedBy = "check",
                    DateLeft = order.Date, DateArrived = order.Date, TimeLeft = new TimeOnly(8, 15), TimeArrived = new TimeOnly(9, 15)
                };
                ticket.Status = MsapConstants.DispatchTicketStatus.ForApproval;
                db.Add(unfinished);
                await db.SaveChangesAsync();
                Check(!(await progressDispatcher.ApproveTariffAsync(ticket.DispatchTicketId, "check", default)).IsSuccess, "Individual approval bypassed unfinished tariff on another ticket.");
                Check(!(await progressDispatcher.BatchApproveTariffAsync([ticket.DispatchTicketId], "check", default)).IsSuccess, "Batch approval bypassed unfinished tariff on another ticket.");
                ticket.Status = MsapConstants.DispatchTicketStatus.Disapproved;
                await db.SaveChangesAsync();
                Check(!(await progressBilling.CreateBillingAsync(new Billing { Date = order.Date, JobOrderId = order.JobOrderId }, "check", "MMSI", default)).IsSuccess, "Billing ignored disapproved tickets.");
                var splitRequest = new Billing
                {
                    MsapBillingNumber = "CHECK-S1", Date = order.Date, JobOrderId = order.JobOrderId,
                    CustomerId = customer.CustomerId, ToBillDispatchTickets = [ticket.DispatchTicketId.ToString()]
                };
                Check(!(await progressBilling.CreatePhilCebSplitAsync(splitRequest, "CHECK-S2", "check", "MMSI", default)).IsSuccess, "Split billing bypassed ticket readiness.");
                Check((await progressDispatcher.SaveTariffAsync(new DispatchTicket { DispatchTicketId = ticket.DispatchTicketId, CustomerId = customer.CustomerId, DispatchRate = 100, BAFRate = 10 }, "Per hour", "Per hour", "check", true, default)).IsSuccess, "Disapproved tariff could not be corrected for resubmission.");
                ticket.Status = unfinished.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                var progressBill = new Billing
                {
                    MsapBillingNumber = "CHECK-P", Date = order.Date, Status = MsapConstants.BillingStatus.ForPosting,
                    BilledTo = "LOCAL", CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId,
                    TerminalId = terminal.TerminalId, CreatedBy = "check", Amount = 100, Balance = 100
                };
                db.Add(progressBill);
                ticket.Billing = progressBill;
                await db.SaveChangesAsync();
                var bafOnlyBill = new Billing
                {
                    MsapBillingNumber = "CHECK-BAF", Date = order.Date, Status = MsapConstants.BillingStatus.ForPosting,
                    BilledTo = "LOCAL", CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId,
                    TerminalId = terminal.TerminalId, CreatedBy = "check", JobOrderId = order.JobOrderId, Amount = 25, Balance = 25
                };
                var unrelatedBill = new Billing
                {
                    MsapBillingNumber = "CHECK-O", Date = order.Date, Status = MsapConstants.BillingStatus.ForPosting,
                    BilledTo = "LOCAL", CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId,
                    TerminalId = terminal.TerminalId, CreatedBy = "check", Amount = 50, Balance = 50
                };
                db.AddRange(bafOnlyBill, unrelatedBill);
                await db.SaveChangesAsync();
                var paging = new DataTablesParameters
                {
                    Start = 0, Length = 10, Search = new DataTablesSearch { Value = "" },
                    Columns = [new DataTablesColumn { Data = "status", Search = new DataTablesSearch { Value = "For Posting" } }]
                };
                var scoped = await progressBilling.GetPagedBillingsAsync(paging, default, order.JobOrderId);
                Check(scoped.RecordsFiltered == 2 && scoped.TotalRecords == 2 && scoped.Data.All(b => b.MsapBillingId != unrelatedBill.MsapBillingId), "Scoped posting must include ticket-linked and BAF-only bills and exclude unrelated bills.");
                var financialProgress = await JobProgressCalculator.LoadForBillingsAsync(work, [progressBill.MsapBillingId, bafOnlyBill.MsapBillingId], default);
                Check(financialProgress.Count == 1 && financialProgress[0].JobOrderId == order.JobOrderId && financialProgress[0].Billings.Count == 0, "Financial progress must deduplicate linked jobs and show the earliest unfinished stage.");
                bafOnlyBill.Status = unrelatedBill.Status = MsapConstants.BillingStatus.ForCollection;
                await db.SaveChangesAsync();
                var collectibles = await progressCollection.GetUncollectedBillingsForTableAsync(customer.CustomerId, null, default, order.JobOrderId);
                string collectibleJson = JsonSerializer.Serialize(collectibles.Data);
                Check(collectibleJson.Contains("CHECK-BAF", StringComparison.Ordinal) && !collectibleJson.Contains("CHECK-O", StringComparison.Ordinal), "Guided collection must include the split BAF bill and exclude other jobs.");
                db.RemoveRange(bafOnlyBill, unrelatedBill);
                await db.SaveChangesAsync();
                Console.WriteLine("PASS: financial navigation deduplicates jobs, filters ticket-linked/split billings, matches Posting status and scopes collection.");
                Check(!(await progressBilling.PostBillingAsync(progressBill.MsapBillingId, "check", default)).IsSuccess, "Posting ignored an unbilled ticket when Billing.JobOrderId was absent.");
                progressBill.Status = MsapConstants.BillingStatus.ForCollection;
                ticket.Status = MsapConstants.DispatchTicketStatus.Billed;
                await db.SaveChangesAsync();
                Check(!(await progressCollection.CreateCollectionAsync(new IBS.Models.MSAP.ViewModels.CreateCollectionViewModel
                {
                    Date = order.Date, CustomerId = customer.CustomerId, Amount = 100, CashAmount = 100,
                    BillingPayments = [new IBS.Models.MSAP.ViewModels.BillingPaymentViewModel { BillingId = progressBill.MsapBillingId, AmountToPay = 100 }]
                }, "check", default)).IsSuccess, "Collection ignored unfinished billing on another ticket.");
                customer = await db.Customers.SingleAsync(c => c.CustomerId == customer.CustomerId);
                ticket = await db.MsapDispatchTickets.SingleAsync(t => t.DispatchTicketId == ticket.DispatchTicketId);
                unfinished = await db.MsapDispatchTickets.SingleAsync(t => t.DispatchTicketId == unfinished.DispatchTicketId);
                progressBill = await db.MsapBillings.SingleAsync(b => b.MsapBillingId == progressBill.MsapBillingId);
                customer.WithHoldingVat = true;
                progressBill.IsVatable = true;
                progressBill.Amount = progressBill.Balance = 112;
                unfinished.Status = MsapConstants.DispatchTicketStatus.Billed;
                unfinished.BillingId = progressBill.MsapBillingId;
                var settlementSplit = new Billing
                {
                    MsapBillingNumber = "SETTLE-BAF", Date = order.Date, Status = MsapConstants.BillingStatus.ForCollection,
                    BilledTo = "LOCAL", CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId,
                    TerminalId = terminal.TerminalId, CreatedBy = "check", JobOrderId = order.JobOrderId, IsVatable = true, Amount = 56, Balance = 56
                };
                db.Add(settlementSplit);
                await db.SaveChangesAsync();
                var settlementRequest = new IBS.Models.MSAP.ViewModels.CreateCollectionViewModel
                {
                    Date = order.Date, CustomerId = customer.CustomerId, JobOrderId = order.JobOrderId, MsapCollectionNumber = "SETTLE-CR",
                    Amount = 160.5m, CashAmount = 160.5m, WVAT = 7.5m,
                    BillingPayments = [new() { BillingId = progressBill.MsapBillingId, AmountToPay = 107 }, new() { BillingId = settlementSplit.MsapBillingId, AmountToPay = 53.5m }]
                };
                settlementRequest.WVAT = 0;
                Check(!(await progressCollection.CreateCollectionAsync(settlementRequest, "check", default)).IsSuccess
                    && !await db.MsapCollections.AnyAsync(), "Incorrect withholding created a collection.");
                settlementRequest.WVAT = 7.5m;
                settlementRequest.Amount = settlementRequest.CashAmount = 168;
                settlementRequest.BillingPayments![0].AmountToPay = 112;
                settlementRequest.BillingPayments[1].AmountToPay = 56;
                Check(!(await progressCollection.CreateCollectionAsync(settlementRequest, "check", default)).IsSuccess
                    && !await db.MsapCollections.AnyAsync(), "Payment above the remaining net balance created a collection.");
                settlementRequest.Amount = settlementRequest.CashAmount = 160.5m;
                settlementRequest.BillingPayments[0].AmountToPay = 107;
                settlementRequest.BillingPayments[1].AmountToPay = 53.5m;
                async Task<ServiceResult<int>> CollectInNewContext(string number)
                {
                    await using var isolated = new MsapDbContext(options);
                    var request = new IBS.Models.MSAP.ViewModels.CreateCollectionViewModel
                    {
                        Date = settlementRequest.Date, CustomerId = settlementRequest.CustomerId, JobOrderId = settlementRequest.JobOrderId,
                        MsapCollectionNumber = number, Amount = settlementRequest.Amount, CashAmount = settlementRequest.CashAmount, WVAT = settlementRequest.WVAT,
                        BillingPayments = settlementRequest.BillingPayments.Select(p => new IBS.Models.MSAP.ViewModels.BillingPaymentViewModel
                        {
                            BillingId = p.BillingId, AmountToPay = p.AmountToPay
                        }).ToList()
                    };
                    return await new CollectionService(new UnitOfWork(isolated, shared), NullLogger<CollectionService>.Instance)
                        .CreateCollectionAsync(request, "check", default);
                }
                var concurrentCollections = await Task.WhenAll(CollectInNewContext("SETTLE-A"), CollectInNewContext("SETTLE-B"));
                Check(concurrentCollections.Count(r => r.IsSuccess) == 1 && await db.MsapCollections.CountAsync() == 1,
                    "Concurrent collection submissions paid the same split bills twice.");
                var settled = concurrentCollections.Single(r => r.IsSuccess);
                settlementRequest.MsapCollectionNumber = (await db.MsapCollections.SingleAsync(c => c.MsapCollectionId == settled.Data)).MsapCollectionNumber;
                progressBill = await db.MsapBillings.SingleAsync(b => b.MsapBillingId == progressBill.MsapBillingId);
                settlementSplit = await db.MsapBillings.SingleAsync(b => b.MsapBillingId == settlementSplit.MsapBillingId);
                Check(progressBill.Balance == 0 && settlementSplit.Balance == 0
                    && (await JobProgressCalculator.LoadAsync(work, order, booking, default)).Stage == JobProgressCalculator.DepositStage,
                    "Split billing remains collectible after receiving the full net payment and withholding VAT.");
                settlementRequest.MsapCollectionId = settled.Data;
                Check((await progressCollection.UpdateCollectionAsync(settlementRequest, "check", default)).IsSuccess
                    && progressBill.Balance == 0 && settlementSplit.Balance == 0, "Editing a settled split collection failed or counted withholding twice.");
                settlementRequest.MsapCollectionId = null;
                settlementRequest.MsapCollectionNumber = "DUP-CR";
                Check(!(await progressCollection.CreateCollectionAsync(settlementRequest, "check", default)).IsSuccess,
                    "A second collection paid already settled split bills.");
                Check(await db.MsapCollections.CountAsync() == 1 && progressBill.CollectionId == settled.Data && settlementSplit.CollectionId == settled.Data,
                    "Duplicate collection replaced receipt links or left an extra collection.");
                var receipt = await progressCollection.GetCollectionByIdAsync(settled.Data, default);
                var receiptProgress = await JobProgressCalculator.LoadForBillingsAsync(work, receipt!.PaidBills!.Select(b => b.MsapBillingId), default);
                Check(receiptProgress.Count == 1 && receiptProgress[0].Stage == JobProgressCalculator.DepositStage, "Split collection preview lost Job Progress.");
                Check(!(await progressCollection.CreateCollectionAsync(new IBS.Models.MSAP.ViewModels.CreateCollectionViewModel
                {
                    Date = order.Date, CustomerId = customer.CustomerId, JobOrderId = order.JobOrderId, MsapCollectionNumber = "EMPTY-CR"
                }, "check", default)).IsSuccess && await db.MsapCollections.CountAsync() == 1, "Empty collection bypassed selection validation.");
                customer = await db.Customers.SingleAsync(c => c.CustomerId == customer.CustomerId);
                ticket = await db.MsapDispatchTickets.SingleAsync(t => t.DispatchTicketId == ticket.DispatchTicketId);
                unfinished = await db.MsapDispatchTickets.SingleAsync(t => t.DispatchTicketId == unfinished.DispatchTicketId);
                progressBill = await db.MsapBillings.SingleAsync(b => b.MsapBillingId == progressBill.MsapBillingId);
                settlementSplit = await db.MsapBillings.SingleAsync(b => b.MsapBillingId == settlementSplit.MsapBillingId);
                receipt = await db.MsapCollections.SingleAsync(c => c.MsapCollectionId == settled.Data);
                progressBill.CollectionId = null;
                progressBill.CollectionNumber = null;
                db.Remove(settlementSplit);
                db.Remove(receipt);
                customer.WithHoldingVat = false;
                Console.WriteLine("PASS: split billing settles cash plus withholding, prevents duplicate collection, and retains receipt progress.");
                ticket.BillingId = null;
                ticket.Billing = null;
                ticket.Status = MsapConstants.DispatchTicketStatus.ForTariff;
                unfinished.BillingId = null;
                unfinished.Billing = null;
                db.Remove(progressBill);
                db.Remove(unfinished);
                await db.SaveChangesAsync();
                Console.WriteLine("PASS: real service gates block premature individual/batch approval, normal/split billing, posting and collection; rejected tariff correction resubmits successfully.");

                var revision = Booking(2);
                revision.VesselScheduleId = booking.VesselScheduleId;
                revision.Status = MsapConstants.VesselScheduleStatus.Cancelled;
                var updated = await schedules.UpdateAsync(revision, "check");
                Check(!updated.IsSuccess, "Schedule revision must be blocked once Dispatch Tickets exist.");
                booking = await Reload(booking.VesselScheduleId);
                order = await db.MsapJobOrders.SingleAsync();
                ticket = await db.MsapDispatchTickets.SingleAsync();
                Check(booking.Status == MsapConstants.VesselScheduleStatus.Confirmed && order.PlannedStartTime == new DateTime(2030, 1, 1, 8, 0, 0)
                    && ticket.DateLeft == new DateOnly(2030, 1, 1) && ticket.TimeLeft == new TimeOnly(8, 15), "Revision changed status or replaced actual service times.");
                ticket.Status = MsapConstants.DispatchTicketStatus.Billed;
                await db.SaveChangesAsync();
                Check(!(await schedules.UpdateAsync(revision, "check")).IsSuccess, "Revision bypassed billed-ticket restrictions.");
                ticket.Status = MsapConstants.DispatchTicketStatus.ForTariff;
                order.Status = MsapConstants.JobOrderStatus.Closed;
                await db.SaveChangesAsync();
                revision.PlannedStart = revision.PlannedStart.AddHours(1);
                revision.PlannedEnd = revision.PlannedEnd.AddHours(1);
                Check(!(await schedules.UpdateAsync(revision, "check")).IsSuccess, "Revision bypassed closed Job Order rules.");
                booking = await Reload(booking.VesselScheduleId);
                Check(booking.PlannedStart.Hour == 8, "Rejected revision changed the booking.");
                Check(!(await schedules.ChangeStatusAsync(booking.VesselScheduleId, MsapConstants.VesselScheduleStatus.Cancelled, "check")).IsSuccess, "Schedule cancelled despite existing Dispatch Tickets.");
                Check(!(await jobs.CancelJobOrderAsync(order.JobOrderId, "check")).IsSuccess, "Job Order cancelled with active tickets.");
                order = await db.MsapJobOrders.SingleAsync();
                order.Status = MsapConstants.JobOrderStatus.Open;
                ticket = await db.MsapDispatchTickets.SingleAsync();
                ticket.Status = MsapConstants.DispatchTicketStatus.Deleted;
                await db.SaveChangesAsync();
                var bill = new Billing
                {
                    MsapBillingNumber = "CHECK-B", Date = order.Date, Status = MsapConstants.BillingStatus.ForPosting,
                    BilledTo = "LOCAL", CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId,
                    TerminalId = terminal.TerminalId, JobOrderId = order.JobOrderId, CreatedBy = "check"
                };
                db.Add(bill);
                await db.SaveChangesAsync();
                Check(!(await jobs.CancelJobOrderAsync(order.JobOrderId, "check")).IsSuccess, "Cancellation bypassed unposted billing.");
                bill.Status = MsapConstants.BillingStatus.ForCollection;
                await db.SaveChangesAsync();
                Check(!(await jobs.CancelJobOrderAsync(order.JobOrderId, "check")).IsSuccess, "Cancellation bypassed posted billing.");
                db.Remove(bill);
                await db.SaveChangesAsync();
                Check((await jobs.CancelJobOrderAsync(order.JobOrderId, "check")).IsSuccess, "Resolved booking could not cancel through Job Order.");
                booking = await Reload(booking.VesselScheduleId);
                Check(booking.Status == MsapConstants.VesselScheduleStatus.Cancelled
                    && (await db.MsapJobOrders.SingleAsync()).Status == MsapConstants.JobOrderStatus.Cancelled
                    && await db.MsapDispatchTickets.CountAsync() == 1, "Cancellation did not synchronize or retain records.");
                await jobs.TryAutoCloseAsync(order.JobOrderId, "check", default);
                Check((await db.MsapJobOrders.SingleAsync()).Status == MsapConstants.JobOrderStatus.Cancelled, "Auto-close overwrote cancellation.");
                var dispatcher = new DispatchTicketService(work, null!, NullLogger<DispatchTicketService>.Instance);
                Check(!(await dispatcher.RestoreTicketAsync(ticket.DispatchTicketId, "check", default)).IsSuccess, "Cancelled Job Order restored a ticket.");
                Check(!(await jobs.AssignTugboatAsync(order.JobOrderId, tug.TugboatId, "check", default)).IsSuccess, "Cancelled Job Order accepted a tug.");
                Check(!(await schedules.DeleteAsync(booking.VesselScheduleId, "check")).IsSuccess, "A linked booking was deleted.");

                var concurrent = Booking(3);
                Check((await schedules.CreateAsync(concurrent, "check")).IsSuccess, "Could not prepare duplicate-click check.");
                concurrent = await Reload(concurrent.VesselScheduleId);
                async Task<ServiceResult<int>> ConfirmInNewContext(int id, DateTime review)
                {
                    await using var isolated = new MsapDbContext(options);
                    var isolatedWork = new UnitOfWork(isolated, shared);
                    return await new VesselScheduleService(isolatedWork, new JobOrderService(isolatedWork, NullLogger<JobOrderService>.Instance), NullLogger<VesselScheduleService>.Instance)
                        .ConfirmAsync(id, review, "check");
                }
                var duplicates = await Task.WhenAll(ConfirmInNewContext(concurrent.VesselScheduleId, concurrent.CreatedDate), ConfirmInNewContext(concurrent.VesselScheduleId, concurrent.CreatedDate));
                Check(duplicates.All(r => r.IsSuccess) && duplicates[0].Data == duplicates[1].Data && await db.MsapJobOrders.CountAsync() == 2, "Concurrent confirmation created duplicate orders.");
                var left = Booking(4);
                var right = Booking(5);
                Check((await schedules.CreateAsync(left, "check")).IsSuccess && (await schedules.CreateAsync(right, "check")).IsSuccess, "Could not prepare numbering check.");
                left = await Reload(left.VesselScheduleId);
                right = await Reload(right.VesselScheduleId);
                var parallel = await Task.WhenAll(ConfirmInNewContext(left.VesselScheduleId, left.CreatedDate), ConfirmInNewContext(right.VesselScheduleId, right.CreatedDate));
                Check(parallel.All(r => r.IsSuccess) && await db.MsapJobOrders.Select(j => j.JobOrderNumber).Distinct().CountAsync() == 4, "Parallel bookings received duplicate Job Order numbers.");
                var failing = Booking(6);
                Check((await schedules.CreateAsync(failing, "check")).IsSuccess, "Could not prepare rollback check.");
                failing = await Reload(failing.VesselScheduleId);
                var failingService = new VesselScheduleService(work, new FailingJobOrderService(work), NullLogger<VesselScheduleService>.Instance);
                Check(!(await failingService.ConfirmAsync(failing.VesselScheduleId, failing.CreatedDate, "check")).IsSuccess, "A failed order save reported success.");
                failing = await Reload(failing.VesselScheduleId);
                Check(failing.JobOrderId == null && failing.Status == MsapConstants.VesselScheduleStatus.Tentative && await db.MsapJobOrders.CountAsync() == 4, "Job Order / schedule confirmation was not atomic.");
                var legacy = Booking(7);
                legacy.CustomerId = null;
                legacy.CreatedDate = new DateTime(2030, 1, 1);
                db.Add(legacy);
                await db.SaveChangesAsync();
                Check(!(await schedules.ConfirmAsync(legacy.VesselScheduleId, legacy.CreatedDate, "check")).IsSuccess, "A legacy booking confirmed without a customer.");
                var unassigned = Booking(8);
                unassigned.AssignedTugboatIds = null;
                Check((await schedules.CreateAsync(unassigned, "check")).IsSuccess, "Tentative booking required a tug.");
                unassigned = await Reload(unassigned.VesselScheduleId);
                Check(!(await schedules.ConfirmAsync(unassigned.VesselScheduleId, unassigned.CreatedDate, "check")).IsSuccess, "Confirmation did not require a tug.");
                Check(!(await schedules.ChangeStatusAsync(unassigned.VesselScheduleId, MsapConstants.VesselScheduleStatus.Confirmed, "check")).IsSuccess, "Status action bypassed confirmation.");
                Check(!(await schedules.ChangeStatusAsync(concurrent.VesselScheduleId, MsapConstants.VesselScheduleStatus.Completed, "check")).IsSuccess, "Booking completed without actual tickets.");
                concurrent = await Reload(concurrent.VesselScheduleId);
                var past = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime().AddDays(-1));
                var completedTicket = new DispatchTicket
                {
                    DispatchNumber = "DONE-DT", Date = past, JobOrderId = concurrent.JobOrderId,
                    CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId,
                    TugBoatId = tug.TugboatId, ServiceId = serviceType.ServiceId, Status = MsapConstants.DispatchTicketStatus.ForTariff,
                    CreatedBy = "check", DateLeft = past, TimeLeft = new TimeOnly(8, 0), DateArrived = past
                };
                db.Add(completedTicket);
                await db.SaveChangesAsync();
                Check(!(await jobs.CompleteBookingAsync(concurrent.JobOrderId!.Value, "check")).IsSuccess, "Booking completed without actual end time.");
                completedTicket.TimeArrived = new TimeOnly(7, 0);
                await db.SaveChangesAsync();
                Check(!(await jobs.CompleteBookingAsync(concurrent.JobOrderId.Value, "check")).IsSuccess, "Booking completed with reversed actual times.");
                completedTicket.TimeArrived = new TimeOnly(9, 0);
                await db.SaveChangesAsync();
                Check((await jobs.CompleteBookingAsync(concurrent.JobOrderId.Value, "check")).IsSuccess, "Operational completion failed before tariff/billing.");
                concurrent = await Reload(concurrent.VesselScheduleId);
                Check(concurrent.Status == MsapConstants.VesselScheduleStatus.Completed &&
                    (await db.MsapJobOrders.SingleAsync(j => j.JobOrderId == concurrent.JobOrderId)).Status == MsapConstants.JobOrderStatus.Open,
                    "Operational completion closed the financial workflow.");
                Check(!(await dispatcher.CreateDispatchTicketAsync(new IBS.Models.MSAP.ViewModels.DispatchTicketViewModel
                {
                    JobOrderId = concurrent.JobOrderId, DispatchNumber = "LATE-DT", Date = past, CustomerId = customer.CustomerId,
                    VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId, TugBoatId = tug.TugboatId, ServiceId = serviceType.ServiceId
                }, null, null, "check", default)).IsSuccess, "Completed booking accepted a new ticket.");
                Check(!(await jobs.CancelJobOrderAsync(concurrent.JobOrderId!.Value, "check")).IsSuccess, "Completed booking was cancelled.");
                var cancellation = Booking(9);
                Check((await schedules.CreateAsync(cancellation, "check")).IsSuccess, "Could not prepare linked cancellation.");
                cancellation = await Reload(cancellation.VesselScheduleId);
                Check((await schedules.ConfirmAsync(cancellation.VesselScheduleId, cancellation.CreatedDate, "check")).IsSuccess, "Could not confirm cancellation booking.");
                cancellation = await Reload(cancellation.VesselScheduleId);
                Check((await schedules.ChangeStatusAsync(cancellation.VesselScheduleId, MsapConstants.VesselScheduleStatus.Cancelled, "check")).IsSuccess, "Confirmed booking without tickets could not cancel.");
                Check((await db.MsapJobOrders.SingleAsync(j => j.JobOrderId == cancellation.JobOrderId)).Status == MsapConstants.JobOrderStatus.Cancelled,
                    "Schedule-only cancellation left its Job Order active.");
                var race = Booking(10);
                Check((await schedules.CreateAsync(race, "check")).IsSuccess, "Could not prepare booking race.");
                race = await Reload(race.VesselScheduleId);
                Check((await schedules.ConfirmAsync(race.VesselScheduleId, race.CreatedDate, "check")).IsSuccess, "Could not confirm booking race.");
                race = await Reload(race.VesselScheduleId);
                async Task<bool> RaceAction(bool cancel)
                {
                    await using var isolated = new MsapDbContext(options);
                    var raceWork = new UnitOfWork(isolated, shared);
                    if (cancel)
                    {
                        return (await new JobOrderService(raceWork, NullLogger<JobOrderService>.Instance).CancelJobOrderAsync(race.JobOrderId!.Value, "check")).IsSuccess;
                    }
                    var raceDispatch = new DispatchTicketService(raceWork, null!, NullLogger<DispatchTicketService>.Instance);
                    return (await raceDispatch.CreateDispatchTicketAsync(new IBS.Models.MSAP.ViewModels.DispatchTicketViewModel
                    {
                        JobOrderId = race.JobOrderId, DispatchNumber = "RACE-DT", Date = past, CustomerId = customer.CustomerId,
                        VesselId = vessel.VesselId, PortId = port.PortId, TerminalId = terminal.TerminalId, TugBoatId = tug.TugboatId, ServiceId = serviceType.ServiceId
                    }, null, null, "check", default)).IsSuccess;
                }
                var raceResults = await Task.WhenAll(RaceAction(true), RaceAction(false));
                Check(raceResults.Count(success => success) == 1, "Cancellation and ticket creation both succeeded, or neither succeeded.");
                race = await Reload(race.VesselScheduleId);
                if (raceResults[0])
                {
                    Check(race.Status == MsapConstants.VesselScheduleStatus.Cancelled &&
                        !await db.MsapDispatchTickets.AnyAsync(t => t.JobOrderId == race.JobOrderId), "Cancelled order accepted a racing ticket.");
                }
                else
                {
                    Check(race.Status == MsapConstants.VesselScheduleStatus.Confirmed &&
                        (await db.MsapJobOrders.SingleAsync(j => j.JobOrderId == race.JobOrderId)).Status == MsapConstants.JobOrderStatus.Open,
                        "Ticket creation left a cancelled parent.");
                }
                var completedRevision = Booking(3);
                completedRevision.VesselScheduleId = concurrent.VesselScheduleId;
                Check(!(await schedules.UpdateAsync(completedRevision, "check")).IsSuccess, "Completed booking remained editable.");
                var revisable = Booking(28);
                Check((await schedules.CreateAsync(revisable, "check")).IsSuccess, "Could not prepare unused schedule revision.");
                revisable = await Reload(revisable.VesselScheduleId);
                var firstConfirmation = await schedules.ConfirmAsync(revisable.VesselScheduleId, revisable.CreatedDate, "check");
                Check(firstConfirmation.IsSuccess, firstConfirmation.Message!);
                var originalOrder = await db.MsapJobOrders.SingleAsync(j => j.JobOrderId == firstConfirmation.Data);
                Check(await jobs.GetEditErrorAsync(originalOrder, default) != null, "Scheduled Job Order remained editable directly.");
                int orderCount = await db.MsapJobOrders.CountAsync();
                var additionalTug = new Tugboat { TugboatNumber = "003", TugboatName = "Additional Tug", PortId = port.PortId, IsCompanyOwned = true };
                db.Add(additionalTug);
                await db.SaveChangesAsync();
                var tugAddition = Booking(28);
                tugAddition.VesselScheduleId = revisable.VesselScheduleId;
                tugAddition.AssignedTugboatIds = JsonSerializer.Serialize(new[] { tug.TugboatId, additionalTug.TugboatId });
                Check((await schedules.UpdateAsync(tugAddition, "check")).IsSuccess, "Could not add a tugboat to the confirmed booking.");
                revisable = await Reload(revisable.VesselScheduleId);
                originalOrder = await db.MsapJobOrders.SingleAsync(j => j.JobOrderId == firstConfirmation.Data);
                Check(revisable.Status == MsapConstants.VesselScheduleStatus.Confirmed && revisable.JobOrderId == firstConfirmation.Data
                    && originalOrder.Status == MsapConstants.JobOrderStatus.Open && originalOrder.RequiredTugCount == 2
                    && originalOrder.PreferredTugboatId == tug.TugboatId && await db.MsapJobOrders.CountAsync() == orderCount,
                    "Adding only tugboats invalidated or replaced the Job Order, lost confirmation, or failed to update its tug count.");
                var repeatedAssignment = Booking(28);
                repeatedAssignment.VesselScheduleId = revisable.VesselScheduleId;
                repeatedAssignment.AssignedTugboatIds = JsonSerializer.Serialize(new[] { additionalTug.TugboatId, tug.TugboatId, additionalTug.TugboatId });
                Check((await schedules.UpdateAsync(repeatedAssignment, "check")).IsSuccess, "Reordered duplicate tug assignments could not be saved.");
                revisable = await Reload(revisable.VesselScheduleId);
                Check(revisable.JobOrderId == firstConfirmation.Data && revisable.Status == MsapConstants.VesselScheduleStatus.Confirmed
                    && await db.MsapJobOrders.CountAsync() == orderCount, "An unchanged tugboat set caused unnecessary invalidation.");
                var newPlan = Booking(28);
                newPlan.VesselScheduleId = revisable.VesselScheduleId;
                newPlan.PlannedStart = newPlan.PlannedStart.AddHours(1);
                newPlan.PlannedEnd = newPlan.PlannedEnd.AddHours(1);
                Check((await schedules.UpdateAsync(newPlan, "check")).IsSuccess, "Unused confirmed booking could not be revised.");
                revisable = await Reload(revisable.VesselScheduleId);
                originalOrder = await db.MsapJobOrders.SingleAsync(j => j.JobOrderId == firstConfirmation.Data);
                Check(revisable.Status == MsapConstants.VesselScheduleStatus.Tentative && revisable.JobOrderId == null
                    && originalOrder.Status == MsapConstants.JobOrderStatus.Invalidated && await db.MsapJobOrders.CountAsync() == orderCount,
                    "Revision failed to retain and invalidate the unused order or return the booking to Tentative.");
                var replacement = await schedules.ConfirmAsync(revisable.VesselScheduleId, revisable.EditedDate!.Value, "check");
                Check(replacement.IsSuccess && replacement.Data != firstConfirmation.Data && await db.MsapJobOrders.CountAsync() == orderCount + 1,
                    "Reconfirmation did not create a new numbered replacement order.");
                var outsider = new Tugboat { TugboatNumber = "002", TugboatName = "Unassigned Tug", PortId = port.PortId, IsCompanyOwned = true };
                db.Add(outsider);
                await db.SaveChangesAsync();
                var optionsForTicket = await dispatcher.PopulateDispatchTicketViewModelAsync(null, replacement.Data, default);
                Check(optionsForTicket.Tugboats!.Count == 1 && optionsForTicket.Tugboats[0].Value == tug.TugboatId.ToString(), "Scheduled ticket dropdown included an unassigned tugboat.");
                optionsForTicket.DispatchNumber = "INVALID-TUG";
                optionsForTicket.TugBoatId = outsider.TugboatId;
                optionsForTicket.ServiceId = serviceType.ServiceId;
                Check(!(await dispatcher.CreateDispatchTicketAsync(optionsForTicket, null, null, "check", default)).IsSuccess, "Scheduled ticket creation accepted an unassigned tugboat.");
                var direct = new JobOrder
                {
                    Date = new DateOnly(2030, 1, 28), CustomerId = customer.CustomerId, VesselId = vessel.VesselId,
                    PortId = port.PortId, TerminalId = terminal.TerminalId
                };
                var directCreated = await jobs.CreateJobOrderAsync(direct, "check", default);
                Check(directCreated.IsSuccess, directCreated.Message!);
                var directRevision = new JobOrder
                {
                    JobOrderId = directCreated.Data, Date = direct.Date, CustomerId = direct.CustomerId,
                    VesselId = direct.VesselId, PortId = direct.PortId, TerminalId = direct.TerminalId, Remarks = "Revised before ticket entry"
                };
                Check((await jobs.UpdateJobOrderAsync(directRevision, "check", default)).IsSuccess, "Direct unused Job Order could not be edited.");
                var directOptions = await dispatcher.PopulateDispatchTicketViewModelAsync(null, directCreated.Data, default);
                Check(directOptions.Tugboats!.Any(t => t.Value == outsider.TugboatId.ToString()), "Direct Job Order incorrectly restricted tugboat options.");
                directOptions.DispatchNumber = "DIRECT-LOCK";
                directOptions.TugBoatId = outsider.TugboatId;
                directOptions.ServiceId = serviceType.ServiceId;
                Check((await dispatcher.CreateDispatchTicketAsync(directOptions, null, null, "check", default)).IsSuccess, "Direct ticket entry failed.");
                Check(!(await jobs.UpdateJobOrderAsync(directRevision, "check", default)).IsSuccess, "Direct Job Order remained editable after ticket entry.");
                Console.WriteLine("PASS: tugboat additions preserve confirmation and the same order; other unused schedule revisions retain invalidated numbering; reconfirmation creates a replacement; scheduled orders and orders with tickets cannot be edited; assigned tugboats are enforced.");
                Console.WriteLine("PASS: scheduling migration, tentative-only save, booking review, customer/tug/period/overlap guards, linked order, revisions, actual times, synchronized cancellation, billing guards, operational completion, duplicate clicks, concurrent numbering/cancellation and atomic rollback.");
            }
            finally
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", server);
                await drop.ExecuteNonQueryAsync();
            }
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class FailingJobOrderService : JobOrderService
        {
            public FailingJobOrderService(UnitOfWork work) : base(work, NullLogger<JobOrderService>.Instance)
            {
            }

            public override async Task<ServiceResult<int>> CreateJobOrderAsync(JobOrder job, string username, CancellationToken ct)
            {
                var created = await base.CreateJobOrderAsync(job, username, ct);
                Check(created.IsSuccess, created.Message!);
                return ServiceResult<int>.Failure("Injected failure after saving the order.");
            }
        }

        private sealed class CheckAccess : IAccessControlService
        {
            private readonly ProcedureEnum? _allowed;

            public CheckAccess(ProcedureEnum? allowed)
            {
                _allowed = allowed;
            }

            public Task<bool> HasAccessAsync(string userId, params ProcedureEnum[] procedures)
            {
                return Task.FromResult(_allowed.HasValue && procedures.Contains(_allowed.Value));
            }

            public Task<bool> HasAnyAccessAsync(string userId, params ProcedureEnum[] procedures)
            {
                return HasAccessAsync(userId, procedures);
            }
        }
    }
}
