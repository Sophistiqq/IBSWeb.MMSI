using System.Text.Json;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.Models;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class OperationalRateDatabaseCheck
    {
        public static async Task RunAsync()
        {
            Dictionary<string, string> before = await RebuildDatabaseCheck.AuditSourceAsync();
            string database = await TugboatWorkspaceDatabaseCheck.RunAsync();
            string? previous = Environment.GetEnvironmentVariable("IBS_REBUILD_TEST_DATABASE");
            try
            {
                Environment.SetEnvironmentVariable("IBS_REBUILD_TEST_DATABASE", database);
                await CheckCaptureAsync();
            }
            finally
            {
                Environment.SetEnvironmentVariable("IBS_REBUILD_TEST_DATABASE", previous);
            }
            Dictionary<string, string> after = await RebuildDatabaseCheck.AuditSourceAsync();
            Check(before.OrderBy(item => item.Key).SequenceEqual(after.OrderBy(item => item.Key)), "Source database changed during verification.");
            Console.WriteLine("PASS: operational rate capture verified on a disposable clone; source fingerprints and migration histories unchanged.");
        }

        private static async Task CheckCaptureAsync()
        {
            string connection = TugboatOwnerCheck.TestConnection();
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(connection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options);
            await db.Database.MigrateAsync();
            Check(!db.Database.HasPendingModelChanges(), "The tariff source migration differs from the model.");
            await using var shared = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
            var work = new UnitOfWork(db, shared);
            var rates = new RateUploadService(db, work);
            Customer customer = await db.Customers.FirstAsync(item => item.Company == MsapConstants.Company_MMSI);
            Terminal terminal = await db.MsapTerminals.FirstAsync();
            Vessel vessel = await db.MsapVessels.FirstAsync();
            Tugboat tug = await db.MsapTugboats.FirstAsync();
            var service = new Service { ServiceNumber = "RC1", ServiceName = "Operational rate capture check" };
            var actor = new ApplicationUser { Id = "operational-rate-check", UserName = "operational-rate-check", Name = "Rate Reviewer", IsActive = true, Department = "MIS" };
            var entry = new VesselScheduleServiceEntry
            {
                TugboatId = tug.TugboatId, Service = service, TariffBasis = TariffChargeBasis.PerHour,
                BafMethod = BafCalculationMethod.PerHour, ManualTariffRate = 123.4567m, ManualBafRate = 0m
            };
            var schedule = new VesselSchedule
            {
                CustomerId = customer.CustomerId, PortId = terminal.PortId, TerminalId = terminal.TerminalId, VesselId = vessel.VesselId,
                VoyageNumber = "RATE-CHECK", CreatedDate = new DateTime(2026, 10, 20, 8, 0, 0),
                PlannedStart = new DateTime(2026, 10, 20, 8, 0, 0), PlannedEnd = new DateTime(2026, 10, 20, 9, 0, 0),
                AssignedTugboatIds = JsonSerializer.Serialize(new[] { tug.TugboatId }), ServiceEntries = [entry]
            };
            db.AddRange(actor, schedule);
            await db.SaveChangesAsync();
            int id = schedule.VesselScheduleId;
            int entryId = entry.VesselScheduleServiceEntryId;
            var date = new DateOnly(2026, 10, 20);
            ServiceResult<ScheduleRateCaptureViewModel> preview = await rates.PreviewScheduleCaptureAsync(id, entryId, RateTableKind.Tariff, date, default);
            Check(preview.IsSuccess && preview.Data!.Rate.Amount == 123.4567m, preview.Message ?? "Trusted source preview failed.");
            ServiceResult<Guid> stale = await rates.CaptureScheduleRateAsync(id, entryId, RateTableKind.Tariff, date, "stale", actor.Id, default);
            Check(!stale.IsSuccess && !await db.MsapTariffRates.AnyAsync(item => item.ServiceId == service.ServiceId), "A stale capture activated a rate.");
            ServiceResult<Guid> saved = await rates.CaptureScheduleRateAsync(id, entryId, RateTableKind.Tariff, date, preview.Data!.Fingerprint, actor.Id, default);
            Check(saved.IsSuccess, saved.Message ?? "Basic capture failed.");
            ServiceResult<Guid> retry = await rates.CaptureScheduleRateAsync(id, entryId, RateTableKind.Tariff, date, preview.Data.Fingerprint, actor.Id, default);
            Check(retry.IsSuccess && retry.Data == saved.Data && await db.MsapTariffRates.CountAsync(item => item.ServiceId == service.ServiceId) == 1, "A retry duplicated the rate.");
            RateUploadViewModel? evidence = await rates.GetAsync(saved.Data, actor.Id, default);
            Check(evidence?.Upload?.ApprovedAt != null && evidence.Rows.Single().SourceServiceEntryId == entryId, "Capture lost approval or source evidence.");
            Check(entry.ManualTariffRate == 123.4567m && entry.ManualBafRate == 0m, "Capture rewrote the source schedule.");
            ServiceResult<ScheduleRateCaptureViewModel> bafPreview = await rates.PreviewScheduleCaptureAsync(id, entryId, RateTableKind.BAF, date, default);
            Check(bafPreview.IsSuccess, bafPreview.Message ?? "Explicit zero BAF preview failed.");
            ServiceResult<Guid> bafSaved = await rates.CaptureScheduleRateAsync(id, entryId, RateTableKind.BAF, date, bafPreview.Data!.Fingerprint, actor.Id, default);
            Check(bafSaved.IsSuccess, bafSaved.Message ?? "Explicit zero BAF capture failed.");
            Check((await rates.GetScheduleCapturesAsync(schedule, default)).All(item => item.PublishedUpdateId.HasValue), "Published rates still appeared unsaved.");
            var otherEntry = new VesselScheduleServiceEntry
            {
                VesselScheduleServiceEntryId = -1,
                TugboatId = (await db.MsapTugboats.FirstAsync(item => item.TugboatId != tug.TugboatId)).TugboatId,
                ServiceId = service.ServiceId, TariffBasis = entry.TariffBasis, BafMethod = entry.BafMethod,
                ManualTariffRate = entry.ManualTariffRate, ManualBafRate = entry.ManualBafRate
            };
            schedule.ServiceEntries.Add(otherEntry);
            List<ScheduleRateCaptureViewModel> otherCaptures = (await rates.GetScheduleCapturesAsync(schedule, default))
                .Where(item => item.EntryId == otherEntry.VesselScheduleServiceEntryId).ToList();
            Check(otherCaptures.Count == 2 && otherCaptures.All(item => item.CanCapture && !item.PublishedUpdateId.HasValue),
                "Saving one tug's service hid the save buttons for another tug with the same manual rates.");
            otherEntry.ManualTariffRate = 999m;
            otherEntry.ManualBafRate = 999m;
            Check((await rates.GetScheduleCapturesAsync(schedule, default)).Where(item => item.EntryId == otherEntry.VesselScheduleServiceEntryId)
                .All(item => !item.CanCapture), "Conflicting manual overrides were offered for capture.");
            schedule.ServiceEntries.Remove(otherEntry);
            Check(await work.TariffTable.GetApplicableRateAsync(customer.CustomerId, terminal.TerminalId, service.ServiceId + 1000000, date, default) == null
                && await work.BafTable.GetApplicableRateAsync(customer.CustomerId, terminal.TerminalId, service.ServiceId + 1000000, date, default) == null,
                "An exact service miss fell back to another service.");
            TariffRate? basic = await work.TariffTable.GetApplicableRateAsync(customer.CustomerId, terminal.TerminalId, service.ServiceId, date, default);
            BafRate? baf = await work.BafTable.GetApplicableRateAsync(customer.CustomerId, terminal.TerminalId, service.ServiceId, date, default);
            Check(basic?.Dispatch == 123.4567m && baf?.Amount == 0m, "Exact lookup did not find the captured rates.");
            entry.CancelledAt = new DateTime(2026, 10, 20);
            Check(!(await rates.PreviewScheduleCaptureAsync(id, entryId, RateTableKind.BAF, date, default)).IsSuccess, "Cancelled service was publishable.");
            entry.CancelledAt = null;

            var job = new CustomerOrderSlip
            {
                CustomerOrderSlipNumber = "RATE-CHECK", Date = date, CustomerId = customer.CustomerId, CreatedBy = actor.UserName, CreatedDate = schedule.CreatedDate,
                VesselId = vessel.VesselId, PortId = terminal.PortId, TerminalId = terminal.TerminalId, Status = MsapConstants.CustomerOrderSlipStatus.Open
            };
            var tableEntry = new VesselScheduleServiceEntry
            {
                TugboatId = tug.TugboatId, ServiceId = service.ServiceId, TariffBasis = TariffChargeBasis.PerHour,
                BafMethod = BafCalculationMethod.PerHour, ManualTariffRate = 123.4567m, IsManualTariffOverride = false
            };
            var tableSchedule = new VesselSchedule
            {
                CustomerOrderSlip = job, CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = terminal.PortId, TerminalId = terminal.TerminalId,
                PlannedStart = schedule.PlannedStart, PlannedEnd = schedule.PlannedEnd, ServiceEntries = [tableEntry],
                Revision = 1, ApprovedRevision = 1, ApprovedByUserId = actor.Id, ApprovedAt = schedule.CreatedDate, Status = MsapConstants.VesselScheduleStatus.Confirmed
            };
            db.Add(tableSchedule);
            await db.SaveChangesAsync();
            var ticket = new DispatchTicket
            {
                DispatchNumber = "RATE-CHECK", CustomerOrderSlipId = job.CustomerOrderSlipId, CustomerId = customer.CustomerId, VesselId = vessel.VesselId,
                CreatedBy = actor.UserName, CreatedDate = schedule.CreatedDate,
                PortId = terminal.PortId, TerminalId = terminal.TerminalId, TugBoatId = tug.TugboatId, ServiceId = service.ServiceId,
                VesselScheduleServiceEntryId = tableEntry.VesselScheduleServiceEntryId, Date = date, DateLeft = date, DateArrived = date,
                TimeLeft = new TimeOnly(8, 0), TimeArrived = new TimeOnly(9, 0)
            };
            Check(ScheduledTicketCharges.Apply(ticket, tableSchedule, tableEntry, null, baf) == null
                && ticket.IsManualTariffRate, "Missing-table tariff did not use the supplied manual fallback.");
            db.Add(ticket);
            await db.SaveChangesAsync();
            (DispatchTicket Ticket, TariffRate? Tariff, BafRate? Baf) equalPreview = await ScheduledTicketCharges.PreviewAsync(work, ticket, tableSchedule, default);
            Check(!equalPreview.Ticket.IsManualTariffRate && !ScheduledTicketCharges.HasChanges(ticket, equalPreview.Ticket),
                "Publishing an equal tariff unnecessarily required recalculation.");
            foreach (RateTableKind kind in Enum.GetValues<RateTableKind>())
            {
                if (kind == RateTableKind.BAF)
                {
                    tableEntry.ManualTariffRate = 123.4567m;
                    tableEntry.IsManualTariffOverride = true;
                    tableEntry.ManualBafRate = 0m;
                    Check(ScheduledTicketCharges.Apply(ticket, tableSchedule, tableEntry, basic, null) == null
                        && ticket.IsManualBafRate, "Could not prepare a saved manual BAF ticket.");
                    await db.SaveChangesAsync();
                }
                var correction = new RateEditorViewModel { Kind = kind, Rows = [new RateUploadRowViewModel
                {
                    CustomerId = customer.CustomerId, PortId = terminal.PortId, TerminalId = terminal.TerminalId,
                    ServiceId = service.ServiceId, EffectiveDate = date, Amount = kind == RateTableKind.Tariff ? 150m : 10m
                }] };
                ServiceResult<Guid> draft = await rates.SaveDraftAsync(correction, actor.Id, default);
                Check(draft.IsSuccess && (await rates.SaveAsync(draft.Data, actor.Id, false, [1], default)).IsSuccess
                    && (await rates.ApproveAsync(draft.Data, actor.Id, default)).IsSuccess, "Existing editor approval failed.");
                RateUploadViewModel? impact = await rates.GetAsync(draft.Data, actor.Id, default);
                Check(impact!.AffectedTickets.Any(item => item.DispatchTicketId == ticket.DispatchTicketId), "Rate correction lost the affected-ticket list.");
                Check(ticket.DispatchRate == 123.4567m && ticket.BAFRate == 0m, "Rate editing silently repriced a saved ticket.");
                if (kind == RateTableKind.Tariff)
                {
                    var dispatch = new DispatchTicketService(work, null!, NullLogger<DispatchTicketService>.Instance);
                    JobProgressViewModel progress = await JobProgressCalculator.LoadAsync(work, job, tableSchedule, default);
                    Check(progress.CalculationBlockers.ContainsKey(ticket.DispatchTicketId), "A tariff correction did not block unreviewed billing.");
                    ServiceResult<TicketChargeReviewViewModel> review = await dispatch.PreviewChargesAsync(ticket.DispatchTicketId, job.CustomerOrderSlipId, default);
                    Check(review.IsSuccess && review.Data!.SavedTariffRate == 123.4567m && review.Data.ProposedTariffRate == 150m,
                        "Tariff review did not show the saved and corrected amounts.");
                    Check(!(await dispatch.RecalculateChargesAsync(ticket.DispatchTicketId, job.CustomerOrderSlipId, actor.UserName!, default, "stale")).IsSuccess
                        && ticket.DispatchRate == 123.4567m, "A stale tariff review changed saved charges.");
                    ServiceResult recalculated = await dispatch.RecalculateChargesAsync(ticket.DispatchTicketId, job.CustomerOrderSlipId, actor.UserName!, default, review.Data!.Fingerprint);
                    Check(recalculated.IsSuccess && ticket.DispatchRate == 150m && !ticket.IsManualTariffRate
                        && tableEntry.ManualTariffRate == 123.4567m, recalculated.Message ?? "Reviewed tariff recalculation failed.");
                    Check(await db.AuditTrails.AnyAsync(audit => audit.RecordId == ticket.DispatchTicketId
                        && audit.DocumentType == "Dispatch Ticket" && audit.Activity.Contains("tariff 123.4567 -> 150")),
                        "Recalculation did not audit the tariff correction.");
                    progress = await JobProgressCalculator.LoadAsync(work, job, tableSchedule, default);
                    Check(!progress.CalculationBlockers.ContainsKey(ticket.DispatchTicketId), "Reviewed tariff correction left an obsolete billing blocker.");
                    correction.Rows[0].EffectiveDate = date.AddDays(1);
                    correction.Rows[0].Amount = 200m;
                    ServiceResult<Guid> futureDraft = await rates.SaveDraftAsync(correction, actor.Id, default);
                    Check(futureDraft.IsSuccess && (await rates.SaveAsync(futureDraft.Data, actor.Id, false, [1], default)).IsSuccess
                        && (await rates.ApproveAsync(futureDraft.Data, actor.Id, default)).IsSuccess, "New dated tariff approval failed.");
                    RateUploadViewModel? futureImpact = await rates.GetAsync(futureDraft.Data, actor.Id, default);
                    Check(!futureImpact!.AffectedTickets.Any(item => item.DispatchTicketId == ticket.DispatchTicketId)
                        && !ScheduledTicketCharges.HasChanges(ticket, (await ScheduledTicketCharges.PreviewAsync(work, ticket, tableSchedule, default)).Ticket),
                        "A future tariff affected an earlier actual service.");
                    ticket.DateLeft = ticket.DateArrived = date.AddDays(1);
                    await db.SaveChangesAsync();
                    futureImpact = await rates.GetAsync(futureDraft.Data, actor.Id, default);
                    Check(futureImpact!.AffectedTickets.Any(item => item.DispatchTicketId == ticket.DispatchTicketId)
                        && (await ScheduledTicketCharges.PreviewAsync(work, ticket, tableSchedule, default)).Ticket.DispatchRate == 200m,
                        "A new tariff was ignored on its effective actual service date.");
                    ticket.Status = MsapConstants.DispatchTicketStatus.Billed;
                    await db.SaveChangesAsync();
                    Check(!(await dispatch.RecalculateChargesAsync(ticket.DispatchTicketId, job.CustomerOrderSlipId, actor.UserName!, default, review.Data.Fingerprint)).IsSuccess,
                        "A billed ticket allowed tariff recalculation.");
                    ticket.Status = MsapConstants.DispatchTicketStatus.ForBilling;
                    ticket.DateLeft = ticket.DateArrived = date;
                    await db.SaveChangesAsync();
                }
                if (kind == RateTableKind.BAF)
                {
                    var dispatch = new DispatchTicketService(work, null!, NullLogger<DispatchTicketService>.Instance);
                    var review = await dispatch.PreviewChargesAsync(ticket.DispatchTicketId, job.CustomerOrderSlipId, default);
                    Check(review.IsSuccess && review.Data!.ProposedBafRate == 10m, "Manual BAF review did not propose the updated table amount.");
                    Check(!(await dispatch.RecalculateChargesAsync(ticket.DispatchTicketId, job.CustomerOrderSlipId, actor.UserName!, default, "stale")).IsSuccess
                        && ticket.BAFRate == 0m, "A stale review changed saved manual charges.");
                    var recalculated = await dispatch.RecalculateChargesAsync(ticket.DispatchTicketId, job.CustomerOrderSlipId, actor.UserName!, default, review.Data!.Fingerprint);
                    Check(recalculated.IsSuccess && ticket.BAFRate == 10m && !ticket.IsManualBafRate
                        && ticket.DispatchRate == 123.4567m && ticket.IsManualTariffRate, recalculated.Message ?? "Reviewed BAF recalculation failed.");
                    Check(tableEntry.ManualBafRate == 0m, "Recalculation rewrote approved schedule terms.");
                    var progress = await JobProgressCalculator.LoadAsync(work, job, tableSchedule, default);
                    Check(!progress.CalculationBlockers.ContainsKey(ticket.DispatchTicketId), "Recalculated BAF remained blocked by the old manual amount.");
                }
            }
            var schedules = new VesselScheduleService(work, new CustomerOrderSlipService(work, NullLogger<CustomerOrderSlipService>.Instance), NullLogger<VesselScheduleService>.Instance);
            schedule.Revision++;
            await db.SaveChangesAsync();
            (ServiceResult<int> Result, string? Acknowledgement) warning = await rates.ConfirmScheduleAsync(schedules, id, schedule.CreatedDate, true, null, actor.UserName, actor.Id, default);
            Check(warning.Acknowledgement != null && !warning.Result.IsSuccess, "Unpublished manual rates bypassed server acknowledgement.");
            Console.WriteLine("PASS: trusted direct capture, zero BAF, idempotency, source evidence, exact matching, stale/cancelled rejection, existing editor corrections, Basic/BAF impact lists and unchanged ticket amounts (isolated clone).");
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
