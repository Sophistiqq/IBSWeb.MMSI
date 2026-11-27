using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.Models.Filpride.MasterFile;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Services;
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
            await CheckLongReferenceReopenAndReconfirmAsync(connection);

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

        private static async Task CheckLongReferenceReopenAndReconfirmAsync(string connection)
        {
            const string longRef = "SUBCON-INV-2026-LONG-REF-0001";
            string? expectedGlRef = null;
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>().UseNpgsql(connection).Options);
            await using var accounting = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);

            var addedContraAccount = false;
            if (!await accounting.FilprideChartOfAccounts.AnyAsync(a => a.AccountNumber == OtherTugChargeService.ContraRevenueAccount))
            {
                accounting.FilprideChartOfAccounts.Add(new FilprideChartOfAccount
                {
                    AccountNumber = OtherTugChargeService.ContraRevenueAccount,
                    AccountName = "Services by Other Tugs",
                    AccountType = "Revenue",
                    NormalBalance = "Credit",
                    Level = 4,
                    FinancialStatementType = "PnL",
                    CreatedBy = "Long-ref check"
                });
                await accounting.SaveChangesAsync();
                addedContraAccount = true;
            }

            var actor = await db.Users.AsNoTracking().FirstAsync(u => u.IsActive && u.Name != null && u.Name != "");
            var external = await db.MsapTugboats.AsNoTracking().FirstAsync(t => !t.IsCompanyOwned && t.TugboatOwnerId != null);
            var owner = await db.MsapTugboatOwners.SingleAsync(o => o.TugboatOwnerId == external.TugboatOwnerId);
            var originalAddress = owner.Address;
            var originalTin = owner.Tin;
            var originalVatType = owner.VatType;
            var originalTaxType = owner.TaxType;
            var originalWht = owner.WithholdingTaxPercent;

            var template = await db.MsapDispatchTickets
                .Include(t => t.ExpectedServiceEntry)
                .Where(t => t.Customer.Company == MsapConstants.Company_MMSI
                    && t.CustomerOrderSlipId != null
                    && (t.CustomerOrderSlip!.Status == MsapConstants.CustomerOrderSlipStatus.Open
                        || t.CustomerOrderSlip.Status == MsapConstants.CustomerOrderSlipStatus.Closed)
                    && t.VesselScheduleServiceEntryId != null
                    && t.ExpectedServiceEntry != null
                    && t.ExpectedServiceEntry.CancelledAt == null
                    && t.Status != MsapConstants.DispatchTicketStatus.Deleted
                    && !db.MsapVesselSchedules.Any(s => s.CustomerOrderSlipId == t.CustomerOrderSlipId
                        && (s.ApprovedRevision != s.Revision
                            || s.Status == MsapConstants.VesselScheduleStatus.Tentative
                            || s.Status == MsapConstants.VesselScheduleStatus.Cancelled)))
                .OrderByDescending(t => t.DispatchTicketId)
                .FirstAsync();

            var today = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime());
            var serviceEntry = (VesselScheduleServiceEntry)db.Entry(template.ExpectedServiceEntry!).CurrentValues.ToObject();
            serviceEntry.VesselScheduleServiceEntryId = 0;
            serviceEntry.TugboatId = external.TugboatId;
            serviceEntry.CancelledAt = null;
            serviceEntry.CancelledByUserId = null;
            serviceEntry.CancellationReason = null;

            var testTicket = (DispatchTicket)db.Entry(template).CurrentValues.ToObject();
            testTicket.DispatchTicketId = 0;
            testTicket.AccountingSourceId = Guid.NewGuid();
            testTicket.DispatchNumber = "LR" + Guid.NewGuid().ToString("N")[..16];
            testTicket.TugBoatId = external.TugboatId;
            testTicket.BillingId = null;
            testTicket.DateLeft = today.AddDays(-1);
            testTicket.DateArrived = today.AddDays(-1);
            testTicket.TimeLeft = new TimeOnly(8, 0);
            testTicket.TimeArrived = new TimeOnly(10, 0);
            testTicket.ApOtherTugs = 0m;
            testTicket.ApOtherTugsIncludesVat = false;

            int? chargeId = null;
            try
            {
                owner.Address = "Long-ref check fixture";
                owner.Tin = "000-000-000-00000";
                owner.VatType = "Vatable";
                owner.TaxType = "Withholding Tax";
                owner.WithholdingTaxPercent = 0.02m;

                db.Add(serviceEntry);
                await db.SaveChangesAsync();
                testTicket.VesselScheduleServiceEntryId = serviceEntry.VesselScheduleServiceEntryId;
                db.Add(testTicket);
                await db.SaveChangesAsync();

                var cosId = testTicket.CustomerOrderSlipId!.Value;
                var service = new OtherTugChargeService(db, null!, NullLogger<OtherTugChargeService>.Instance);

                var created = await service.SaveAsync(new OtherTugChargeViewModel
                {
                    CustomerOrderSlipId = cosId,
                    TugboatOwnerId = owner.TugboatOwnerId,
                    ProviderReference = longRef,
                    DocumentDate = today,
                    IncludesVat = true,
                    Allocations = [new OtherTugChargeLineViewModel { DispatchTicketId = testTicket.DispatchTicketId, Amount = 1120m }]
                }, null, actor.Id, default);
                Check(created.IsSuccess, created.Message ?? "Failed to save long-reference provider charge.");
                chargeId = created.Data;
                expectedGlRef = $"OTC-{chargeId.Value}";

                var confirmed = await service.ConfirmAsync(chargeId.Value, 1, actor.Id, default);
                Check(confirmed.IsSuccess, confirmed.Message ?? "Failed to confirm long-reference provider charge.");
                var glConfirmed = await accounting.FilprideGeneralLedgerBooks.AsNoTracking()
                    .IgnoreQueryFilters(["TugboatWorkspace"])
                    .Where(g => g.WorkspaceTugboatId == null && g.Reference == expectedGlRef
                        && g.CounterpartyId == owner.TugboatOwnerId)
                    .ToListAsync();
                Check(glConfirmed.Count == 3
                    && glConfirmed.Single(g => g.AccountNo == OtherTugChargeService.ContraRevenueAccount).Debit == 1000m
                    && glConfirmed.Single(g => g.AccountNo == OtherTugChargeService.InputVatAccount).Debit == 120m
                    && glConfirmed.Single(g => g.AccountNo == TugboatPaymentService.ApAccount).Credit == 1120m,
                    "Initial confirmation did not post expected truncated-reference GL entries.");

                var reopened = await service.ReopenConfirmedChargeAsync(chargeId.Value, "Long-reference reopen regression check", actor.Id, default);
                Check(reopened.IsSuccess, reopened.Message ?? "Failed to reopen long-reference provider charge.");
                var glAfterReopen = await accounting.FilprideGeneralLedgerBooks.AsNoTracking()
                    .IgnoreQueryFilters(["TugboatWorkspace"])
                    .Where(g => g.WorkspaceTugboatId == null && g.Reference == expectedGlRef
                        && g.CounterpartyId == owner.TugboatOwnerId)
                    .ToListAsync();
                Check(glAfterReopen.Count == 0,
                    "ReopenConfirmedChargeAsync failed to reverse GL entries when ProviderReference exceeds 20 characters.");

                var edited = await service.SaveAsync(new OtherTugChargeViewModel
                {
                    Id = chargeId.Value,
                    Revision = 2,
                    CustomerOrderSlipId = cosId,
                    TugboatOwnerId = owner.TugboatOwnerId,
                    ProviderReference = longRef,
                    DocumentDate = today,
                    IncludesVat = true,
                    Allocations = [new OtherTugChargeLineViewModel { DispatchTicketId = testTicket.DispatchTicketId, Amount = 2240m }]
                }, null, actor.Id, default);
                Check(edited.IsSuccess, edited.Message ?? "Failed to edit reopened long-reference provider charge.");

                var reconfirmed = await service.ConfirmAsync(chargeId.Value, 3, actor.Id, default);
                Check(reconfirmed.IsSuccess, reconfirmed.Message ?? "Failed to re-confirm edited long-reference provider charge.");
                var glReconfirmed = await accounting.FilprideGeneralLedgerBooks.AsNoTracking()
                    .IgnoreQueryFilters(["TugboatWorkspace"])
                    .Where(g => g.WorkspaceTugboatId == null && g.Reference == expectedGlRef
                        && g.CounterpartyId == owner.TugboatOwnerId)
                    .ToListAsync();
                Check(glReconfirmed.Count == 3
                    && glReconfirmed.Single(g => g.AccountNo == OtherTugChargeService.ContraRevenueAccount).Debit == 2000m
                    && glReconfirmed.Single(g => g.AccountNo == OtherTugChargeService.InputVatAccount).Debit == 240m
                    && glReconfirmed.Single(g => g.AccountNo == TugboatPaymentService.ApAccount).Credit == 2240m,
                    "Re-confirmation did not post updated GL entries after reversing truncated-reference entries.");

                Console.WriteLine("PASS: OtherTugChargeService confirm -> reopen -> edit -> re-confirm with >20-char ProviderReference reverses and re-posts GL entries accurately.");
            }
            finally
            {
                accounting.ChangeTracker.Clear();
                if (expectedGlRef != null)
                {
                    var glToRemove = await accounting.FilprideGeneralLedgerBooks.IgnoreQueryFilters(["TugboatWorkspace"])
                        .Where(g => g.WorkspaceTugboatId == null && g.Reference == expectedGlRef
                            && g.CounterpartyId == owner.TugboatOwnerId)
                        .ToListAsync();
                    if (glToRemove.Count != 0)
                    {
                        accounting.FilprideGeneralLedgerBooks.RemoveRange(glToRemove);
                    }
                }

                var accountingAudits = await accounting.FilprideAuditTrails
                    .Where(a => a.DocumentType == "Other Tug Charges" && a.Activity.Contains(longRef))
                    .ToListAsync();
                if (accountingAudits.Count != 0)
                {
                    accounting.FilprideAuditTrails.RemoveRange(accountingAudits);
                }

                if (addedContraAccount)
                {
                    var contra = await accounting.FilprideChartOfAccounts
                        .SingleOrDefaultAsync(a => a.AccountNumber == OtherTugChargeService.ContraRevenueAccount);
                    if (contra != null)
                    {
                        accounting.FilprideChartOfAccounts.Remove(contra);
                    }
                }

                await accounting.SaveChangesAsync();

                db.ChangeTracker.Clear();
                if (chargeId.HasValue)
                {
                    var audits = await db.AuditTrails
                        .Where(a => a.DocumentType == "Other Tug Charges" && a.RecordId == chargeId.Value)
                        .ToListAsync();
                    db.AuditTrails.RemoveRange(audits);
                    var persistedCharge = await db.MsapOtherTugCharges.Include(c => c.Lines)
                        .SingleOrDefaultAsync(c => c.OtherTugChargeId == chargeId.Value);
                    if (persistedCharge != null)
                    {
                        db.MsapOtherTugChargeLines.RemoveRange(persistedCharge.Lines);
                        db.MsapOtherTugCharges.Remove(persistedCharge);
                    }
                }

                if (testTicket.DispatchTicketId != 0)
                {
                    var persistedTicket = await db.MsapDispatchTickets.FindAsync(testTicket.DispatchTicketId);
                    if (persistedTicket != null)
                    {
                        db.MsapDispatchTickets.Remove(persistedTicket);
                    }
                }

                if (serviceEntry.VesselScheduleServiceEntryId != 0)
                {
                    var persistedEntry = await db.MsapVesselScheduleServiceEntries.FindAsync(serviceEntry.VesselScheduleServiceEntryId);
                    if (persistedEntry != null)
                    {
                        db.MsapVesselScheduleServiceEntries.Remove(persistedEntry);
                    }
                }

                var persistedOwner = await db.MsapTugboatOwners.SingleAsync(o => o.TugboatOwnerId == owner.TugboatOwnerId);
                persistedOwner.Address = originalAddress;
                persistedOwner.Tin = originalTin;
                persistedOwner.VatType = originalVatType;
                persistedOwner.TaxType = originalTaxType;
                persistedOwner.WithholdingTaxPercent = originalWht;
                await db.SaveChangesAsync();
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
