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
                    decimal? tableRate = null;
                    bool eligible;
                    if (kind == RateTableKind.Tariff)
                    {
                        TariffRate? rate = await _work.TariffTable.GetApplicableRateAsync(schedule.CustomerId.GetValueOrDefault(), schedule.TerminalId, entry.ServiceId, plannedDate, ct);
                        tableRate = rate?.Dispatch;
                        eligible = rate == null || rate.Dispatch == amount;
                    }
                    else
                    {
                        BafRate? rate = await _work.BafTable.GetApplicableRateAsync(schedule.CustomerId.GetValueOrDefault(), schedule.TerminalId, entry.ServiceId, plannedDate, ct);
                        tableRate = rate?.Amount;
                        eligible = rate == null || rate.AsOfDate < ScheduledTicketCharges.BafCycleStart(plannedDate) || rate.Amount == amount;
                    }

                    if (!amount.HasValue && tableRate.HasValue && tableRate > 0)
                    {
                        continue;
                    }

                    RateUpload? captured = amount.HasValue
                        ? published.FirstOrDefault(update => update.RateKind == kind
                            && JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(update.RowsJson)!.Any(row => row.Selected
                                && row.SourceScheduleId == schedule.VesselScheduleId && row.SourceRevision == schedule.Revision
                                && row.SourceServiceEntryId == entry.VesselScheduleServiceEntryId && row.Amount == amount))
                        : null;

                    bool isPending = !amount.HasValue && (!tableRate.HasValue || (kind == RateTableKind.Tariff && tableRate <= 0));

                    DateOnly today = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime());
                    DateOnly defaultEffectiveDate = kind == RateTableKind.BAF
                        ? ScheduledTicketCharges.BafCycleStart(plannedDate)
                        : (plannedDate > today ? today : plannedDate);

                    result.Add(new ScheduleRateCaptureViewModel
                    {
                        ScheduleId = schedule.VesselScheduleId, EntryId = entry.VesselScheduleServiceEntryId, Kind = kind,
                        EffectiveDate = defaultEffectiveDate,
                        PublishedUpdateId = captured?.RateUploadId,
                        CanCapture = captured == null && (eligible || isPending) && schedule.Status is not (MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled),
                        CustomerId = schedule.CustomerId,
                        PortId = schedule.PortId,
                        TerminalId = schedule.TerminalId,
                        ServiceId = entry.ServiceId,
                        Amount = amount,
                        IsPendingRate = isPending,
                        ApplicableTableRate = tableRate
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
            return ServiceResult<Guid>.Success(update.RateUploadId, "Rate approved for reuse and eligible unbilled tickets were automatically recalculated.");
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

        public async Task<ScheduleRateSetupViewModel> GetScheduleRateSetupAsync(
            VesselSchedule schedule,
            List<ScheduleRateCaptureViewModel> captures,
            CancellationToken ct)
        {
            var plannedDate = DateOnly.FromDateTime(schedule.PlannedStart);
            var today = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime());
            var defaultTariffDate = plannedDate > today ? today : plannedDate;
            var defaultBafDate = ScheduledTicketCharges.BafCycleStart(plannedDate);
            var model = new ScheduleRateSetupViewModel
            {
                ScheduleId = schedule.VesselScheduleId,
                CustomerId = schedule.CustomerId,
                PortId = schedule.PortId,
                TerminalId = schedule.TerminalId,
                PlannedDate = plannedDate
            };

            if (!schedule.CustomerId.HasValue)
            {
                return model;
            }

            var awaitingTariff = new Dictionary<int, (Guid Id, decimal? Amount)>();
            var awaitingBaf = new Dictionary<int, (Guid Id, decimal? Amount)>();
            if (_db != null)
            {
                var awaitingUploads = await _db.MsapRateUploads.AsNoTracking()
                    .Where(u => u.Company == MsapConstants.Company_MMSI && u.SubmittedAt != null && u.ApprovedAt == null)
                    .OrderByDescending(u => u.SubmittedAt)
                    .Take(50)
                    .ToListAsync(ct);

                foreach (var upload in awaitingUploads)
                {
                    var uploadRows = JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(upload.RowsJson) ?? [];
                    foreach (var row in uploadRows.Where(r => r.Selected && r.CustomerId == schedule.CustomerId.Value && r.TerminalId == schedule.TerminalId))
                    {
                        var target = upload.RateKind == RateTableKind.Tariff ? awaitingTariff : awaitingBaf;
                        target.TryAdd(row.ServiceId, (upload.RateUploadId, row.Amount));
                    }
                }
            }

            var captureMap = captures
                .GroupBy(c => (c.EntryId, c.Kind))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var group in schedule.ServiceEntries.Where(e => !e.CancelledAt.HasValue).GroupBy(e => e.ServiceId))
            {
                var first = group.First();
                var entryIds = group.Select(e => e.VesselScheduleServiceEntryId).ToList();
                var tariffCaptures = entryIds
                    .Select(id => captureMap.TryGetValue((id, RateTableKind.Tariff), out var c) ? c : null)
                    .Where(c => c != null)
                    .Cast<ScheduleRateCaptureViewModel>()
                    .ToList();
                var bafCaptures = entryIds
                    .Select(id => captureMap.TryGetValue((id, RateTableKind.BAF), out var c) ? c : null)
                    .Where(c => c != null)
                    .Cast<ScheduleRateCaptureViewModel>()
                    .ToList();

                decimal? tariffTableRate = tariffCaptures.FirstOrDefault(c => c.ApplicableTableRate.HasValue)?.ApplicableTableRate;
                if (tariffCaptures.Count == 0)
                {
                    tariffTableRate = (await _work.TariffTable.GetApplicableRateAsync(schedule.CustomerId.Value, schedule.TerminalId, group.Key, plannedDate, ct))?.Dispatch;
                }

                decimal? bafTableRate = bafCaptures.FirstOrDefault(c => c.ApplicableTableRate.HasValue)?.ApplicableTableRate;
                if (bafCaptures.Count == 0)
                {
                    var bafRate = await _work.BafTable.GetApplicableRateAsync(schedule.CustomerId.Value, schedule.TerminalId, group.Key, plannedDate, ct);
                    if (bafRate != null && bafRate.AsOfDate >= defaultBafDate)
                    {
                        bafTableRate = bafRate.Amount;
                    }
                }

                var primaryTariff = tariffCaptures.FirstOrDefault(c => c.IsPendingRate || c.CanCapture) ?? tariffCaptures.FirstOrDefault();
                var primaryBaf = bafCaptures.FirstOrDefault(c => c.IsPendingRate || c.CanCapture) ?? bafCaptures.FirstOrDefault();

                var tariffBasisLabel = first.TariffBasis switch
                {
                    TariffChargeBasis.PerMove => "Per move",
                    TariffChargeBasis.PerHour => "Per hour",
                    _ => "Not specified"
                };
                var bafMethodLabel = first.BafMethod switch
                {
                    BafCalculationMethod.PerMove => "Per move",
                    BafCalculationMethod.PerHour => "Per hour",
                    BafCalculationMethod.Lumpsum => "Lumpsum",
                    BafCalculationMethod.Industry => "Industry",
                    _ => "Not specified"
                };

                awaitingTariff.TryGetValue(group.Key, out var pendingTariff);
                awaitingBaf.TryGetValue(group.Key, out var pendingBaf);

                model.Items.Add(new ScheduleRateSetupItemViewModel
                {
                    ServiceId = group.Key,
                    ServiceName = first.Service?.ServiceName ?? $"Service #{group.Key}",
                    SourceEntryId = first.VesselScheduleServiceEntryId,
                    TugboatNames = string.Join(", ", group.Select(e => e.Tugboat?.TugboatName ?? $"Tug #{e.TugboatId}").Distinct()),
                    ChargingContext = $"Basic: {tariffBasisLabel} · BAF: {bafMethodLabel}",

                    TariffTableRate = tariffTableRate,
                    TariffManualAmount = first.ManualTariffRate,
                    TariffIsPending = primaryTariff?.IsPendingRate == true,
                    TariffCanSetup = primaryTariff != null && (primaryTariff.IsPendingRate || (primaryTariff.CanCapture && !primaryTariff.PublishedUpdateId.HasValue)),
                    TariffEffectiveDate = primaryTariff?.EffectiveDate ?? defaultTariffDate,
                    TariffPublishedUpdateId = primaryTariff?.PublishedUpdateId,
                    PendingTariffUploadId = pendingTariff.Id == Guid.Empty ? null : pendingTariff.Id,
                    PendingTariffAmount = pendingTariff.Amount,

                    BafTableRate = bafTableRate,
                    BafManualAmount = first.ManualBafRate,
                    BafIsPending = primaryBaf?.IsPendingRate == true,
                    BafCanSetup = primaryBaf != null && (primaryBaf.IsPendingRate || (primaryBaf.CanCapture && !primaryBaf.PublishedUpdateId.HasValue)),
                    BafEffectiveDate = primaryBaf?.EffectiveDate ?? defaultBafDate,
                    BafPublishedUpdateId = primaryBaf?.PublishedUpdateId,
                    PendingBafUploadId = pendingBaf.Id == Guid.Empty ? null : pendingBaf.Id,
                    PendingBafAmount = pendingBaf.Amount
                });
            }

            return model;
        }

        public async Task<ServiceResult<BatchRatePreviewViewModel>> PreviewBatchAsync(BatchRateUpdateRequest request, CancellationToken ct)
        {
            var preparation = await ValidateAndPrepareBatchAsync(request, applyBaseline: true, ct);
            if (!preparation.IsSuccess)
            {
                return ServiceResult<BatchRatePreviewViewModel>.Failure(preparation.Message!);
            }

            return ServiceResult<BatchRatePreviewViewModel>.Success(BuildBatchPreview(request.TariffRows, request.BafRows));
        }

        public async Task<ServiceResult<BatchRateSaveResult>> SaveBatchAsync(
            BatchRateUpdateRequest request,
            string userId,
            bool canApprove,
            CancellationToken ct)
        {
            var action = (request.Action ?? "submit").Trim().ToLowerInvariant();
            if (action is not ("draft" or "submit" or "approve"))
            {
                return ServiceResult<BatchRateSaveResult>.Failure("Choose a valid rate update action.");
            }
            if (action == "approve" && !canApprove)
            {
                return ServiceResult<BatchRateSaveResult>.Failure("Rate approval permission is required to activate rates.");
            }

            var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                return ServiceResult<BatchRateSaveResult>.Failure("The account needs a full name of up to 255 characters before recording rate changes.");
            }

            var validation = await ValidateAndPrepareBatchAsync(request, applyBaseline: false, ct);
            if (!validation.IsSuccess)
            {
                return ServiceResult<BatchRateSaveResult>.Failure(validation.Message!);
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            VesselSchedule? schedule = null;
            if (request.ScheduleId.HasValue)
            {
                schedule = await _work.VesselSchedule.GetScheduleWithDetailsAsync(request.ScheduleId.Value, ct);
                if (schedule == null)
                {
                    return ServiceResult<BatchRateSaveResult>.Failure("Schedule not found.");
                }
                if (schedule.Status is MsapConstants.VesselScheduleStatus.Completed or MsapConstants.VesselScheduleStatus.Cancelled)
                {
                    return ServiceResult<BatchRateSaveResult>.Failure("Completed or cancelled schedules cannot receive rate updates.");
                }
            }

            if (action == "approve")
            {
                await _db.Database.ExecuteSqlRawAsync("LOCK TABLE msap.mmsi_tariff_rates, msap.mmsi_baf_rates IN SHARE ROW EXCLUSIVE MODE", ct);
            }

            if (request.TariffRows.Count > 0)
            {
                await UpdateBaselineAsync(request.TariffRows, RateTableKind.Tariff, true, ct);
            }
            if (request.BafRows.Count > 0)
            {
                await UpdateBaselineAsync(request.BafRows, RateTableKind.BAF, true, ct);
            }

            if (action == "approve" && !string.IsNullOrWhiteSpace(request.Fingerprint))
            {
                var freshPreview = BuildBatchPreview(request.TariffRows, request.BafRows);
                if (!string.Equals(freshPreview.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
                {
                    return ServiceResult<BatchRateSaveResult>.Failure("Stored rates changed while reviewing. Review the updated comparison and try again.");
                }
            }

            if (schedule != null)
            {
                var entryByService = schedule.ServiceEntries
                    .Where(e => !e.CancelledAt.HasValue)
                    .GroupBy(e => e.ServiceId)
                    .ToDictionary(g => g.Key, g => g.First().VesselScheduleServiceEntryId);
                foreach (var row in request.TariffRows.Concat(request.BafRows))
                {
                    row.SourceScheduleId ??= schedule.VesselScheduleId;
                    row.SourceRevision ??= schedule.Revision;
                    if (!row.SourceServiceEntryId.HasValue && entryByService.TryGetValue(row.ServiceId, out var entryId))
                    {
                        row.SourceServiceEntryId = entryId;
                    }
                }
            }

            var result = new BatchRateSaveResult { Approved = action == "approve" };
            var now = DateTimeHelper.GetCurrentPhilippineTime();

            if (request.TariffRows.Count > 0)
            {
                var tariffResult = await SaveSingleKindBatchAsync(RateTableKind.Tariff, request.TariffId, request.TariffRows, action, schedule?.VesselScheduleId, actor, now, ct);
                if (!tariffResult.IsSuccess)
                {
                    return ServiceResult<BatchRateSaveResult>.Failure(tariffResult.Message!);
                }
                result.TariffUploadId = tariffResult.Data;
            }

            if (request.BafRows.Count > 0)
            {
                var bafResult = await SaveSingleKindBatchAsync(RateTableKind.BAF, request.BafId, request.BafRows, action, schedule?.VesselScheduleId, actor, now, ct);
                if (!bafResult.IsSuccess)
                {
                    return ServiceResult<BatchRateSaveResult>.Failure(bafResult.Message!);
                }
                result.BafUploadId = bafResult.Data;
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var message = action switch
            {
                "approve" => "Rates approved and activated. Eligible unbilled tickets without a prepared billing were automatically recalculated.",
                "submit" => "Rates submitted for review. Live rates remain unchanged until approved.",
                _ => "Rate draft saved. Review and submit when ready."
            };
            return ServiceResult<BatchRateSaveResult>.Success(result, message);
        }

        private async Task<ServiceResult> ValidateAndPrepareBatchAsync(BatchRateUpdateRequest request, bool applyBaseline, CancellationToken ct)
        {
            if (request.TariffRows.Count == 0 && request.BafRows.Count == 0)
            {
                return ServiceResult.Failure("Enter at least one Basic or BAF rate before continuing.");
            }

            if (request.TariffRows.Count > 0)
            {
                var tariffErrors = ValidateRows(request.TariffRows);
                if (tariffErrors.Count > 0)
                {
                    return ServiceResult.Failure(string.Join("\n", tariffErrors.Take(20)));
                }
                if (request.ScheduleId.HasValue && request.TariffRows.Any(r => r.Amount is not > 0))
                {
                    return ServiceResult.Failure("Basic tariff rates must be greater than zero.");
                }
                var refs = await PopulateReferencesAsync(request.TariffRows, ct);
                if (!refs.IsSuccess)
                {
                    return refs;
                }
                if (applyBaseline)
                {
                    await UpdateBaselineAsync(request.TariffRows, RateTableKind.Tariff, true, ct);
                }
            }

            if (request.BafRows.Count > 0)
            {
                var bafErrors = ValidateRows(request.BafRows);
                if (bafErrors.Count > 0)
                {
                    return ServiceResult.Failure(string.Join("\n", bafErrors.Take(20)));
                }
                if (request.RequireTuesdayForBaf && request.BafRows.Any(r => r.EffectiveDate.DayOfWeek != DayOfWeek.Tuesday))
                {
                    return ServiceResult.Failure("BAF effective dates must fall on a Tuesday.");
                }
                var refs = await PopulateReferencesAsync(request.BafRows, ct);
                if (!refs.IsSuccess)
                {
                    return refs;
                }
                if (applyBaseline)
                {
                    await UpdateBaselineAsync(request.BafRows, RateTableKind.BAF, true, ct);
                }
            }

            return ServiceResult.Success("Valid");
        }

        private static BatchRatePreviewViewModel BuildBatchPreview(List<RateUploadRowViewModel> tariffRows, List<RateUploadRowViewModel> bafRows)
        {
            var items = new List<BatchRatePreviewItemViewModel>();
            foreach (var (kind, rows) in new[] { (RateTableKind.Tariff, tariffRows), (RateTableKind.BAF, bafRows) })
            {
                foreach (var row in rows)
                {
                    items.Add(new BatchRatePreviewItemViewModel
                    {
                        Kind = kind,
                        KindLabel = kind == RateTableKind.Tariff ? "Basic" : "BAF",
                        CustomerName = row.CustomerName,
                        PortName = row.PortName,
                        TerminalName = row.TerminalName,
                        ServiceName = row.ServiceName,
                        EffectiveDate = row.EffectiveDate,
                        BaselineDate = row.BaselineDate,
                        StoredAmount = row.StoredAmount,
                        Amount = row.Amount!.Value,
                        Change = row.Change
                    });
                }
            }

            var fingerprint = Fingerprint(items.Select(i => new
            {
                i.Kind,
                i.CustomerName,
                i.TerminalName,
                i.ServiceName,
                i.EffectiveDate,
                i.StoredAmount,
                i.Amount,
                i.Change
            }));

            return new BatchRatePreviewViewModel
            {
                Fingerprint = fingerprint,
                HasChanges = items.Any(i => i.Change != "Unchanged"),
                Items = items
            };
        }

        private async Task<ServiceResult<Guid>> SaveSingleKindBatchAsync(
            RateTableKind kind,
            Guid? existingId,
            List<RateUploadRowViewModel> rows,
            string action,
            int? scheduleId,
            ApplicationUser actor,
            DateTime now,
            CancellationToken ct)
        {
            RateUpload? update;
            if (existingId.HasValue)
            {
                update = await LockAsync(existingId.Value, ct);
                if (update == null || update.UploadedByUserId != actor.Id || update.SubmittedAt.HasValue || update.RateKind != kind)
                {
                    return ServiceResult<Guid>.Failure($"Only your unsubmitted {kind} draft can be updated.");
                }
            }
            else
            {
                update = new RateUpload
                {
                    UploadedByUserId = actor.Id,
                    UploadedBy = actor.Name.Trim(),
                    UploadedAt = now,
                    RateKind = kind,
                    FileName = scheduleId.HasValue ? $"Booking #{scheduleId} {kind} rates" : $"{kind} rate update"
                };
            }

            foreach (var row in rows)
            {
                row.Selected = action == "draft" ? row.Selected : row.Change != "Unchanged" || !row.ExistingRateId.HasValue;
            }
            if (action != "draft" && !rows.Any(r => r.Selected))
            {
                foreach (var row in rows)
                {
                    row.Selected = true;
                }
            }

            var before = update.RowsJson;
            update.UploadedBy = actor.Name.Trim();
            update.RowsJson = JsonSerializer.Serialize(rows);
            update.TotalRows = rows.Count;
            update.ChangedRows = rows.Count(r => r.Change != "Unchanged");
            if (action is "submit" or "approve")
            {
                update.SubmittedAt ??= now;
            }
            if (!existingId.HasValue)
            {
                _db.MsapRateUploads.Add(update);
            }

            _db.AuditTrails.Add(CreateAudit(actor, update, action == "draft" ? "Saved draft" : "Submitted for review", before));

            if (action == "approve")
            {
                var selectedRows = rows.Where(r => r.Selected).ToList();
                await PopulateNamesAsync([update], ct);
                await ActivateRowsAsync(update, selectedRows, actor, ct);
            }

            return ServiceResult<Guid>.Success(update.RateUploadId);
        }

        private static string Fingerprint(object value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
        }
    }
}
