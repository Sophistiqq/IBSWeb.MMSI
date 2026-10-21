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

namespace IBS.Services.MSAP
{
    public class RateUploadService
    {
        private readonly MsapDbContext _db;
        private readonly IUnitOfWork _work;

        public RateUploadService(MsapDbContext db, IUnitOfWork work)
        {
            _db = db;
            _work = work;
        }

        public async Task<List<RateUpload>> GetRecentAsync(bool awaitingReview, CancellationToken ct)
        {
            List<RateUpload> updates = await _db.MsapRateUploads.AsNoTracking()
                .Where(update => update.Company == MsapConstants.Company_MMSI && (!awaitingReview || update.SubmittedAt != null && update.ApprovedAt == null))
                .OrderByDescending(update => update.UploadedAt).ThenBy(update => update.RateUploadId).Take(50)
                .Select(update => new RateUpload
                {
                    RateUploadId = update.RateUploadId, Company = update.Company, FileName = update.FileName,
                    UploadedByUserId = update.UploadedByUserId, UploadedBy = update.UploadedBy,
                    UploadedAt = update.UploadedAt, SubmittedAt = update.SubmittedAt, RateKind = update.RateKind,
                    ApprovedAt = update.ApprovedAt, ApprovedBy = update.ApprovedBy,
                    TotalRows = update.TotalRows, ChangedRows = update.ChangedRows
                }).ToListAsync(ct);
            await PopulateNamesAsync(updates, ct);
            return updates;
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
            if (update.ApprovedAt.HasValue && update.RateKind == RateTableKind.BAF)
            {
                await PopulateAffectedTicketsAsync(model, ct);
            }
            return model;
        }

        public async Task<RateEditorViewModel?> GetEditorAsync(RateTableKind kind, DateOnly asOf, Guid? id, string userId, CancellationToken ct)
        {
            if (!Enum.IsDefined(kind) || asOf.Year is < 1900 or > 9998)
            {
                return null;
            }
            var model = new RateEditorViewModel { Kind = kind, AsOfDate = asOf, Id = id };
            if (id.HasValue)
            {
                RateUploadViewModel? saved = await GetAsync(id.Value, userId, ct);
                if (saved == null || !saved.IsOwner || saved.Upload!.SubmittedAt.HasValue || saved.Upload.RateKind != kind)
                {
                    return null;
                }
                model.Rows = saved.Rows;
            }
            else
            {
                model.Rows = await GetRowsAsync(kind, null, asOf, ct);
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
                if (!row.Amount.HasValue || row.Amount < 0 || row.Amount > 99999999999999.9999m || decimal.Round(row.Amount.Value, 4) != row.Amount)
                {
                    errors.Add($"Row {index + 1}: enter a nonnegative amount with at most four decimal places. Blank is not zero.");
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
            await _db.Database.ExecuteSqlRawAsync("LOCK TABLE msap.mmsi_tariff_rates, msap.mmsi_baf_rates IN SHARE ROW EXCLUSIVE MODE", ct);
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
            DateTime now = DateTimeHelper.GetCurrentPhilippineTime();
            foreach (var row in rows)
            {
                if (update.RateKind == RateTableKind.Tariff)
                {
                    TariffRate rate = row.ExistingRateId.HasValue ? await _db.MsapTariffRates.SingleAsync(rate => rate.TariffRateId == row.ExistingRateId, ct)
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
                    BafRate rate = row.ExistingRateId.HasValue ? await _db.MsapBafRates.SingleAsync(rate => rate.BafRateId == row.ExistingRateId, ct)
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
            await transaction.CommitAsync(ct);
            return ServiceResult.Success("Selected rates approved and activated. Existing ticket amounts were not changed; pending charges can now be recalculated from the COS.");
        }

        private async Task PopulateAffectedTicketsAsync(RateUploadViewModel model, CancellationToken ct)
        {
            var included = model.Rows.Where(row => row.Selected).ToList();
            if (included.Count == 0)
            {
                return;
            }
            List<int> customers = included.Select(row => row.CustomerId).Distinct().ToList();
            DateOnly from = included.Min(row => row.EffectiveDate);
            // shortcut: review the latest 200 candidate tickets; use the COS review for older jobs until impact paging is needed.
            var candidates = await _db.MsapDispatchTickets.AsNoTracking().Include(ticket => ticket.CustomerOrderSlip)
                .Where(ticket => customers.Contains(ticket.CustomerId) && ticket.Customer.Company == MsapConstants.Company_MMSI
                    && ticket.DateLeft >= from && ticket.CustomerOrderSlipId != null && !ticket.IsManualBafRate
                    && ticket.BillingId == null && ticket.CustomerOrderSlip!.Status == MsapConstants.CustomerOrderSlipStatus.Open
                    && (ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling || ticket.Status == MsapConstants.DispatchTicketStatus.CalculationPending)
                    && !_db.MsapBillings.Any(bill => bill.CustomerOrderSlipId == ticket.CustomerOrderSlipId))
                .OrderByDescending(ticket => ticket.DateLeft).ThenByDescending(ticket => ticket.DispatchTicketId).Take(201).ToListAsync(ct);
            model.ImpactLimitReached = candidates.Count > 200;
            foreach (var ticket in candidates.Take(200))
            {
                var rate = await _work.BafTable.GetApplicableRateAsync(ticket.CustomerId, ticket.TerminalId, ticket.ServiceId, ticket.DateLeft!.Value, ct);
                if (rate == null || !included.Any(row => row.CustomerId == rate.CustomerId && row.TerminalId == rate.TerminalId
                    && row.ServiceId == rate.ServiceId && row.EffectiveDate == rate.AsOfDate)
                    || ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling && ticket.BAFRate == rate.Amount)
                {
                    continue;
                }
                model.AffectedTickets.Add(new TicketChargeReviewViewModel
                {
                    DispatchTicketId = ticket.DispatchTicketId, CustomerOrderSlipId = ticket.CustomerOrderSlipId!.Value,
                    TicketNumber = ticket.DispatchNumber, CosNumber = ticket.CustomerOrderSlip!.CustomerOrderSlipNumber,
                    HasSavedCharges = ticket.Status == MsapConstants.DispatchTicketStatus.ForBilling,
                    SavedBafRate = ticket.BAFRate, ProposedBafRate = rate.Amount
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
            return await _db.MsapRateUploads.FromSqlInterpolated($"SELECT * FROM msap.mmsi_rate_uploads WHERE rate_upload_id = {id} FOR UPDATE")
                .SingleOrDefaultAsync(update => update.Company == MsapConstants.Company_MMSI, ct);
        }

        private async Task<bool> UpdateBaselineAsync(List<RateUploadRowViewModel> rows, RateTableKind kind, bool apply, CancellationToken ct)
        {
            if (rows.Count == 0)
            {
                return false;
            }
            List<RateUploadRowViewModel> rates = await GetRowsAsync(kind, new DateOnly(1900, 1, 1), rows.Max(row => row.EffectiveDate), ct);
            ILookup<(int CustomerId, int TerminalId, int ServiceId), RateUploadRowViewModel> lookup = rates.ToLookup(rate => (rate.CustomerId, rate.TerminalId, rate.ServiceId));
            bool changed = false;
            foreach (var row in rows)
            {
                RateUploadRowViewModel? baseline = lookup[(row.CustomerId, row.TerminalId, row.ServiceId)].Where(rate => rate.EffectiveDate <= row.EffectiveDate)
                    .OrderByDescending(rate => rate.EffectiveDate).ThenByDescending(rate => rate.ExistingRateId).FirstOrDefault();
                int? existingId = baseline?.EffectiveDate == row.EffectiveDate ? baseline.ExistingRateId : null;
                changed |= row.ExistingRateId != existingId || row.BaselineRateId != baseline?.ExistingRateId
                    || row.BaselineDate != baseline?.EffectiveDate || row.BaselineUpdatedAt != baseline?.BaselineUpdatedAt || row.StoredAmount != baseline?.Amount;
                if (apply)
                {
                    row.ExistingRateId = existingId;
                    row.BaselineRateId = baseline?.ExistingRateId;
                    row.BaselineDate = baseline?.EffectiveDate;
                    row.BaselineUpdatedAt = baseline?.BaselineUpdatedAt;
                    row.StoredAmount = baseline?.Amount;
                }
            }
            return changed;
        }
    }
}
