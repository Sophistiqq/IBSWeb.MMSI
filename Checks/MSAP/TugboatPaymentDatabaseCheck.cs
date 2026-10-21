using System.Security.Claims;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.Repository;
using IBS.Models;
using IBS.Models.Filpride;
using IBS.Models.Enums;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.MasterFile;
using IBS.Models.Filpride.ViewModels;
using IBS.Models.MSAP;
using IBS.Services;
using IBS.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IBSWeb.Areas.Filpride.Controllers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Checks.MSAP
{
    internal static class TugboatPaymentDatabaseCheck
    {
        public static async Task RunAsync()
        {
            string connection = TugboatOwnerCheck.TestConnection();
            await using var source = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql(connection).Options);
            await using var services = new ServiceCollection().AddLogging().AddSingleton<TugboatWorkspace>()
                .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection))
                .AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                .Services.BuildServiceProvider();
            var options = services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
            await using var db = Open();
            await db.Database.MigrateAsync();
            Check(!db.Database.HasPendingModelChanges(), "Payment migration differs from the model.");
            var ticket = await source.MsapDispatchTickets.Include(t => t.ExpectedServiceEntry)
                .Where(t => t.BillingId != null && t.VesselScheduleServiceEntryId != null && t.OtherTugChargeLine == null)
                .OrderBy(t => t.DispatchTicketId).FirstAsync();
            var tug = await source.MsapTugboats.FirstAsync(t => !t.IsCompanyOwned && t.TugboatOwnerId != null);
            var owner = await source.MsapTugboatOwners.SingleAsync(o => o.TugboatOwnerId == tug.TugboatOwnerId);
            owner.Address = "Disposable payment fixture";
            owner.Tin = "000-000-000-00000";
            owner.VatType = "Vatable";
            owner.TaxType = "Withholding Tax";
            owner.WithholdingTaxPercent = 0.02m;
            ticket.TugBoatId = tug.TugboatId;
            ticket.ExpectedServiceEntry!.TugboatId = tug.TugboatId;
            string reference = "PAY-" + Guid.NewGuid().ToString("N");
            var charge = new OtherTugCharge
            {
                TugboatOwnerId = owner.TugboatOwnerId,
                ProviderName = owner.TugboatOwnerName,
                ProviderReference = reference,
                DocumentDate = DateOnly.FromDateTime(DateTime.Today),
                IncludesVat = true,
                PreparedByName = "Payment check",
                PreparedByUserId = "payment-check",
                UpdatedAt = DateTime.Today,
                ConfirmedAt = DateTime.Today,
                ConfirmedByName = "Payment check",
                ConfirmedByUserId = "payment-check",
                Lines = [new OtherTugChargeLine { DispatchTicketId = ticket.DispatchTicketId, Amount = 1120m }]
            };
            source.Add(charge);
            await source.SaveChangesAsync();
            Check(TugboatPaymentService.Calculate(owner, charge) == (1120m, 20m), "VAT-inclusive withholding is wrong.");
            charge.IncludesVat = false;
            charge.Lines[0].Amount = 1000m;
            Check(TugboatPaymentService.Calculate(owner, charge) == (1120m, 20m), "VAT-exclusive withholding is wrong.");
            charge.IncludesVat = true;
            charge.Lines[0].Amount = 1120m;
            source.ChangeTracker.Clear();
            var bank = await db.FilprideBankAccounts.FirstAsync(b => b.IsActive);
            var baseline = new Dictionary<string, string>
            {
                ["201020500"] = "AP - Trade - Other Tugs",
                ["201020600"] = "AP - Trade - SBMA",
                ["201020700"] = "AP - Trade - Commission",
                ["201020800"] = "AP - Trade - Incentive",
                ["201020900"] = "AP - Trade - Fuel",
                ["201021000"] = "AP - Trade - Freight",
                ["201021100"] = "AP - Trade - Trading Fee",
                ["201021200"] = "AP - Trade - Lubes"
            };
            var accounts = await db.FilprideChartOfAccounts.AsNoTracking()
                .Where(a => baseline.Keys.Contains(a.AccountNumber)).ToListAsync();
            Check(accounts.Count == baseline.Count && accounts.All(a => a.AccountName == baseline[a.AccountNumber]
                && !a.IsMain && !a.HasChildren && !a.IsHidden && a.AccountType == "Liabilities"
                && a.NormalBalance == "Credit" && a.FinancialStatementType == "BalanceSheet"),
                "The screenshot baseline requires the eight dedicated AP Trade liability accounts created in the dev chart.");
            var payableAccount = accounts.Single(a => a.AccountNumber == "201020500");
            Check(TugboatPaymentService.ApAccount == payableAccount.AccountNumber && !payableAccount.IsMain && !payableAccount.HasChildren
                && payableAccount.AccountType == "Liabilities" && payableAccount.NormalBalance == "Credit"
                && payableAccount.FinancialStatementType == "BalanceSheet",
                "AP Other Tugs must use the dedicated 201020500 payable, not the generic trade control or a P&L account.");
            var storage = new Storage();
            var service = Service(db, storage);
            var form = Form();
            var prepared = Form();
            await service.PrepareAsync(prepared, default);
            Check(prepared.Owner?.TugboatOwnerId == owner.TugboatOwnerId && prepared.AllocationIds.SequenceEqual(form.AllocationIds),
                "Direct payment selection did not load its owner and confirmed provider bill.");
            Check(prepared.WithholdingAccountNumber == "201030220", "Payment preview did not use the owner's withholding account.");
            Check(!await db.FilprideJournalVoucherHeaders.AnyAsync(j => j.References == reference),
                "The direct-payment fixture must not have an initial payable journal.");
            var ownerSelection = new TugboatPaymentViewModel { OwnerId = owner.TugboatOwnerId };
            await service.PrepareAsync(ownerSelection, default);
            Check(ownerSelection.Owner?.TugboatOwnerId == owner.TugboatOwnerId && ownerSelection.OwnerProfileError == null
                && ownerSelection.Charge == null && ownerSelection.Bills.Single(b => b.ChargeId == charge.OtherTugChargeId)
                    .Lines.Any(l => l.TicketId == ticket.DispatchTicketId && l.CosId == ticket.CustomerOrderSlipId
                        && !string.IsNullOrWhiteSpace(l.CosNumber) && !string.IsNullOrWhiteSpace(l.TicketNumber)),
                "Owner selection did not load its profile and confirmed COS/tickets before choosing a bill.");
            var billAmounts = ownerSelection.Bills.Single(b => b.ChargeId == charge.OtherTugChargeId);
            Check(billAmounts.Gross == 1120m && billAmounts.Withholding == 20m && billAmounts.Net == 1100m,
                "Available bill totals did not use the authoritative VAT/withholding calculation.");
            storage.Fail = true;
            byte[] document = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nfixture document");
            await using (var stream = new MemoryStream(document))
            {
                var file = new FormFile(stream, 0, stream.Length, "file", "fixture.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
                await RejectAsync(() => service.CreateAsync(form, file, "Payment check", default), "Failed upload left a payment.");
            }
            Check(storage.Deleted && !await db.FilprideTugboatPayments.AnyAsync(p => p.OtherTugChargeId == charge.OtherTugChargeId),
                "Failed upload did not compensate and release the reservation.");
            storage.Fail = false;
            int id = await service.CreateAsync(form, null, "Payment check", default);
            var reservedSelection = new TugboatPaymentViewModel { OwnerId = owner.TugboatOwnerId };
            await service.PrepareAsync(reservedSelection, default);
            Check(reservedSelection.Bills.All(b => b.ChargeId != charge.OtherTugChargeId), "Reserved bill remained selectable in the COS/ticket list.");
            var saved = (await service.GetAsync(id, default))!;
            Check(saved.OldCvNo == form.OldCVNo, "The copied form's old CV reference was not saved.");
            Check(saved.SupplierId == null && saved.Payee == owner.TugboatOwnerName && saved.CheckAmount == 1100m
                && saved.TugboatPayments.Single().GrossAmount == 1120m && saved.TugboatPayments.Single().LiabilityJournalVoucherId == null
                && saved.TugboatPayments.Single().LiabilitySnapshot == "" && saved.Details!.Count == 3
                && saved.Details.All(d => !d.IsDisplayEntry), "Payment snapshot or real accounting entries are incorrect.");
            try
            {
                await db.GetService<IMigrator>().MigrateAsync("20261020144653_LinkOtherTugPayments");
                throw new InvalidOperationException("Rollback accepted a direct payment without an AP journal.");
            }
            catch (PostgresException ex) when (ex.SqlState == "P0001" && (ex.MessageText.Contains("direct tugboat payments", StringComparison.Ordinal)
                || ex.MessageText.Contains("selected-ticket vouchers", StringComparison.Ordinal)))
            {
                Check((await db.Database.GetAppliedMigrationsAsync()).Contains("20261021032054_AllowDirectTugboatPayments"), "Rejected rollback removed the direct-payment migration.");
                await db.Database.MigrateAsync();
                db.ChangeTracker.Clear();
            }
            await RejectAsync(() => service.CreateAsync(Form(), null, "Payment check", default), "Duplicate bill reservation was accepted.");
            await RejectSqlAsync($"UPDATE msap.mmsi_other_tug_charges SET provider_reference='changed' WHERE other_tug_charge_id={charge.OtherTugChargeId}");
            await RejectSqlAsync($"UPDATE msap.mmsi_other_tug_charge_lines SET amount=1 WHERE other_tug_charge_id={charge.OtherTugChargeId}");
            // Owner maintenance must not recalculate a saved voucher.
            var updatedOwner = await source.MsapTugboatOwners.SingleAsync(o => o.TugboatOwnerId == owner.TugboatOwnerId);
            updatedOwner.WithholdingTaxPercent = 0.01m;
            await source.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Print", "Payment check", null, default), "A draft voucher could be printed as posted.");
            var inactive = await db.FilprideBankAccounts.SingleAsync(b => b.BankAccountId == bank.BankAccountId);
            inactive.IsActive = false;
            await db.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "Inactive bank was accepted at posting.");
            inactive.IsActive = true;
            db.Update(inactive);
            await db.SaveChangesAsync();
            var period = new PostedPeriod { Module = nameof(Module.CheckVoucher), Month = form.Date.Month, Year = form.Date.Year, IsPosted = true, PostedBy = "Payment check" };
            db.PostedPeriods.Add(period);
            await db.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "A closed payment period was accepted.");
            db.PostedPeriods.Remove(period);
            await db.SaveChangesAsync();
            var altered = await db.FilprideCheckVoucherDetails.SingleAsync(d => d.CheckVoucherHeaderId == id && d.AccountNo == TugboatPaymentService.ApAccount);
            altered.Debit = 1m;
            await db.SaveChangesAsync();
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "Altered payment entries were accepted.");
            altered.Debit = 1120m;
            db.Update(altered);
            await db.SaveChangesAsync();
            await service.ChangeStatusAsync(id, "Post", "Payment check", null, default);
            await CheckLedgerAsync(saved.CheckVoucherHeaderNo!, 3, 1120m);
            await new UnitOfWork(db).FilprideCheckVoucher.RebuildGeneralLedgerAsync(form.Date, form.Date.AddDays(1));
            await CheckLedgerAsync(saved.CheckVoucherHeaderNo!, 3, 1120m);
            var trade = new CheckVoucherTradeController(new UnitOfWork(db), null!, db, storage,
                NullLogger<CheckVoucherTradeController>.Instance, null!, new CheckVoucherDocumentationService(db), service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Payment check")], "check"))
                    }
                }
            };
            var preview = await trade.Print(id, null, default) as ViewResult;
            Check(preview?.ViewName == "Print" && preview.Model is CheckVoucherVM { Supplier: null, Header: not null } previewModel
                && previewModel.Header.CheckVoucherHeaderId == id && previewModel.Details?.Count == 3
                && preview.ViewData["TugboatCosNumbers"] is string[] { Length: > 0 }
                && !(await service.GetAsync(id, default))!.IsPrinted,
                "Owner payment preview failed to reuse the existing voucher model or marked the voucher printed.");
            var original = await trade.PrintTugboatPayment(id, default) as ViewResult;
            Check(original?.ViewName == "Print" && original.Model is CheckVoucherVM
                && original.ViewData["WasPrinted"] is false && original.ViewData["PrintPayment"] is true
                && (await service.GetAsync(id, default))!.IsPrinted,
                "Original owner voucher failed to reuse the shared print view or lost its original-copy state.");
            var reprint = await trade.PrintTugboatPayment(id, default) as ViewResult;
            Check(reprint?.ViewName == "Print" && reprint.ViewData["WasPrinted"] is true,
                "Owner voucher reprint did not retain its recorded print state.");
            await RejectAsync(() => service.ChangeStatusAsync(id, "Post", "Payment check", null, default), "Repeated posting was accepted.");
            await RejectAsync(() => service.ChangeStatusAsync(id, "Cancel", "Payment check", "Correction", default), "A posted voucher could be cancelled without reversal.");
            await service.ChangeStatusAsync(id, "Unpost", "Payment check", null, default);
            Check(!await db.FilprideGeneralLedgerBooks.AnyAsync(g => g.Reference == saved.CheckVoucherHeaderNo)
                && (await service.GetAsync(id, default))!.TugboatPayments.Single().ReleasedAt == null, "Unposting released a bill or retained entries.");
            await RejectAsync(() => service.CreateAsync(Form(), null, "Payment check", default), "Unposting allowed a duplicate reservation.");
            await service.ChangeStatusAsync(id, "Post", "Payment check", null, default);
            await service.ChangeStatusAsync(id, "Void", "Payment check", "Correction fixture", default);
            await CheckLedgerAsync(saved.CheckVoucherHeaderNo!, 6, 2240m);
            Check(await db.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == id && p.ReleasedAt != null), "Voiding did not release the bill.");
            updatedOwner.WithholdingTaxPercent = 0.02m;
            await source.SaveChangesAsync();
            int replacement = await service.CreateAsync(Form(), null, "Payment check", default);
            await service.ChangeStatusAsync(replacement, "Cancel", "Payment check", "Correction fixture", default);
            Check(await db.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == replacement && p.ReleasedAt != null), "Cancellation retained a reservation.");
            var races = await Task.WhenAll(RaceAsync(), RaceAsync());
            Check(races.Count(x => x) == 1, "Concurrent drafts did not produce exactly one reservation.");
            int active = await db.FilprideTugboatPayments.Where(p => p.OtherTugChargeId == charge.OtherTugChargeId && p.ReleasedAt == null)
                .Select(p => p.CheckVoucherHeaderId).SingleAsync();
            var work = new UnitOfWork(db);
            var documentation = new CheckVoucherDocumentationService(db);
            var nonTrade = new CheckVoucherNonTradePaymentController(work, null!, db, storage,
                NullLogger<CheckVoucherNonTradePaymentController>.Instance, null!, documentation);
            var invoice = new CheckVoucherNonTradeInvoiceController(work, null!, db, storage,
                NullLogger<CheckVoucherNonTradeInvoiceController>.Instance, null!, documentation);
            var payroll = new CheckVoucherNonTradePayrollInvoiceController(work, null!, db, storage,
                NullLogger<CheckVoucherNonTradeInvoiceController>.Instance, null!, documentation);
            Check(await nonTrade.Post(active, default) is NotFoundResult
                && await nonTrade.Cancel(active, "Forged non-trade request", default) is NotFoundResult
                && await invoice.Post(active, null, default) is NotFoundResult
                && await invoice.Cancel(active, "Forged invoice request", default) is NotFoundResult
                && await payroll.Post(active, null, default) is NotFoundResult
                && await payroll.Cancel(active, "Forged payroll request", default) is NotFoundResult,
                "A non-trade action could bypass the AP Other Tugs lifecycle.");
            Check(await db.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == active && p.ReleasedAt == null)
                && (await service.GetAsync(active, default))!.CanceledBy == null,
                "A rejected non-trade request changed the provider reservation.");
            await CheckMultipleTicketsAsync();
            var workspace = new TugboatWorkspace();
            workspace.Select(await source.MsapTugboats.Where(t => t.IsCompanyOwned).Select(t => t.TugboatId).FirstAsync(), "Check tugboat");
            await using (var scoped = new ApplicationDbContext(options, workspace))
            {
                Check(!await scoped.FilprideTugboatPayments.AnyAsync(p => p.CheckVoucherHeaderId == active), "Company payment leaked into a tugboat workspace.");
                await RejectAsync(() => Service(scoped, storage).CreateAsync(Form(), null, "Payment check", default), "Company-level provider AP entered a tugboat workspace.");
            }
            Console.WriteLine($"PASS: AP Other Tugs gross/VAT/EWT, creation/posting without an initial AP journal, upload rollback, source locks, duplicate/concurrent reservations, immutable snapshots, post/unpost/void/cancel, dedicated 201020500 entries, deferred expense/AP recognition, AP reversal and workspace isolation. Clone fixture charge={charge.OtherTugChargeId}, draft CV={active}.");

            async Task CheckMultipleTicketsAsync()
            {
                var tickets = new List<DispatchTicket>();
                for (int index = 0; index < 4; index++)
                {
                    var entry = (VesselScheduleServiceEntry)source.Entry(ticket.ExpectedServiceEntry!).CurrentValues.ToObject();
                    entry.VesselScheduleServiceEntryId = 0;
                    source.Add(entry);
                    await source.SaveChangesAsync();
                    var copy = (DispatchTicket)source.Entry(ticket).CurrentValues.ToObject();
                    copy.DispatchTicketId = 0;
                    copy.AccountingSourceId = Guid.NewGuid();
                    copy.DispatchNumber = "MP" + Guid.NewGuid().ToString("N")[..16];
                    copy.VesselScheduleServiceEntryId = entry.VesselScheduleServiceEntryId;
                    source.Add(copy);
                    tickets.Add(copy);
                }
                await source.SaveChangesAsync();
                OtherTugCharge Bill(string suffix, bool includesVat, params (DispatchTicket Ticket, decimal Amount)[] lines)
                {
                    return new OtherTugCharge
                    {
                        TugboatOwnerId = owner.TugboatOwnerId, ProviderName = owner.TugboatOwnerName,
                        ProviderReference = reference + suffix, DocumentDate = form.Date, IncludesVat = includesVat,
                        PreparedByName = "Payment check", PreparedByUserId = "payment-check", UpdatedAt = DateTime.Today,
                        ConfirmedAt = DateTime.Today, ConfirmedByName = "Payment check", ConfirmedByUserId = "payment-check",
                        Lines = lines.Select(line => new OtherTugChargeLine { DispatchTicketId = line.Ticket.DispatchTicketId, Amount = line.Amount }).ToList()
                    };
                }
                var firstBill = Bill("-A", false, (tickets[0], 1000.03m), (tickets[1], 1000.03m));
                var secondBill = Bill("-B", true, (tickets[2], 1120m));
                var foreignTug = await source.MsapTugboats.FirstAsync(t => !t.IsCompanyOwned && t.TugboatOwnerId != null && t.TugboatOwnerId != owner.TugboatOwnerId);
                tickets[3].TugBoatId = foreignTug.TugboatId;
                var foreignBill = Bill("-FOREIGN", true, (tickets[3], 1120m));
                foreignBill.TugboatOwnerId = foreignTug.TugboatOwnerId!.Value;
                source.AddRange(firstBill, secondBill, foreignBill);
                await source.SaveChangesAsync();
                int firstLine = firstBill.Lines[0].OtherTugChargeLineId;
                int remainingLine = firstBill.Lines[1].OtherTugChargeLineId;
                int secondLine = secondBill.Lines[0].OtherTugChargeLineId;
                TugboatPaymentViewModel Selection(params int[] ids)
                {
                    TugboatPaymentViewModel selected = Form();
                    selected.AllocationIds = ids;
                    return selected;
                }
                await RejectAsync(() => service.CreateAsync(Selection(), null, "Payment check", default), "Empty ticket selection was accepted.");
                await RejectAsync(() => service.CreateAsync(Selection(firstLine, firstLine), null, "Payment check", default), "Duplicate selected IDs were accepted.");
                await RejectAsync(() => service.CreateAsync(Selection(firstLine, int.MaxValue), null, "Payment check", default), "A forged allocation ID was accepted.");
                await RejectAsync(() => service.CreateAsync(Selection(firstLine, foreignBill.Lines[0].OtherTugChargeLineId), null, "Payment check", default), "Tickets from different owners were combined.");
                int combined = await service.CreateAsync(Selection(firstLine, secondLine), null, "Payment check", default);
                FilprideCheckVoucherHeader combinedVoucher = (await service.GetAsync(combined, default))!;
                Check(combinedVoucher.TugboatPayments.Count == 2 && combinedVoucher.InvoiceAmount == 2240.04m
                    && combinedVoucher.CheckAmount == 2200.04m && combinedVoucher.TugboatPayments.Sum(p => p.WithholdingAmount) == 40m
                    && combinedVoucher.SINo!.Length == 2
                    && combinedVoucher.TugboatPayments.SelectMany(TugboatPaymentService.Lines).Select(l => l.AllocationId).Order()
                        .SequenceEqual(new[] { firstLine, secondLine }.Order()),
                    "Multi-bill payment did not save exactly the tagged tickets and their cumulative tax rounding.");
                var available = new TugboatPaymentViewModel { OwnerId = owner.TugboatOwnerId };
                await service.PrepareAsync(available, default);
                Check(available.Bills.Single(b => b.ChargeId == firstBill.OtherTugChargeId).Lines.Select(l => l.AllocationId).SequenceEqual([remainingLine])
                    && available.Bills.All(b => b.ChargeId != secondBill.OtherTugChargeId),
                    "Selecting one ticket reserved unchecked tickets or left selected tickets available.");
                await RejectAsync(() => service.CreateAsync(Selection(secondLine), null, "Payment check", default), "A reserved ticket was paid twice.");
                int remainder = await service.CreateAsync(Selection(remainingLine), null, "Payment check", default);
                FilprideCheckVoucherHeader remainderVoucher = (await service.GetAsync(remainder, default))!;
                Check(remainderVoucher.InvoiceAmount == 1120.03m
                    && combinedVoucher.TugboatPayments.Where(p => p.OtherTugChargeId == firstBill.OtherTugChargeId).Sum(p => p.GrossAmount)
                        + remainderVoucher.InvoiceAmount == 2240.07m,
                    "Separate ticket payments failed to preserve the full bill's rounded VAT total.");
                try
                {
                    await db.GetService<IMigrator>().MigrateAsync("20261021032054_AllowDirectTugboatPayments");
                    throw new InvalidOperationException("Rollback accepted multi-bill/selected-ticket payments.");
                }
                catch (PostgresException ex) when (ex.SqlState == "P0001" && ex.MessageText.Contains("selected-ticket vouchers", StringComparison.Ordinal))
                { }
                await service.ChangeStatusAsync(combined, "Post", "Payment check", null, default);
                await new UnitOfWork(db).FilprideCheckVoucher.RebuildGeneralLedgerAsync(form.Date, form.Date.AddDays(1));
                var ledger = await db.FilprideGeneralLedgerBooks.Where(g => g.Reference == combinedVoucher.CheckVoucherHeaderNo).ToListAsync();
                Check(ledger.Count == 3 && ledger.Sum(g => g.Debit) == 2240.04m && ledger.Sum(g => g.Credit) == 2240.04m
                    && ledger.All(g => g.CounterpartyType == CounterpartyType.TugboatOwner && g.CounterpartyId == owner.TugboatOwnerId),
                    "Combined payment posting/rebuild lost balanced totals or owner identity.");
                bool[] printedCopies = await Task.WhenAll(PrintCopyAsync(), PrintCopyAsync());
                Check(printedCopies.Count(wasPrinted => !wasPrinted) == 1 && printedCopies.Count(wasPrinted => wasPrinted) == 1,
                    "Concurrent print requests did not produce exactly one original and one reprint.");
                async Task<bool> PrintCopyAsync()
                {
                    await using ApplicationDbContext printing = Open();
                    return await Service(printing, storage).ChangeStatusAsync(combined, "Print", "Payment check", null, default);
                }
                await service.ChangeStatusAsync(combined, "Unpost", "Payment check", null, default);
                await RejectAsync(() => service.CreateAsync(Selection(firstLine), null, "Payment check", default), "Unposting released a selected ticket.");
                await service.ChangeStatusAsync(combined, "Post", "Payment check", null, default);
                await service.ChangeStatusAsync(combined, "Void", "Payment check", "Multi-ticket check", default);
                Check(await db.FilprideTugboatPaymentAllocations.CountAsync(a => new[] { firstLine, secondLine }.Contains(a.OtherTugChargeLineId) && a.ReleasedAt != null) == 2,
                    "Voiding did not release every selected ticket.");
                await service.ChangeStatusAsync(remainder, "Cancel", "Payment check", "Multi-ticket check", default);
                int replacement = await service.CreateAsync(Selection(firstLine, remainingLine, secondLine), null, "Payment check", default);
                await service.ChangeStatusAsync(replacement, "Cancel", "Payment check", "Multi-ticket check", default);
                Console.WriteLine("PASS: individual tickets across provider bills, unchecked remainder payment, mixed VAT, cumulative rounding, wrong-owner/forged/duplicate IDs, multi-ticket posting/rebuild/unpost/void/cancel and safe rollback guard.");
            }

            ApplicationDbContext Open()
            {
                return new ApplicationDbContext(options);
            }
            TugboatPaymentViewModel Form()
            {
                return new TugboatPaymentViewModel
                {
                    OwnerId = owner.TugboatOwnerId,
                    OldCVNo = "LEGACY-PAYMENT-CHECK",
                    AllocationIds = charge.Lines.Select(line => line.OtherTugChargeLineId).ToArray(),
                    BankId = bank.BankAccountId,
                    CheckNo = "T" + Guid.NewGuid().ToString("N"),
                    Date = charge.DocumentDate,
                    CheckDate = charge.DocumentDate,
                    Particulars = "Isolated AP Other Tugs settlement check"
                };
            }
            async Task<bool> RaceAsync()
            {
                await using var competing = Open();
                try
                { await Service(competing, storage).CreateAsync(Form(), null, "Payment check", default); return true; }
                catch (InvalidOperationException) { return false; }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { return false; }
            }
            async Task RejectSqlAsync(string sql)
            {
                await using var isolated = Open();
                try
                { await isolated.Database.ExecuteSqlRawAsync(sql); }
                catch (PostgresException ex) when (ex.SqlState == "P0001") { return; }
                throw new InvalidOperationException("Reserved source mutation was accepted: " + sql);
            }
            async Task CheckLedgerAsync(string number, int count, decimal total)
            {
                var rows = await db.FilprideGeneralLedgerBooks.AsNoTracking().Where(g => g.Reference == number).ToListAsync();
                Check(rows.Count == count && rows.Sum(g => g.Debit) == total && rows.Sum(g => g.Credit) == total
                    && rows.All(g => g.CounterpartyType == CounterpartyType.TugboatOwner && g.CounterpartyId == owner.TugboatOwnerId),
                    "Payment ledger is unbalanced, duplicated or lost owner identity.");
                Check(!await db.FilprideJournalVoucherHeaders.AnyAsync(j => j.References == reference)
                    && !await db.FilprideGeneralLedgerBooks.AnyAsync(g => g.Reference == number && g.Account.FinancialStatementType == "PnL"),
                    "Direct payment unexpectedly created initial expense/AP recognition.");
                decimal payable = rows.Where(g => g.AccountNo == TugboatPaymentService.ApAccount).Sum(g => g.Credit - g.Debit);
                Check(payable == (count == 3 ? -1120m : 0m), "Direct AP payment/reversal posted the wrong control-account amount.");
            }
        }

        private static TugboatPaymentService Service(ApplicationDbContext db, Storage storage)
        {
            return new TugboatPaymentService(db, new UnitOfWork(db), new CheckVoucherDocumentationService(db), storage,
                NullLogger<TugboatPaymentService>.Instance);
        }

        private static async Task RejectAsync(Func<Task> action, string message)
        {
            try
            { await action(); }
            catch (InvalidOperationException) { return; }
            catch (System.ComponentModel.DataAnnotations.ValidationException) { return; }
            throw new InvalidOperationException(message);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            { throw new InvalidOperationException(message); }
        }

        private sealed class Storage: ICloudStorageService
        {
            public bool Fail { get; set; }
            public bool Deleted { get; private set; }
            public Task<string> UploadFileAsync(IFormFile fileToUpload, string fileNameToSave)
            {
                if (Fail)
                { throw new InvalidOperationException("Fixture upload failure."); }
                return Task.FromResult("fixture://payment");
            }
            public Task DeleteFileAsync(string fileNameToDelete) { Deleted = true; return Task.CompletedTask; }
            public Task<string> GetSignedUrlAsync(string fileNameToRead, int timeOutInMinutes = 30) { return Task.FromResult("fixture://payment"); }
            public Task<Stream> DownloadFileAsync(string fileNameToDownload) { throw new NotSupportedException(); }
            public Task<IFormFile?> GetFileAsFormFile(string fileName) { return Task.FromResult<IFormFile?>(null); }
        }
    }
}
