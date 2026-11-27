using System.Text.Json;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using ApplicationUser = IBS.Models.ApplicationUser;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IBS.Services.MSAP
{
    public partial class RateUploadService
    {
        private readonly MsapDbContext _db;
        private readonly IUnitOfWork _work;
        private readonly ILogger<RateUploadService> _logger;

        public RateUploadService(MsapDbContext db, IUnitOfWork work, ILogger<RateUploadService>? logger = null)
        {
            _db = db;
            _work = work;
            _logger = logger ?? NullLogger<RateUploadService>.Instance;
        }

        public async Task<ServiceResult<byte[]>> ExportAsync(DateOnly? dateFrom, DateOnly dateTo, CancellationToken ct, RateTableKind kind = RateTableKind.Tariff)
        {
            if (!Enum.IsDefined(kind) || dateTo.Year is < 1900 or > 9998 || (dateFrom.HasValue
                && (dateFrom.Value.Year is < 1900 or > 9998 || dateFrom > dateTo)))
            {
                return ServiceResult<byte[]>.Failure("Choose a valid table and dates between 1900 and 9998, with From on or before To.");
            }
            try
            {
                var rows = await GetRowsAsync(kind, dateFrom, dateTo, ct);
                var masterLists = dateFrom.HasValue ? null : await GetMasterListsAsync(ct);
                return ServiceResult<byte[]>.Success(await RateUploadWorkbook.WriteAsync(rows, kind, dateFrom.HasValue, ct, masterLists));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to export {Kind} rates", kind);
                return ServiceResult<byte[]>.Failure("The rate export could not be generated. Try again or contact support.");
            }
        }

        public async Task<List<RateUpload>> GetRecentAsync(bool awaitingReview, CancellationToken ct)
        {
            var (items, _) = await GetPagedAsync(awaitingReview, 1, 10, ct);
            return items;
        }

        public async Task<(List<RateUpload> Items, int TotalCount)> GetPagedAsync(bool awaitingReview, int page, int pageSize, CancellationToken ct)
        {
            var normalizedPage = Math.Max(1, page);
            var normalizedSize = Math.Clamp(pageSize, 1, 50);
            var query = _db.MsapRateUploads.AsNoTracking()
                .Where(update => update.Company == MsapConstants.Company_MMSI && (!awaitingReview || update.SubmittedAt != null && update.ApprovedAt == null));
            var totalCount = await query.CountAsync(ct);
            List<RateUpload> updates = await query
                .OrderByDescending(update => update.UploadedAt).ThenBy(update => update.RateUploadId)
                .Skip((normalizedPage - 1) * normalizedSize)
                .Take(normalizedSize)
                .Select(update => new RateUpload
                {
                    RateUploadId = update.RateUploadId, Company = update.Company, FileName = update.FileName,
                    UploadedByUserId = update.UploadedByUserId, UploadedBy = update.UploadedBy,
                    UploadedAt = update.UploadedAt, SubmittedAt = update.SubmittedAt, RateKind = update.RateKind,
                    ApprovedAt = update.ApprovedAt, ApprovedBy = update.ApprovedBy,
                    TotalRows = update.TotalRows, ChangedRows = update.ChangedRows
                }).ToListAsync(ct);
            await PopulateNamesAsync(updates, ct);
            return (updates, totalCount);
        }

        public async Task<RateUploadViewModel?> GetAsync(Guid id, string userId, CancellationToken ct)
        {
            RateUpload? update = await _db.MsapRateUploads.AsNoTracking()
                .SingleOrDefaultAsync(item => item.RateUploadId == id && item.Company == MsapConstants.Company_MMSI, ct);
            if (update == null)
            {
                return null;
            }
            await PopulateNamesAsync([update], ct);
            List<RateUploadRowViewModel> rows = JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(update.RowsJson)!;
            var model = new RateUploadViewModel
            {
                Upload = update, Rows = rows, IsOwner = update.UploadedByUserId == userId,
                BaselineChanged = !update.ApprovedAt.HasValue && await UpdateBaselineAsync(rows, update.RateKind, false, ct)
            };
            if (update.ApprovedAt.HasValue)
            {
                await PopulateAffectedTicketsAsync(model, ct);
            }
            return model;
        }

        public async Task<RateEditorViewModel?> GetEditorAsync(
            RateTableKind kind,
            DateOnly asOf,
            Guid? id,
            string userId,
            CancellationToken ct,
            int? scheduleId = null,
            string? mode = null,
            int? customerId = null,
            int? portId = null,
            int? terminalId = null,
            int? serviceId = null)
        {
            if (!Enum.IsDefined(kind) || asOf.Year is < 1900 or > 9998)
            {
                return null;
            }

            VesselSchedule? schedule = null;
            if (!id.HasValue && scheduleId.HasValue)
            {
                schedule = await _work.VesselSchedule.GetScheduleWithDetailsAsync(scheduleId.Value, ct);
                if (schedule == null)
                {
                    return null;
                }
            }

            var effectiveAsOf = schedule != null && asOf == DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime())
                ? DateOnly.FromDateTime(schedule.PlannedStart)
                : asOf;
            var effectiveMode = !id.HasValue && (string.Equals(mode, "combined", StringComparison.OrdinalIgnoreCase) || (schedule != null && string.IsNullOrEmpty(mode)))
                ? "combined"
                : "single";

            var model = new RateEditorViewModel
            {
                Kind = kind,
                AsOfDate = effectiveAsOf,
                Id = id,
                Mode = effectiveMode,
                ScheduleId = schedule?.VesselScheduleId,
                ScheduleLabel = schedule == null ? null : $"Booking #{schedule.VesselScheduleId} · {schedule.Customer?.CustomerName ?? "Customer"} · {schedule.Vessel?.VesselName ?? "Vessel"}",
                SelectedCustomerId = customerId ?? schedule?.CustomerId,
                SelectedPortId = portId ?? schedule?.PortId,
                SelectedTerminalId = terminalId ?? schedule?.TerminalId,
                SelectedServiceId = serviceId
            };

            if (id.HasValue)
            {
                RateUploadViewModel? saved = await GetAsync(id.Value, userId, ct);
                if (saved == null || !saved.IsOwner || saved.Upload!.SubmittedAt.HasValue || saved.Upload.RateKind != kind)
                {
                    return null;
                }
                model.Rows = saved.Rows;
            }
            else if (effectiveMode == "combined")
            {
                model.CombinedRows = await GetCombinedRowsAsync(effectiveAsOf, schedule, model.SelectedCustomerId, model.SelectedPortId, model.SelectedTerminalId, model.SelectedServiceId, ct);
            }
            else
            {
                model.Rows = await GetRowsAsync(kind, null, effectiveAsOf, ct);
            }

            model.Customers = await _db.Customers.AsNoTracking().Where(customer => customer.Company == MsapConstants.Company_MMSI)
                .OrderBy(customer => customer.CustomerName).Select(customer => new RateReferenceViewModel { Id = customer.CustomerId, Name = customer.CustomerName, Code = customer.CustomerCode }).ToListAsync(ct);
            model.Ports = await _db.MsapPorts.AsNoTracking().OrderBy(port => port.PortName)
                .Select(port => new RateReferenceViewModel { Id = port.PortId, Name = port.PortName, Code = port.PortNumber }).ToListAsync(ct);
            model.Terminals = await _db.MsapTerminals.AsNoTracking().OrderBy(terminal => terminal.TerminalName)
                .Select(terminal => new RateReferenceViewModel { Id = terminal.TerminalId, Name = terminal.TerminalName, Code = terminal.TerminalNumber, PortId = terminal.PortId }).ToListAsync(ct);
            model.Services = await _db.MsapServices.AsNoTracking().OrderBy(service => service.ServiceName)
                .Select(service => new RateReferenceViewModel { Id = service.ServiceId, Name = service.ServiceName, Code = service.ServiceNumber }).ToListAsync(ct);
            return model;
        }

        private async Task<List<RateEditorCombinedRowViewModel>> GetCombinedRowsAsync(
            DateOnly asOf,
            VesselSchedule? schedule,
            int? customerId,
            int? portId,
            int? terminalId,
            int? serviceId,
            CancellationToken ct)
        {
            var tariffRows = await GetRowsAsync(RateTableKind.Tariff, null, asOf, ct);
            var bafRows = await GetRowsAsync(RateTableKind.BAF, null, asOf, ct);

            if (schedule != null && schedule.CustomerId.HasValue)
            {
                tariffRows = tariffRows.Where(r => r.CustomerId == schedule.CustomerId.Value && r.TerminalId == schedule.TerminalId).ToList();
                bafRows = bafRows.Where(r => r.CustomerId == schedule.CustomerId.Value && r.TerminalId == schedule.TerminalId).ToList();
            }

            var bafCycleStart = ScheduledTicketCharges.BafCycleStart(asOf);
            var combinedMap = new Dictionary<(int CustomerId, int TerminalId, int ServiceId), RateEditorCombinedRowViewModel>();

            foreach (var t in tariffRows)
            {
                var key = (t.CustomerId, t.TerminalId, t.ServiceId);
                combinedMap[key] = new RateEditorCombinedRowViewModel
                {
                    CustomerId = t.CustomerId, CustomerCode = t.CustomerCode, CustomerName = t.CustomerName,
                    PortId = t.PortId, PortCode = t.PortCode, PortName = t.PortName,
                    TerminalId = t.TerminalId, TerminalCode = t.TerminalCode, TerminalName = t.TerminalName,
                    ServiceId = t.ServiceId, ServiceCode = t.ServiceCode, ServiceName = t.ServiceName,
                    TariffEffectiveDate = t.EffectiveDate,
                    TariffAmount = t.Amount,
                    TariffExistingRateId = t.ExistingRateId,
                    TariffBaselineRateId = t.BaselineRateId,
                    TariffBaselineDate = t.BaselineDate,
                    TariffStoredAmount = t.StoredAmount,
                    BafEffectiveDate = bafCycleStart,
                    IsPendingTariff = t.Amount is not > 0,
                    IsPendingBaf = true
                };
            }

            foreach (var b in bafRows)
            {
                var key = (b.CustomerId, b.TerminalId, b.ServiceId);
                if (!combinedMap.TryGetValue(key, out var item))
                {
                    item = new RateEditorCombinedRowViewModel
                    {
                        CustomerId = b.CustomerId, CustomerCode = b.CustomerCode, CustomerName = b.CustomerName,
                        PortId = b.PortId, PortCode = b.PortCode, PortName = b.PortName,
                        TerminalId = b.TerminalId, TerminalCode = b.TerminalCode, TerminalName = b.TerminalName,
                        ServiceId = b.ServiceId, ServiceCode = b.ServiceCode, ServiceName = b.ServiceName,
                        TariffEffectiveDate = asOf,
                        IsPendingTariff = true
                    };
                    combinedMap[key] = item;
                }

                var isCurrentCycle = b.EffectiveDate >= bafCycleStart;
                item.BafBaselineDate = b.BaselineDate;
                item.BafStoredAmount = b.StoredAmount;
                item.BafBaselineRateId = b.BaselineRateId;
                if (isCurrentCycle)
                {
                    item.BafEffectiveDate = b.EffectiveDate;
                    item.BafAmount = b.Amount;
                    item.BafExistingRateId = b.ExistingRateId;
                    item.IsPendingBaf = false;
                }
                else
                {
                    item.BafEffectiveDate = bafCycleStart;
                    item.BafAmount = null;
                    item.BafExistingRateId = null;
                    item.IsPendingBaf = true;
                }
            }

            if (schedule != null && schedule.CustomerId.HasValue)
            {
                var today = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime());
                var defaultTariffDate = asOf > today ? today : asOf;
                var activeEntries = schedule.ServiceEntries.Where(e => !e.CancelledAt.HasValue).ToList();
                var scheduleServiceIds = activeEntries.Select(e => e.ServiceId).Distinct().ToHashSet();

                foreach (var group in activeEntries.GroupBy(e => e.ServiceId))
                {
                    var first = group.First();
                    var key = (schedule.CustomerId.Value, schedule.TerminalId, group.Key);
                    if (!combinedMap.TryGetValue(key, out var item))
                    {
                        item = new RateEditorCombinedRowViewModel
                        {
                            CustomerId = schedule.CustomerId.Value,
                            CustomerCode = schedule.Customer?.CustomerCode ?? "",
                            CustomerName = schedule.Customer?.CustomerName ?? $"Customer #{schedule.CustomerId}",
                            PortId = schedule.PortId,
                            PortCode = schedule.Port?.PortNumber ?? "",
                            PortName = schedule.Port?.PortName ?? $"Port #{schedule.PortId}",
                            TerminalId = schedule.TerminalId,
                            TerminalCode = schedule.Terminal?.TerminalNumber ?? "",
                            TerminalName = schedule.Terminal?.TerminalName ?? $"Terminal #{schedule.TerminalId}",
                            ServiceId = group.Key,
                            ServiceCode = first.Service?.ServiceNumber ?? "",
                            ServiceName = first.Service?.ServiceName ?? $"Service #{group.Key}",
                            TariffEffectiveDate = defaultTariffDate,
                            BafEffectiveDate = bafCycleStart,
                            IsPendingTariff = true,
                            IsPendingBaf = true
                        };
                        combinedMap[key] = item;
                    }

                    item.SourceScheduleId = schedule.VesselScheduleId;
                    item.SourceServiceEntryId = first.VesselScheduleServiceEntryId;
                    item.SourceRevision = schedule.Revision;
                    var tugs = string.Join(", ", group.Select(e => e.Tugboat?.TugboatName ?? $"Tug #{e.TugboatId}").Distinct());
                    item.ContextNote = $"Booking #{schedule.VesselScheduleId} · {tugs}";

                    if (item.IsPendingTariff && first.ManualTariffRate.HasValue)
                    {
                        item.TariffEffectiveDate = defaultTariffDate;
                        item.TariffAmount = first.ManualTariffRate;
                    }
                    if (item.IsPendingBaf && first.ManualBafRate.HasValue)
                    {
                        item.BafEffectiveDate = bafCycleStart;
                        item.BafAmount = first.ManualBafRate;
                    }
                }

                return combinedMap.Values
                    .OrderByDescending(r => scheduleServiceIds.Contains(r.ServiceId))
                    .ThenByDescending(r => r.IsPendingTariff || r.IsPendingBaf)
                    .ThenBy(r => r.ServiceName)
                    .ToList();
            }

            if (customerId.HasValue && portId.HasValue && terminalId.HasValue && serviceId.HasValue)
            {
                var key = (customerId.Value, terminalId.Value, serviceId.Value);
                if (!combinedMap.ContainsKey(key))
                {
                    var customer = await _db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.CustomerId == customerId.Value && c.Company == MsapConstants.Company_MMSI, ct);
                    var port = await _db.MsapPorts.AsNoTracking().SingleOrDefaultAsync(p => p.PortId == portId.Value, ct);
                    var terminal = await _db.MsapTerminals.AsNoTracking().SingleOrDefaultAsync(t => t.TerminalId == terminalId.Value && t.PortId == portId.Value, ct);
                    var service = await _db.MsapServices.AsNoTracking().SingleOrDefaultAsync(s => s.ServiceId == serviceId.Value, ct);
                    if (customer != null && port != null && terminal != null && service != null)
                    {
                        combinedMap[key] = new RateEditorCombinedRowViewModel
                        {
                            CustomerId = customer.CustomerId, CustomerCode = customer.CustomerCode, CustomerName = customer.CustomerName,
                            PortId = port.PortId, PortCode = port.PortNumber, PortName = port.PortName,
                            TerminalId = terminal.TerminalId, TerminalCode = terminal.TerminalNumber, TerminalName = terminal.TerminalName,
                            ServiceId = service.ServiceId, ServiceCode = service.ServiceNumber, ServiceName = service.ServiceName,
                            TariffEffectiveDate = asOf,
                            BafEffectiveDate = bafCycleStart,
                            IsPendingTariff = true,
                            IsPendingBaf = true
                        };
                    }
                }
            }

            return combinedMap.Values
                .OrderBy(r => r.CustomerCode)
                .ThenBy(r => r.TerminalCode)
                .ThenBy(r => r.ServiceName)
                .ToList();
        }

        public async Task<List<RateUploadRowViewModel>> GetRowsAsync(RateTableKind kind, DateOnly? from, DateOnly through, CancellationToken ct)
        {
            List<RateUploadRowViewModel> rows;
            if (kind == RateTableKind.Tariff)
            {
                List<TariffRate> rates = await _work.TariffTable.GetRatesForExportAsync(from, through, ct);
                rows = rates.Select(rate => new RateUploadRowViewModel
                {
                    CustomerId = rate.CustomerId, CustomerCode = rate.Customer.CustomerCode, CustomerName = rate.Customer.CustomerName,
                    PortId = rate.PortId, PortCode = rate.Port.PortNumber, PortName = rate.Port.PortName,
                    TerminalId = rate.TerminalId, TerminalCode = rate.Terminal.TerminalNumber, TerminalName = rate.Terminal.TerminalName,
                    ServiceId = rate.ServiceId, ServiceCode = rate.Service.ServiceNumber, ServiceName = rate.Service.ServiceName,
                    EffectiveDate = rate.AsOfDate, Amount = rate.Dispatch, ExistingRateId = rate.TariffRateId, BaselineRateId = rate.TariffRateId,
                    BaselineDate = rate.AsOfDate, StoredAmount = rate.Dispatch, BaselineUpdatedAt = rate.UpdateDate ?? rate.CreatedDate,
                    CreatedBy = rate.CreatedBy, CreatedAt = rate.CreatedDate, UpdatedBy = rate.UpdateBy,
                    ApprovalStatus = rate.ApprovedAt.HasValue ? "Approved" : "Not recorded"
                }).ToList();
            }
            else
            {
                List<BafRate> rates = await _work.BafTable.GetRatesForExportAsync(from, through, ct);
                rows = rates.Select(rate => new RateUploadRowViewModel
                {
                    CustomerId = rate.CustomerId, CustomerCode = rate.Customer.CustomerCode, CustomerName = rate.Customer.CustomerName,
                    PortId = rate.PortId, PortCode = rate.Port.PortNumber, PortName = rate.Port.PortName,
                    TerminalId = rate.TerminalId, TerminalCode = rate.Terminal.TerminalNumber, TerminalName = rate.Terminal.TerminalName,
                    ServiceId = rate.ServiceId, ServiceCode = rate.Service.ServiceNumber, ServiceName = rate.Service.ServiceName,
                    EffectiveDate = rate.AsOfDate, Amount = rate.Amount, ExistingRateId = rate.BafRateId, BaselineRateId = rate.BafRateId,
                    BaselineDate = rate.AsOfDate, StoredAmount = rate.Amount, BaselineUpdatedAt = rate.UpdateDate ?? rate.CreatedDate,
                    CreatedBy = rate.CreatedBy, CreatedAt = rate.CreatedDate, UpdatedBy = rate.UpdateBy,
                    ApprovalStatus = rate.ApprovedAt.HasValue ? "Approved" : "Not recorded"
                }).ToList();
            }
            for (int index = 0; index < rows.Count; index++)
            {
                rows[index].RowNumber = index + 1;
            }
            return rows;
        }

        public async Task<RateWorkbookMasterLists> GetMasterListsAsync(CancellationToken ct)
        {
            return new RateWorkbookMasterLists
            {
                Customers = await _db.Customers.AsNoTracking()
                    .Where(c => c.Company == MsapConstants.Company_MMSI && c.IsActive)
                    .OrderBy(c => c.CustomerName)
                    .Select(c => c.CustomerName)
                    .Distinct()
                    .ToListAsync(ct),
                Ports = await _db.MsapPorts.AsNoTracking()
                    .OrderBy(p => p.PortName)
                    .Select(p => p.PortName)
                    .Distinct()
                    .ToListAsync(ct),
                Terminals = await _db.MsapTerminals.AsNoTracking()
                    .OrderBy(t => t.TerminalName)
                    .Select(t => t.TerminalName)
                    .Distinct()
                    .ToListAsync(ct),
                Services = await _db.MsapServices.AsNoTracking()
                    .OrderBy(s => s.ServiceName)
                    .Select(s => s.ServiceName)
                    .Distinct()
                    .ToListAsync(ct)
            };
        }

        public static List<string> ValidateRows(IReadOnlyList<RateUploadRowViewModel> rows)
        {
            var errors = new List<string>();
            if (rows.Count is 0 or > 10000)
            {
                errors.Add("Save between 1 and 10,000 rate rows.");
                return errors;
            }
            var keys = new HashSet<(int, int, int, DateOnly)>();
            for (int index = 0; index < rows.Count; index++)
            {
                RateUploadRowViewModel row = rows[index];
                if (row.CustomerId <= 0 || row.PortId <= 0 || row.TerminalId <= 0 || row.ServiceId <= 0)
                {
                    errors.Add($"Row {index + 1}: select a customer, port, terminal and service.");
                }
                if (row.EffectiveDate.Year is < 1900 or > 9998)
                {
                    errors.Add($"Row {index + 1}: enter a valid effective date between 1900 and 9998.");
                }
                if (!row.Amount.HasValue || row.Amount < 0 || row.Amount > 99999999999999.99m || decimal.Round(row.Amount.Value, 2) != row.Amount)
                {
                    errors.Add($"Row {index + 1}: enter a nonnegative amount with at most two decimal places. Blank is not zero.");
                }
                if (!keys.Add((row.CustomerId, row.TerminalId, row.ServiceId, row.EffectiveDate)))
                {
                    errors.Add($"Row {index + 1}: duplicate customer, terminal, service and effective date.");
                }
            }
            return errors;
        }

        public async Task<ServiceResult<Guid>> SaveDraftAsync(RateEditorViewModel model, string userId, CancellationToken ct)
        {
            ApplicationUser? actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                return ServiceResult<Guid>.Failure("The account needs a full name of up to 255 characters before recording rate changes.");
            }

            if (!Enum.IsDefined(model.Kind))
            {
                return ServiceResult<Guid>.Failure("Choose Tariff or BAF.");
            }
            List<string> errors = ValidateRows(model.Rows);
            if (errors.Count > 0)
            {
                return ServiceResult<Guid>.Failure(string.Join("\n", errors.Take(100)));
            }
            ServiceResult references = await PopulateReferencesAsync(model.Rows, ct);
            if (!references.IsSuccess)
            {
                return ServiceResult<Guid>.Failure(references.Message!);
            }
            await using var transaction = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync(ct) : null;
            RateUpload? update = model.Id.HasValue ? await LockAsync(model.Id.Value, ct) : new RateUpload
            {
                UploadedByUserId = userId, UploadedBy = actor.Name.Trim(), UploadedAt = DateTimeHelper.GetCurrentPhilippineTime(),
                RateKind = model.Kind, FileName = $"{model.Kind} rate draft"
            };
            if (update == null || update.UploadedByUserId != userId || update.SubmittedAt.HasValue || update.RateKind != model.Kind)
            {
                return ServiceResult<Guid>.Failure("Only your unsubmitted draft can be edited.");
            }
            await UpdateBaselineAsync(model.Rows, model.Kind, true, ct);
            string before = update.RowsJson;
            update.UploadedBy = actor.Name.Trim();
            update.RowsJson = JsonSerializer.Serialize(model.Rows);
            update.TotalRows = model.Rows.Count;
            update.ChangedRows = model.Rows.Count(row => row.Change != "Unchanged");
            if (!model.Id.HasValue)
            {
                _db.MsapRateUploads.Add(update);
            }
            _db.AuditTrails.Add(CreateAudit(actor, update, "Saved draft", before));
            await _db.SaveChangesAsync(ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
            return ServiceResult<Guid>.Success(update.RateUploadId, "Draft saved. Review the comparison before submitting.");
        }

        public async Task<ServiceResult<Guid>> StageExcelUploadAsync(
            Stream fileStream, string fileName, RateTableKind kind, string userId, CancellationToken ct)
        {
            var (rows, parseErrors) = await RateUploadWorkbook.ReadAsync(fileStream, kind, ct);
            if (parseErrors.Count > 0)
            {
                return ServiceResult<Guid>.Failure(string.Join("\n", parseErrors.Take(25)));
            }
            if (rows.Count == 0)
            {
                return ServiceResult<Guid>.Failure("No rate rows were found in the uploaded workbook.");
            }

            var customers = await _db.Customers.AsNoTracking()
                .Where(c => c.Company == MsapConstants.Company_MMSI && c.IsActive)
                .ToListAsync(ct);
            var ports = await _db.MsapPorts.AsNoTracking().ToListAsync(ct);
            var terminals = await _db.MsapTerminals.AsNoTracking().ToListAsync(ct);
            var services = await _db.MsapServices.AsNoTracking().ToListAsync(ct);

            var portById = ports.ToDictionary(p => p.PortId);

            var resolutionErrors = new List<string>();
            foreach (var row in rows)
            {
                var customer = customers.FirstOrDefault(c => !string.IsNullOrEmpty(row.CustomerName)
                    ? string.Equals(c.CustomerName, row.CustomerName, StringComparison.OrdinalIgnoreCase)
                    : !string.IsNullOrEmpty(row.CustomerCode) && string.Equals(c.CustomerCode, row.CustomerCode, StringComparison.OrdinalIgnoreCase));
                var matchingPorts = ports.Where(p => !string.IsNullOrEmpty(row.PortName)
                    ? string.Equals(p.PortName, row.PortName, StringComparison.OrdinalIgnoreCase)
                    : !string.IsNullOrEmpty(row.PortCode) && string.Equals(p.PortNumber, row.PortCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var matchingPortIds = matchingPorts.Select(p => p.PortId).ToHashSet();
                var terminal = matchingPortIds.Count == 0
                    ? null
                    : terminals.FirstOrDefault(t => matchingPortIds.Contains(t.PortId) && (!string.IsNullOrEmpty(row.TerminalName)
                        ? string.Equals(t.TerminalName, row.TerminalName, StringComparison.OrdinalIgnoreCase)
                        : !string.IsNullOrEmpty(row.TerminalCode) && string.Equals(t.TerminalNumber, row.TerminalCode, StringComparison.OrdinalIgnoreCase)));
                var port = terminal != null && portById.TryGetValue(terminal.PortId, out var terminalPort)
                    ? terminalPort
                    : matchingPorts.FirstOrDefault();
                var service = services.FirstOrDefault(s => !string.IsNullOrEmpty(row.ServiceName)
                    ? string.Equals(s.ServiceName, row.ServiceName, StringComparison.OrdinalIgnoreCase)
                    : !string.IsNullOrEmpty(row.ServiceCode) && string.Equals(s.ServiceNumber, row.ServiceCode, StringComparison.OrdinalIgnoreCase));

                if (customer == null || port == null || terminal == null || service == null)
                {
                    var missing = new List<string>();
                    if (customer == null)
                    {
                        missing.Add($"Customer '{(string.IsNullOrEmpty(row.CustomerName) ? row.CustomerCode : row.CustomerName)}'");
                    }
                    if (port == null)
                    {
                        missing.Add($"Port '{(string.IsNullOrEmpty(row.PortName) ? row.PortCode : row.PortName)}'");
                    }
                    else if (terminal == null)
                    {
                        missing.Add($"Terminal '{(string.IsNullOrEmpty(row.TerminalName) ? row.TerminalCode : row.TerminalName)}' under Port '{port.PortName}'");
                    }
                    if (service == null)
                    {
                        missing.Add($"Service '{(string.IsNullOrEmpty(row.ServiceName) ? row.ServiceCode : row.ServiceName)}'");
                    }
                    resolutionErrors.Add($"Excel row {row.RowNumber}: unable to match {string.Join(", ", missing)} in MMSI master files.");
                    continue;
                }

                row.CustomerId = customer.CustomerId;
                row.PortId = port.PortId;
                row.TerminalId = terminal.TerminalId;
                row.ServiceId = service.ServiceId;
            }

            if (resolutionErrors.Count > 0)
            {
                return ServiceResult<Guid>.Failure(string.Join("\n", resolutionErrors.Take(25)));
            }

            var draftResult = await SaveDraftAsync(new RateEditorViewModel { Kind = kind, Rows = rows }, userId, ct);
            if (draftResult.IsSuccess && !string.IsNullOrWhiteSpace(fileName))
            {
                var draft = await _db.MsapRateUploads.FindAsync([draftResult.Data], ct);
                if (draft != null)
                {
                    draft.FileName = Path.GetFileName(fileName);
                    await _db.SaveChangesAsync(ct);
                }
            }

            return draftResult;
        }

        public async Task<ServiceResult> SaveAsync(Guid id, string userId, bool refresh, int[] selectedRows, CancellationToken ct)
        {
            ApplicationUser? actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                return ServiceResult.Failure("The account needs a full name of up to 255 characters before recording rate changes.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            RateUpload? update = await LockAsync(id, ct);
            if (update == null || update.UploadedByUserId != userId || update.ApprovedAt.HasValue)
            {
                return ServiceResult.Failure("This draft is unavailable, approved or belongs to another user.");
            }
            string before = update.RowsJson;
            List<RateUploadRowViewModel> rows = JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(update.RowsJson)!;
            if (ValidateRows(rows).Count > 0)
            {
                return ServiceResult.Failure("This draft has invalid rows. Create a new draft in the rate editor.");
            }
            bool changed = await UpdateBaselineAsync(rows, update.RateKind, refresh, ct);
            if (refresh)
            {
                update.SubmittedAt = null;
            }
            else
            {
                if (changed)
                {
                    return ServiceResult.Failure("Stored rates changed. Refresh the comparison and review it before submitting.");
                }
                if (update.SubmittedAt.HasValue)
                {
                    return ServiceResult.Success("This update is already awaiting review.");
                }
                foreach (var row in rows)
                {
                    row.Selected = selectedRows.Contains(row.RowNumber) && row.Change != "Unchanged";
                }
                if (!rows.Any(row => row.Selected))
                {
                    return ServiceResult.Failure("Select at least one new or changed rate.");
                }
                update.SubmittedAt = DateTimeHelper.GetCurrentPhilippineTime();
            }
            update.UploadedBy = actor.Name.Trim();
            update.RowsJson = JsonSerializer.Serialize(rows);
            update.ChangedRows = rows.Count(row => row.Change != "Unchanged");
            _db.AuditTrails.Add(CreateAudit(actor, update, refresh ? "Refreshed comparison" : "Submitted for review", before));
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success(refresh ? "Comparison refreshed. Review before submitting." : "Saved for reviewing. Live rates unchanged.");
        }

        public async Task<ServiceResult> ApproveAsync(Guid id, string userId, CancellationToken ct)
        {
            ApplicationUser? actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                return ServiceResult.Failure("The account needs a full name of up to 255 characters before recording rate changes.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            RateUpload? update = await LockAsync(id, ct);
            if (update == null || !update.SubmittedAt.HasValue)
            {
                return ServiceResult.Failure("Submit this update for review first.");
            }
            if (update.ApprovedAt.HasValue)
            {
                return ServiceResult.Success("This update is already approved.");
            }
            // Serialize activation so two approvals cannot both pass the same baseline check.
            await _db.AcquireAdvisoryXactLockAsync(MsapLockExtensions.RateTableActivation, ct);
            List<RateUploadRowViewModel> rows = JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(update.RowsJson)!.Where(row => row.Selected).ToList();
            if (ValidateRows(rows).Count > 0 || !Enum.IsDefined(update.RateKind))
            {
                return ServiceResult.Failure("The selected rows are invalid. Refresh and review the draft.");
            }
            ServiceResult references = await PopulateReferencesAsync(rows, ct);
            if (!references.IsSuccess)
            {
                return references;
            }
            if (await UpdateBaselineAsync(rows, update.RateKind, false, ct))
            {
                return ServiceResult.Failure("Stored rates changed after review. The preparer must refresh the comparison and resubmit.");
            }
            await PopulateNamesAsync([update], ct);
            await ActivateRowsAsync(update, rows, actor, ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success(
                "Selected rates approved and activated. Eligible unbilled tickets without a prepared billing were automatically recalculated.");
        }

        public async Task<ServiceResult> SubmitAndApproveAsync(Guid id, int[] selectedRows, string userId, CancellationToken ct)
        {
            var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                return ServiceResult.Failure("The account needs a full name of up to 255 characters before recording rate changes.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var update = await LockAsync(id, ct);
            if (update == null)
            {
                return ServiceResult.Failure("Rate update not found.");
            }
            if (update.ApprovedAt.HasValue)
            {
                return ServiceResult.Success("This update is already approved.");
            }

            await _db.AcquireAdvisoryXactLockAsync(MsapLockExtensions.RateTableActivation, ct);

            var allRows = JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(update.RowsJson)!;
            if (!update.SubmittedAt.HasValue)
            {
                if (update.UploadedByUserId != userId)
                {
                    return ServiceResult.Failure("Only the preparer can submit an unsubmitted draft.");
                }
                if (ValidateRows(allRows).Count > 0)
                {
                    return ServiceResult.Failure("This draft has invalid rows. Create a new draft in the rate editor.");
                }
                if (await UpdateBaselineAsync(allRows, update.RateKind, false, ct))
                {
                    return ServiceResult.Failure("Stored rates changed. Refresh the comparison and review it before approving.");
                }
                var before = update.RowsJson;
                foreach (var row in allRows)
                {
                    row.Selected = selectedRows.Contains(row.RowNumber) && row.Change != "Unchanged";
                }
                if (!allRows.Any(row => row.Selected))
                {
                    return ServiceResult.Failure("Select at least one new or changed rate.");
                }
                update.SubmittedAt = DateTimeHelper.GetCurrentPhilippineTime();
                update.UploadedBy = actor.Name.Trim();
                update.RowsJson = JsonSerializer.Serialize(allRows);
                update.ChangedRows = allRows.Count(row => row.Change != "Unchanged");
                _db.AuditTrails.Add(CreateAudit(actor, update, "Submitted for review", before));
            }

            var selected = allRows.Where(row => row.Selected).ToList();
            if (ValidateRows(selected).Count > 0 || !Enum.IsDefined(update.RateKind))
            {
                return ServiceResult.Failure("The selected rows are invalid. Refresh and review the draft.");
            }
            var references = await PopulateReferencesAsync(selected, ct);
            if (!references.IsSuccess)
            {
                return references;
            }
            if (await UpdateBaselineAsync(selected, update.RateKind, false, ct))
            {
                return ServiceResult.Failure("Stored rates changed after review. The preparer must refresh the comparison and resubmit.");
            }
            await PopulateNamesAsync([update], ct);
            await ActivateRowsAsync(update, selected, actor, ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success(
                "Selected rates approved and activated. Eligible unbilled tickets without a prepared billing were automatically recalculated.");
        }

        private async Task ActivateRowsAsync(RateUpload update, List<RateUploadRowViewModel> rows,
            ApplicationUser actor, CancellationToken ct)
        {
            var now = DateTimeHelper.GetCurrentPhilippineTime();
            var existingTariffIds = update.RateKind == RateTableKind.Tariff
                ? rows.Where(r => r.ExistingRateId.HasValue).Select(r => r.ExistingRateId!.Value).Distinct().ToList()
                : [];
            var existingBafIds = update.RateKind == RateTableKind.BAF
                ? rows.Where(r => r.ExistingRateId.HasValue).Select(r => r.ExistingRateId!.Value).Distinct().ToList()
                : [];

            var existingTariffRates = existingTariffIds.Count != 0
                ? await _db.MsapTariffRates.Where(r => existingTariffIds.Contains(r.TariffRateId)).ToDictionaryAsync(r => r.TariffRateId, ct)
                : [];
            var existingBafRates = existingBafIds.Count != 0
                ? await _db.MsapBafRates.Where(r => existingBafIds.Contains(r.BafRateId)).ToDictionaryAsync(r => r.BafRateId, ct)
                : [];

            foreach (var row in rows)
            {
                if (update.RateKind == RateTableKind.Tariff)
                {
                    var rate = row.ExistingRateId.HasValue && existingTariffRates.TryGetValue(row.ExistingRateId.Value, out var foundTariff)
                        ? foundTariff
                        : new TariffRate { CustomerId = row.CustomerId, PortId = row.PortId, TerminalId = row.TerminalId, ServiceId = row.ServiceId, AsOfDate = row.EffectiveDate, CreatedBy = update.UploadedBy, CreatedDate = now };
                    rate.Dispatch = row.Amount!.Value;
                    rate.UpdateBy = actor.Name.Trim();
                    rate.UpdateDate = now;
                    rate.ApprovedBy = actor.Name.Trim();
                    rate.ApprovedAt = now;
                    if (!row.ExistingRateId.HasValue)
                    {
                        _db.MsapTariffRates.Add(rate);
                    }
                }
                else
                {
                    var rate = row.ExistingRateId.HasValue && existingBafRates.TryGetValue(row.ExistingRateId.Value, out var foundBaf)
                        ? foundBaf
                        : new BafRate { CustomerId = row.CustomerId, PortId = row.PortId, TerminalId = row.TerminalId, ServiceId = row.ServiceId, AsOfDate = row.EffectiveDate, CreatedBy = update.UploadedBy, CreatedDate = now };
                    rate.Amount = row.Amount!.Value;
                    rate.UpdateBy = actor.Name.Trim();
                    rate.UpdateDate = now;
                    rate.ApprovedBy = actor.Name.Trim();
                    rate.ApprovedAt = now;
                    if (!row.ExistingRateId.HasValue)
                    {
                        _db.MsapBafRates.Add(rate);
                    }
                }
            }
            update.ApprovedAt = now;
            update.ApprovedBy = actor.Name.Trim();
            _db.AuditTrails.Add(CreateAudit(actor, update, "Approved selected rates", update.RowsJson, true));
            await _db.SaveChangesAsync(ct);
            await AutoRecalculateEligibleTicketsAsync(update, rows, actor, ct);
        }

        private async Task AutoRecalculateEligibleTicketsAsync(RateUpload update, List<RateUploadRowViewModel> rows,
            ApplicationUser actor, CancellationToken ct)
        {
            if (rows.Count == 0)
            {
                return;
            }
            bool isTariff = update.RateKind == RateTableKind.Tariff;
            List<int> customers = rows.Select(r => r.CustomerId).Distinct().ToList();
            DateOnly minDate = rows.Min(r => r.EffectiveDate);

            List<DispatchTicket> candidates = await _db.MsapDispatchTickets
                .Include(t => t.CustomerOrderSlip)
                .Include(t => t.ExpectedServiceEntry)
                .Include(t => t.Service)
                .Where(t => customers.Contains(t.CustomerId)
                    && t.Customer.Company == MsapConstants.Company_MMSI
                    && t.DateLeft != null && t.DateLeft >= minDate
                    && t.CustomerOrderSlipId != null && t.VesselScheduleServiceEntryId != null
                    && t.BillingId == null
                    && t.CustomerOrderSlip!.Status == MsapConstants.CustomerOrderSlipStatus.Open
                    && (t.Status == MsapConstants.DispatchTicketStatus.AwaitingRate || t.Status == MsapConstants.DispatchTicketStatus.ForBilling)
                    && !_db.MsapBillings.Any(b => b.CustomerOrderSlipId == t.CustomerOrderSlipId))
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                return;
            }

            string actorName = actor.Name.Trim();
            foreach (var cosGroup in candidates.GroupBy(t => t.CustomerOrderSlipId!.Value))
            {
                var booking = await _work.VesselSchedule.GetAsync(s => s.CustomerOrderSlipId == cosGroup.Key, ct);
                var schedule = booking == null
                    ? null
                    : await _work.VesselSchedule.GetScheduleWithDetailsAsync(booking.VesselScheduleId, ct);
                if (schedule == null || schedule.ApprovedRevision != schedule.Revision
                    || schedule.Status is not (MsapConstants.VesselScheduleStatus.Confirmed or MsapConstants.VesselScheduleStatus.InProgress or MsapConstants.VesselScheduleStatus.Completed))
                {
                    continue;
                }

                bool cosChanged = false;
                foreach (DispatchTicket ticket in cosGroup)
                {
                    if (await _work.PostedPeriod.IsMonthClosedAsync(ticket.Date.Year, ticket.Date.Month, ct)
                        || (ticket.DateLeft.HasValue && await _work.PostedPeriod.IsMonthClosedAsync(ticket.DateLeft.Value.Year, ticket.DateLeft.Value.Month, ct)))
                    {
                        continue;
                    }
                    if (ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling
                        && isTariff && ticket.IsManualTariffRate && ticket.ExpectedServiceEntry?.IsManualTariffOverride == true)
                    {
                        continue;
                    }

                    string oldStatus = ticket.Status;
                    decimal oldTariff = ticket.DispatchRate;
                    decimal oldBaf = ticket.BAFRate;
                    decimal oldNet = ticket.TotalNetRevenue;

                    string? blocker = await ScheduledTicketCharges.ApplyAsync(_work, ticket, schedule, actorName, ct);
                    if (ticket.Status != oldStatus || ticket.DispatchRate != oldTariff || ticket.BAFRate != oldBaf || ticket.TotalNetRevenue != oldNet)
                    {
                        cosChanged = true;
                        _db.AuditTrails.Add(new AuditTrail(actorName,
                            $"Auto-recalculated ticket #{ticket.DispatchNumber} from Rate Upload #{update.RateUploadId}: {oldStatus} -> {ticket.Status}; tariff {oldTariff} -> {ticket.DispatchRate}; BAF {oldBaf} -> {ticket.BAFRate}; net {oldNet} -> {ticket.TotalNetRevenue}. {blocker ?? "Charges calculated"}",
                            "Dispatch Ticket", ticket.DispatchTicketId, ticket.DispatchNumber)
                        {
                            UserId = actor.Id,
                            ActorName = actorName
                        });
                    }
                }

                if (cosChanged)
                {
                    List<DispatchTicket> allCosTickets = await _db.MsapDispatchTickets
                        .Where(t => t.CustomerOrderSlipId == cosGroup.Key && t.Status != MsapConstants.DispatchTicketStatus.Deleted)
                        .ToListAsync(ct);
                    SbmaShareCalculator.Apply(allCosTickets, schedule.SbmaEnabled);
                }
            }
        }

        private async Task PopulateAffectedTicketsAsync(RateUploadViewModel model, CancellationToken ct)
        {
            var included = model.Rows.Where(row => row.Selected).ToList();
            if (included.Count == 0)
            {
                return;
            }
            bool tariff = model.Upload!.RateKind == RateTableKind.Tariff;
            List<int> customers = included.Select(row => row.CustomerId).Distinct().ToList();
            DateOnly from = included.Min(row => row.EffectiveDate);
            // shortcut: review the latest 200 candidate tickets; use the COS review for older jobs until impact paging is needed.
            List<DispatchTicket> candidates = await _db.MsapDispatchTickets.AsNoTracking().Include(ticket => ticket.CustomerOrderSlip).Include(ticket => ticket.ExpectedServiceEntry)
                .Where(ticket => customers.Contains(ticket.CustomerId) && ticket.Customer.Company == MsapConstants.Company_MMSI
                    && ticket.DateLeft >= from && ticket.CustomerOrderSlipId != null
                    && (!tariff || !ticket.IsManualTariffRate
                        || ticket.ExpectedServiceEntry != null && ticket.ExpectedServiceEntry.IsManualTariffOverride == false)
                    && ticket.BillingId == null && ticket.CustomerOrderSlip!.Status == MsapConstants.CustomerOrderSlipStatus.Open
                    && (ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling || ticket.Status == MsapConstants.DispatchTicketStatus.AwaitingRate)
                    && !_db.MsapBillings.Any(bill => bill.CustomerOrderSlipId == ticket.CustomerOrderSlipId))
                .OrderByDescending(ticket => ticket.DateLeft).ThenByDescending(ticket => ticket.DispatchTicketId).Take(201).ToListAsync(ct);
            model.ImpactLimitReached = candidates.Count > 200;
            foreach (var ticket in candidates.Take(200))
            {
                TariffRate? basic = tariff ? await _work.TariffTable.GetApplicableRateAsync(ticket.CustomerId, ticket.TerminalId, ticket.ServiceId, ticket.DateLeft!.Value, ct) : null;
                BafRate? baf = !tariff ? await _work.BafTable.GetApplicableRateAsync(ticket.CustomerId, ticket.TerminalId, ticket.ServiceId, ticket.DateLeft!.Value, ct) : null;
                if (tariff && ticket.ExpectedServiceEntry != null
                    && ScheduledTicketCharges.ManualTariffForServiceDate(ticket, ticket.ExpectedServiceEntry, basic).HasValue)
                {
                    continue;
                }
                DateOnly? date = tariff ? basic?.AsOfDate : baf?.AsOfDate;
                decimal? amount = tariff ? basic?.Dispatch : baf?.Amount;
                if (!amount.HasValue || (!tariff && baf != null && baf.AsOfDate < ScheduledTicketCharges.BafCycleStart(ticket.DateLeft!.Value))
                    || !included.Any(row => row.CustomerId == ticket.CustomerId && row.TerminalId == ticket.TerminalId
                    && row.ServiceId == ticket.ServiceId && row.EffectiveDate == date)
                    || (ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling && (tariff ? ticket.DispatchRate : ticket.BAFRate) == amount
                        && (tariff || !ticket.IsManualBafRate)))
                {
                    continue;
                }
                model.AffectedTickets.Add(new TicketChargeReviewViewModel
                {
                    DispatchTicketId = ticket.DispatchTicketId, CustomerOrderSlipId = ticket.CustomerOrderSlipId!.Value,
                    TicketNumber = ticket.DispatchNumber, CosNumber = ticket.CustomerOrderSlip!.CustomerOrderSlipNumber,
                    HasSavedCharges = ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling,
                    SavedBafRate = ticket.BAFRate, ProposedBafRate = baf?.Amount ?? ticket.BAFRate,
                    SavedTariffRate = ticket.DispatchRate, ProposedTariffRate = basic?.Dispatch ?? ticket.DispatchRate
                });
            }
        }

        private async Task PopulateNamesAsync(List<RateUpload> updates, CancellationToken ct)
        {
            List<string> ids = updates.Select(update => update.UploadedByUserId).Distinct().ToList();
            List<string> approvals = updates.Where(update => update.ApprovedBy != null).Select(update => update.ApprovedBy!).Distinct().ToList();
            var users = await _db.Users.AsNoTracking().Where(user => ids.Contains(user.Id) || user.UserName != null && approvals.Contains(user.UserName))
                .Select(user => new { user.Id, user.UserName, user.Name }).ToListAsync(ct);
            foreach (RateUpload update in updates)
            {
                string? name = users.SingleOrDefault(user => user.Id == update.UploadedByUserId)?.Name;
                update.UploadedBy = string.IsNullOrWhiteSpace(name) ? "Name unavailable" : name.Trim();
                string? approver = users.SingleOrDefault(user => user.UserName == update.ApprovedBy)?.Name;
                if (!string.IsNullOrWhiteSpace(approver))
                {
                    update.ApprovedBy = approver.Trim();
                }
            }
        }

        private static AuditTrail CreateAudit(ApplicationUser actor, RateUpload update, string action, string before, bool activating = false)
        {
            static string Snapshot(string json, bool activating, bool previous)
            {
                List<RateUploadRowViewModel> rows = JsonSerializer.Deserialize<List<RateUploadRowViewModel>>(json)!;
                return JsonSerializer.Serialize(rows.Where(row => !activating || row.Selected).Select(row => new
                {
                    row.CustomerId, row.PortId, row.TerminalId, row.ServiceId,
                    row.EffectiveDate, BaselineAmount = row.StoredAmount, row.BaselineDate,
                    row.SourceScheduleId, row.SourceServiceEntryId, row.SourceRevision,
                    Amount = activating && previous ? row.StoredAmount : row.Amount, row.Selected
                }));
            }
            return new AuditTrail(actor.UserName ?? actor.Id,
                $"{action}: {update.RateKind}. Before: {Snapshot(before, activating, true)}. After: {Snapshot(update.RowsJson, activating, false)}.",
                "Rate maintenance", referenceNumber: update.RateUploadId.ToString())
            {
                UserId = actor.Id,
                ActorName = actor.Name.Trim()
            };
        }

        private async Task<ServiceResult> PopulateReferencesAsync(List<RateUploadRowViewModel> rows, CancellationToken ct)
        {
            Dictionary<int, Customer> customers = await _db.Customers.AsNoTracking().Where(customer => customer.Company == MsapConstants.Company_MMSI).ToDictionaryAsync(customer => customer.CustomerId, ct);
            Dictionary<int, Terminal> terminals = await _db.MsapTerminals.AsNoTracking().ToDictionaryAsync(terminal => terminal.TerminalId, ct);
            Dictionary<int, Port> ports = await _db.MsapPorts.AsNoTracking().ToDictionaryAsync(port => port.PortId, ct);
            Dictionary<int, Service> services = await _db.MsapServices.AsNoTracking().ToDictionaryAsync(service => service.ServiceId, ct);
            for (int index = 0; index < rows.Count; index++)
            {
                RateUploadRowViewModel row = rows[index];
                if (!customers.TryGetValue(row.CustomerId, out var customer) || !ports.TryGetValue(row.PortId, out var port)
                    || !terminals.TryGetValue(row.TerminalId, out var terminal) || terminal.PortId != row.PortId
                    || !services.TryGetValue(row.ServiceId, out var service))
                {
                    return ServiceResult.Failure($"Row {index + 1}: invalid MMSI customer, port, terminal or service. The terminal must belong to the selected port.");
                }
                row.RowNumber = index + 1;
                row.CustomerCode = customer.CustomerCode;
                row.CustomerName = customer.CustomerName;
                row.PortCode = port.PortNumber;
                row.PortName = port.PortName;
                row.TerminalCode = terminal.TerminalNumber;
                row.TerminalName = terminal.TerminalName;
                row.ServiceCode = service.ServiceNumber;
                row.ServiceName = service.ServiceName;
            }
            return ServiceResult.Success("References valid.");
        }

        private async Task<RateUpload?> LockAsync(Guid id, CancellationToken ct)
        {
            return await _db.QueryForUpdate<RateUpload, Guid>(id)
                .SingleOrDefaultAsync(update => update.Company == MsapConstants.Company_MMSI, ct);
        }

        private async Task<bool> UpdateBaselineAsync(List<RateUploadRowViewModel> rows, RateTableKind kind, bool apply, CancellationToken ct)
        {
            if (rows.Count == 0)
            {
                return false;
            }
            var maxDate = rows.Max(row => row.EffectiveDate);
            var customerIds = rows.Select(row => row.CustomerId).Distinct().ToList();
            var terminalIds = rows.Select(row => row.TerminalId).Distinct().ToList();
            var serviceIds = rows.Select(row => row.ServiceId).Distinct().ToList();
            var keys = rows.Select(row => (row.CustomerId, row.TerminalId, row.ServiceId)).ToHashSet();

            List<(int CustomerId, int TerminalId, int ServiceId, DateOnly EffectiveDate, int RateId, DateTime? UpdatedAt, decimal Amount)> rates;
            if (kind == RateTableKind.Tariff)
            {
                var raw = await _db.MsapTariffRates.AsNoTracking()
                    .Where(rate => customerIds.Contains(rate.CustomerId)
                        && terminalIds.Contains(rate.TerminalId)
                        && serviceIds.Contains(rate.ServiceId)
                        && rate.AsOfDate <= maxDate
                        && rate.Customer.Company == MsapConstants.Company_MMSI)
                    .Select(rate => new
                    {
                        rate.CustomerId,
                        rate.TerminalId,
                        rate.ServiceId,
                        rate.AsOfDate,
                        RateId = rate.TariffRateId,
                        UpdatedAt = rate.UpdateDate ?? rate.CreatedDate,
                        Amount = rate.Dispatch
                    })
                    .ToListAsync(ct);
                rates = raw.Where(r => keys.Contains((r.CustomerId, r.TerminalId, r.ServiceId)))
                    .Select(r => (r.CustomerId, r.TerminalId, r.ServiceId, r.AsOfDate, r.RateId, r.UpdatedAt, r.Amount))
                    .ToList();
            }
            else
            {
                var raw = await _db.MsapBafRates.AsNoTracking()
                    .Where(rate => customerIds.Contains(rate.CustomerId)
                        && terminalIds.Contains(rate.TerminalId)
                        && serviceIds.Contains(rate.ServiceId)
                        && rate.AsOfDate <= maxDate
                        && rate.Customer.Company == MsapConstants.Company_MMSI)
                    .Select(rate => new
                    {
                        rate.CustomerId,
                        rate.TerminalId,
                        rate.ServiceId,
                        rate.AsOfDate,
                        RateId = rate.BafRateId,
                        UpdatedAt = rate.UpdateDate ?? rate.CreatedDate,
                        Amount = rate.Amount
                    })
                    .ToListAsync(ct);
                rates = raw.Where(r => keys.Contains((r.CustomerId, r.TerminalId, r.ServiceId)))
                    .Select(r => (r.CustomerId, r.TerminalId, r.ServiceId, r.AsOfDate, r.RateId, r.UpdatedAt, r.Amount))
                    .ToList();
            }

            var lookup = rates.ToLookup(rate => (rate.CustomerId, rate.TerminalId, rate.ServiceId));
            var changed = false;
            foreach (var row in rows)
            {
                var baseline = lookup[(row.CustomerId, row.TerminalId, row.ServiceId)]
                    .Where(rate => rate.EffectiveDate <= row.EffectiveDate)
                    .OrderByDescending(rate => rate.EffectiveDate)
                    .ThenByDescending(rate => rate.RateId)
                    .Select(rate => new
                    {
                        rate.EffectiveDate,
                        rate.RateId,
                        rate.UpdatedAt,
                        rate.Amount
                    })
                    .FirstOrDefault();
                int? existingId = baseline?.EffectiveDate == row.EffectiveDate ? baseline.RateId : null;
                changed |= row.ExistingRateId != existingId || row.BaselineRateId != baseline?.RateId
                    || row.BaselineDate != baseline?.EffectiveDate || row.BaselineUpdatedAt != baseline?.UpdatedAt || row.StoredAmount != baseline?.Amount;
                if (apply)
                {
                    row.ExistingRateId = existingId;
                    row.BaselineRateId = baseline?.RateId;
                    row.BaselineDate = baseline?.EffectiveDate;
                    row.BaselineUpdatedAt = baseline?.UpdatedAt;
                    row.StoredAmount = baseline?.Amount;
                }
            }
            return changed;
        }
    }
}
