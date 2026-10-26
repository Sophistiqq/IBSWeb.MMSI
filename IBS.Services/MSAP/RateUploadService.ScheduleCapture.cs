using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBS.Models;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.EntityFrameworkCore;

namespace IBS.Services.MSAP
{
    public partial class RateUploadService
    {
        public async Task<List<ScheduleRateCaptureViewModel>> GetScheduleCapturesAsync(VesselSchedule schedule, CancellationToken ct)
        {
            string source = JsonSerializer.Serialize(new[] { new { SourceScheduleId = schedule.VesselScheduleId, SourceRevision = schedule.Revision } });
            List<RateUpload> published = await _db.MsapRateUploads
                .FromSqlInterpolated($"SELECT * FROM msap.mmsi_rate_uploads WHERE rows_json @> {source}::jsonb")
                .AsNoTracking().Where(update => update.Company == MsapConstants.Company_MMSI && update.ApprovedAt != null).ToListAsync(ct);
            var result = new List<ScheduleRateCaptureViewModel>();
            DateOnly plannedDate = DateOnly.FromDateTime(schedule.PlannedStart);
            foreach (VesselScheduleServiceEntry entry in schedule.ServiceEntries.Where(entry => !entry.CancelledAt.HasValue))
            {
                foreach (RateTableKind kind in Enum.GetValues<RateTableKind>())
                {
                    decimal? amount = kind == RateTableKind.Tariff ? entry.ManualTariffRate : entry.ManualBafRate;
                    if (!amount.HasValue)
                    {
                        continue;
                    }
                    RateUpload? captured = published.FirstOrDefault(update => update.RateKind == kind
                        && JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(update.RowsJson)!.Any(row => row.Selected
                            && row.SourceScheduleId == schedule.VesselScheduleId && row.SourceRevision == schedule.Revision
                            && row.SourceServiceEntryId == entry.VesselScheduleServiceEntryId && row.Amount == amount));
                    bool eligible;
                    if (kind == RateTableKind.Tariff)
                    {
                        TariffRate? rate = await _work.TariffTable.GetApplicableRateAsync(schedule.CustomerId.GetValueOrDefault(), schedule.TerminalId, entry.ServiceId, plannedDate, ct);
                        eligible = rate == null || rate.Dispatch == amount;
                    }
                    else
                    {
                        BafRate? rate = await _work.BafTable.GetApplicableRateAsync(schedule.CustomerId.GetValueOrDefault(), schedule.TerminalId, entry.ServiceId, plannedDate, ct);
                        eligible = rate == null || rate.AsOfDate < ScheduledTicketCharges.BafCycleStart(plannedDate) || rate.Amount == amount;
                    }
                    result.Add(new ScheduleRateCaptureViewModel
                    {
                        ScheduleId = schedule.VesselScheduleId, EntryId = entry.VesselScheduleServiceEntryId, Kind = kind,
                        EffectiveDate = kind == RateTableKind.BAF ? ScheduledTicketCharges.BafCycleStart(plannedDate) : plannedDate,
                        PublishedUpdateId = captured?.RateUploadId,
                        CanCapture = captured == null && eligible && schedule.Status is not (MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                    });
                }
            }
            return result;
        }

        public async Task<ServiceResult<ScheduleRateCaptureViewModel>> PreviewScheduleCaptureAsync(int scheduleId, int entryId,
            RateTableKind kind, DateOnly effectiveDate, CancellationToken ct)
        {
            VesselSchedule? schedule = await _work.VesselSchedule.GetScheduleWithDetailsAsync(scheduleId, ct);
            return schedule == null
                ? ServiceResult<ScheduleRateCaptureViewModel>.Failure("Schedule not found.")
                : await BuildScheduleCaptureAsync(schedule, entryId, kind, effectiveDate, ct);
        }

        private async Task<ServiceResult<ScheduleRateCaptureViewModel>> BuildScheduleCaptureAsync(VesselSchedule schedule, int entryId,
            RateTableKind kind, DateOnly effectiveDate, CancellationToken ct)
        {
            VesselScheduleServiceEntry? entry = schedule.ServiceEntries.SingleOrDefault(entry => entry.VesselScheduleServiceEntryId == entryId);
            if (!Enum.IsDefined(kind) || entry is null || entry.CancelledAt.HasValue
                || schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
            {
                return ServiceResult<ScheduleRateCaptureViewModel>.Failure("Choose an active service and rate kind.");
            }
            if (kind == RateTableKind.BAF && effectiveDate.DayOfWeek != DayOfWeek.Tuesday)
            {
                return ServiceResult<ScheduleRateCaptureViewModel>.Failure("BAF effective date must be a Tuesday.");
            }
            if (kind == RateTableKind.Tariff && (!entry.TariffBasis.HasValue || !Enum.IsDefined(entry.TariffBasis.Value))
                || kind == RateTableKind.BAF && (!entry.BafMethod.HasValue || !Enum.IsDefined(entry.BafMethod.Value)))
            {
                return ServiceResult<ScheduleRateCaptureViewModel>.Failure("Review the service charging basis or BAF method before saving a reusable rate.");
            }
            var row = new RateUploadRowViewModel
            {
                CustomerId = schedule.CustomerId.GetValueOrDefault(), PortId = schedule.PortId, TerminalId = schedule.TerminalId,
                ServiceId = entry.ServiceId, EffectiveDate = effectiveDate,
                Amount = kind == RateTableKind.Tariff ? entry.ManualTariffRate : entry.ManualBafRate,
                SourceScheduleId = schedule.VesselScheduleId, SourceServiceEntryId = entryId, SourceRevision = schedule.Revision
            };
            List<string> errors = ValidateRows([row]);
            if (errors.Count > 0 || (kind == RateTableKind.Tariff && row.Amount is not > 0))
            {
                return ServiceResult<ScheduleRateCaptureViewModel>.Failure(errors.FirstOrDefault() ?? "Basic must be greater than zero.");
            }
            ServiceResult references = await PopulateReferencesAsync([row], ct);
            if (!references.IsSuccess)
            {
                return ServiceResult<ScheduleRateCaptureViewModel>.Failure(references.Message!);
            }
            await UpdateBaselineAsync([row], kind, true, ct);
            // A capture can add a dated rate, but corrections stay in the existing reviewed editor.
            if (row.ExistingRateId.HasValue && row.StoredAmount != row.Amount)
            {
                return ServiceResult<ScheduleRateCaptureViewModel>.Failure("A different rate already exists for this date. Use the rate editor to review a correction.");
            }
            DateOnly plannedDate = DateOnly.FromDateTime(schedule.PlannedStart);
            decimal? applicableAmount;
            if (kind == RateTableKind.Tariff)
            {
                applicableAmount = (await _work.TariffTable.GetApplicableRateAsync(row.CustomerId, row.TerminalId, row.ServiceId, plannedDate, ct))?.Dispatch;
            }
            else
            {
                BafRate? baf = await _work.BafTable.GetApplicableRateAsync(row.CustomerId, row.TerminalId, row.ServiceId, plannedDate, ct);
                applicableAmount = baf != null && baf.AsOfDate >= ScheduledTicketCharges.BafCycleStart(plannedDate) ? baf.Amount : null;
            }
            if (applicableAmount.HasValue && applicableAmount != row.Amount)
            {
                return ServiceResult<ScheduleRateCaptureViewModel>.Failure("This is an override of an applicable table rate. Keep it for this schedule or use the rate editor for reusable changes.");
            }
            var model = new ScheduleRateCaptureViewModel
            {
                ScheduleId = schedule.VesselScheduleId, EntryId = entryId, Kind = kind, EffectiveDate = effectiveDate, Rate = row,
                ChargingContext = kind == RateTableKind.Tariff ? entry.TariffBasis?.ToString() ?? "Not specified" : entry.BafMethod?.ToString() ?? "Not specified",
                CanCapture = true,
                Fingerprint = Fingerprint(new { schedule.VesselScheduleId, schedule.Revision, schedule.EditedDate, schedule.CreatedDate,
                    schedule.Status, schedule.PlannedStart, entry.TariffBasis, entry.BafMethod, kind, row, applicableAmount })
            };
            return ServiceResult<ScheduleRateCaptureViewModel>.Success(model);
        }

        public async Task<ServiceResult<Guid>> CaptureScheduleRateAsync(int scheduleId, int entryId, RateTableKind kind,
            DateOnly effectiveDate, string fingerprint, string userId, CancellationToken ct)
        {
            ApplicationUser? actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                return ServiceResult<Guid>.Failure("The account needs a full name of up to 255 characters before recording rate changes.");
            }
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            VesselSchedule? schedule = await _work.VesselSchedule.GetForUpdateAsync(scheduleId, ct);
            if (schedule == null)
            {
                return ServiceResult<Guid>.Failure("Schedule not found.");
            }
            await _db.Database.ExecuteSqlRawAsync("LOCK TABLE msap.mmsi_tariff_rates, msap.mmsi_baf_rates IN SHARE ROW EXCLUSIVE MODE", ct);
            ServiceResult<ScheduleRateCaptureViewModel> preview = await BuildScheduleCaptureAsync(schedule, entryId, kind, effectiveDate, ct);
            if (!preview.IsSuccess)
            {
                return ServiceResult<Guid>.Failure(preview.Message!);
            }
            RateUploadRowViewModel row = preview.Data!.Rate;
            ScheduleRateCaptureViewModel previous = (await GetScheduleCapturesAsync(schedule, ct)).Single(item => item.EntryId == entryId && item.Kind == kind);
            if (previous.PublishedUpdateId.HasValue)
            {
                return ServiceResult<Guid>.Success(previous.PublishedUpdateId.Value, "This rate was already saved for reuse.");
            }
            if (preview.Data.Fingerprint != fingerprint)
            {
                return ServiceResult<Guid>.Failure("The schedule or stored rate changed. Review the current amount and effective date again.");
            }
            DateTime now = DateTimeHelper.GetCurrentPhilippineTime();
            var update = new RateUpload
            {
                FileName = $"Schedule #{scheduleId}, service #{entryId} rate capture", RateKind = kind,
                UploadedByUserId = actor.Id, UploadedBy = actor.Name.Trim(), UploadedAt = now, SubmittedAt = now,
                TotalRows = 1, ChangedRows = row.Change == "Unchanged" ? 0 : 1, RowsJson = JsonSerializer.Serialize(new[] { row })
            };
            _db.MsapRateUploads.Add(update);
            await ActivateRowsAsync(update, [row], actor, ct);
            if (kind == RateTableKind.Tariff)
            {
                schedule.ServiceEntries.Single(entry => entry.VesselScheduleServiceEntryId == entryId).IsManualTariffOverride = false;
            }
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult<Guid>.Success(update.RateUploadId, "Rate approved for reuse. Saved schedule and ticket amounts are unchanged.");
        }

        public async Task<(ServiceResult<int> Result, string? Acknowledgement)> ConfirmScheduleAsync(IVesselScheduleService schedules,
            int id, DateTime reviewedAt, bool allowConflicts, string? acknowledgement, string username, string userId, CancellationToken ct)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            VesselSchedule? schedule = await _work.VesselSchedule.GetForUpdateAsync(id, ct);
            if (schedule == null)
            {
                return (ServiceResult<int>.Failure("Schedule not found."), null);
            }
            await _db.Database.ExecuteSqlRawAsync("LOCK TABLE msap.mmsi_tariff_rates, msap.mmsi_baf_rates IN SHARE MODE", ct);
            List<ScheduleRateCaptureViewModel> captures = await GetScheduleCapturesAsync(schedule, ct);
            if (captures.Any(item => !item.PublishedUpdateId.HasValue))
            {
                string current = Fingerprint(new { schedule.VesselScheduleId, schedule.Revision, schedule.EditedDate, schedule.CreatedDate,
                    captures = captures.Select(item => new { item.EntryId, item.Kind, item.PublishedUpdateId, item.CanCapture }),
                    amounts = schedule.ServiceEntries.Select(entry => new { entry.VesselScheduleServiceEntryId, entry.ManualTariffRate, entry.ManualBafRate, entry.CancelledAt }) });
                if (acknowledgement != current)
                {
                    return (ServiceResult<int>.Failure("Some manual rates have not been saved for reuse. Approving keeps these amounts for this schedule and does not add them to the rate tables."), current);
                }
            }
            ServiceResult<int> result = await schedules.ConfirmAsync(id, reviewedAt, username, ct, allowConflicts, userId);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(ct);
            }
            return (result, null);
        }

        private static string Fingerprint(object value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
        }
    }
}
