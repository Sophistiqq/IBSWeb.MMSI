using System.Text.Json;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap;
using IBS.Models.Enums;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.Books;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class OtherTugChargeService
    {
        public const string ContraRevenueAccount = "401020400";
        public const string InputVatAccount = "101060200";
        private readonly MsapDbContext _db;
        private readonly ICloudStorageService _storage;
        private readonly ILogger<OtherTugChargeService> _logger;
        private readonly IAccountingContextFactory _accountingContextFactory;

        public OtherTugChargeService(
            MsapDbContext db,
            ICloudStorageService storage,
            ILogger<OtherTugChargeService> logger,
            IAccountingContextFactory? accountingContextFactory = null)
        {
            _db = db;
            _storage = storage;
            _logger = logger;
            _accountingContextFactory = accountingContextFactory ?? AccountingContextFactory.Default;
        }

        private IQueryable<DispatchTicket> EligibleTickets()
        {
            return DispatchTicketRepository.QueryOtherTugChargeTickets(_db);
        }

        private IQueryable<DispatchTicket> AllocationTickets(int cosId, int? chargeId)
        {
            return EligibleTickets().Where(t => t.CustomerOrderSlipId == cosId
                || chargeId.HasValue && t.OtherTugChargeLine != null && t.OtherTugChargeLine.OtherTugChargeId == chargeId);
        }

        public async Task<List<OtherTugCharge>> GetForCosAsync(int cosId, CancellationToken ct)
        {
            return await _db.MsapOtherTugCharges.AsNoTracking().Include(c => c.Lines).ThenInclude(l => l.Ticket)
                .Where(c => c.Lines.Any(l => l.Ticket.CustomerOrderSlipId == cosId && l.Ticket.Customer.Company == MsapConstants.Company_MMSI))
                .OrderByDescending(c => c.UpdatedAt).ToListAsync(ct);
        }

        public async Task<OtherTugChargeViewModel?> GetEditorAsync(int cosId, int? id, int? ticketId, CancellationToken ct)
        {
            var charge = id.HasValue ? await _db.MsapOtherTugCharges.AsNoTracking().Include(c => c.Lines).ThenInclude(l => l.Ticket)
                .SingleOrDefaultAsync(c => c.OtherTugChargeId == id && c.Lines.Any(l => l.Ticket.CustomerOrderSlipId == cosId
                    && l.Ticket.Customer.Company == MsapConstants.Company_MMSI), ct) : null;
            if (id.HasValue && charge == null)
            {
                return null;
            }
            var initial = charge == null ? await EligibleTickets().AsNoTracking().Include(t => t.Tugboat).ThenInclude(t => t.TugboatOwner)
                .SingleOrDefaultAsync(t => t.DispatchTicketId == ticketId && t.CustomerOrderSlipId == cosId, ct) : null;
            if (charge == null && initial == null)
            {
                return null;
            }
            if (charge == null)
            {
                int? existingId = await _db.MsapOtherTugChargeLines.Where(l => l.DispatchTicketId == initial!.DispatchTicketId)
                    .Select(l => (int?)l.OtherTugChargeId).SingleOrDefaultAsync(ct);
                if (existingId.HasValue)
                {
                    return await GetEditorAsync(cosId, existingId, null, ct);
                }
            }
            int ownerId = charge?.TugboatOwnerId ?? initial!.Tugboat.TugboatOwnerId!.Value;
            var model = new OtherTugChargeViewModel
            {
                Id = charge?.OtherTugChargeId, Revision = charge?.Revision ?? 0, CustomerOrderSlipId = cosId,
                TugboatOwnerId = ownerId, ProviderName = charge?.ProviderName ?? initial!.Tugboat.TugboatOwner!.TugboatOwnerName,
                ProviderReference = charge?.ProviderReference ?? "", DocumentDate = charge?.DocumentDate ?? DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime()),
                IncludesVat = charge?.IncludesVat ?? true, Remarks = charge?.Remarks, Charge = charge
            };
            var query = AllocationTickets(cosId, id).AsNoTracking().Where(t => t.Tugboat.TugboatOwnerId == ownerId
                && (t.OtherTugChargeLine == null || t.OtherTugChargeLine.OtherTugChargeId == id));
            if (charge?.ConfirmedAt != null)
            {
                query = _db.MsapDispatchTickets.AsNoTracking().Where(t => t.Customer.Company == MsapConstants.Company_MMSI
                    && t.OtherTugChargeLine != null && t.OtherTugChargeLine.OtherTugChargeId == id);
            }
            model.Tickets = await query.OrderBy(t => t.CustomerOrderSlipId).ThenBy(t => t.DispatchTicketId).Select(t => new OtherTugChargeTicketViewModel
            {
                DispatchTicketId = t.DispatchTicketId, TicketNumber = t.DispatchNumber, CosNumber = t.CustomerOrderSlip!.CustomerOrderSlipNumber,
                TugboatName = t.Tugboat.TugboatName, ServiceName = t.Service.ServiceName, ServiceDate = t.DateLeft
            }).ToListAsync(ct);
            foreach (var ticket in model.Tickets)
            {
                var line = charge?.Lines.SingleOrDefault(l => l.DispatchTicketId == ticket.DispatchTicketId);
                ticket.Selected = line != null || ticket.DispatchTicketId == ticketId;
                ticket.Amount = line?.Amount;
            }
            if (charge?.DocumentName != null)
            {
                try { model.DocumentUrl = await _storage.GetSignedUrlAsync(charge.DocumentName); }
                catch (Exception ex) { _logger.LogWarning(ex, "Provider charge document is unavailable for {ChargeId}", id); }
            }
            return model;
        }

        public static async Task<bool> IsSupportedDocumentAsync(IFormFile document, CancellationToken ct)
        {
            string extension = Path.GetExtension(document.FileName).ToLowerInvariant();
            string? contentType = extension switch
            {
                ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => null
            };
            if (contentType == null || document.ContentType != contentType || document.Length is <= 0 or > 10 * 1024 * 1024)
            {
                return false;
            }
            byte[] bytes = new byte[8];
            await using var stream = document.OpenReadStream();
            int count = await stream.ReadAtLeastAsync(bytes, 8, false, ct);
            return extension switch
            {
                ".pdf" => count >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
                ".png" => count >= 8 && bytes.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                _ => count >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
            };
        }

        public static string? ValidateAllocations(OtherTugChargeViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.ProviderReference) || model.ProviderReference.Trim().Length > 100
                || model.DocumentDate.Year is < 1900 or > 9998 || model.DocumentDate > DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime())
                || model.Remarks?.Length > 500 || !model.IncludesVat.HasValue)
            {
                return "Enter a provider reference, a valid document date no later than today, the VAT-inclusion choice, and remarks of up to 500 characters.";
            }
            if (model.Allocations == null || model.Allocations.Count is < 1 or > 500
                || model.Allocations.Any(l => l.DispatchTicketId <= 0 || !l.Amount.HasValue || l.Amount < 0 || l.Amount > 99999999999999.99m
                    || decimal.Round(l.Amount.Value, 2) != l.Amount.Value)
                || model.Allocations.Select(l => l.DispatchTicketId).Distinct().Count() != model.Allocations.Count)
            {
                return "Select up to 500 distinct actual tickets and enter each provider charge to two decimal places. Zero is valid; blank is pending.";
            }
            if (model.Allocations.Sum(l => l.Amount!.Value) > 99999999999999.99m)
            {
                return "The provider bill total exceeds the supported amount.";
            }
            return null;
        }

        public async Task<ServiceResult<int>> SaveAsync(OtherTugChargeViewModel model, IFormFile? document, string userId, CancellationToken ct)
        {
            string? invalid = ValidateAllocations(model);
            if (invalid != null)
            {
                return ServiceResult<int>.Failure(invalid);
            }
            string? uploaded = null;
            string? oldDocument = null;
            try
            {
                var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
                if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
                {
                    return ServiceResult<int>.Failure("An active account with a full name is required.");
                }
                await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                var charge = model.Id.HasValue ? await LockAsync(model.Id.Value, ct) : new OtherTugCharge();
                if (charge == null || charge.ConfirmedAt.HasValue || model.Id.HasValue && charge.Revision != model.Revision)
                {
                    return ServiceResult<int>.Failure("The charge was confirmed or changed. Reload it before saving.");
                }
                if (charge.PeriodStatus is not (MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted))
                {
                    var periodError = await GuardClosedPeriodAsync(model.DocumentDate, ct)
                        ?? (model.Id.HasValue ? await GuardClosedPeriodAsync(charge.DocumentDate, ct) : null);
                    if (periodError != null)
                    {
                        return ServiceResult<int>.Failure(periodError);
                    }
                }
                string before = Snapshot(charge);
                List<int> ids = model.Allocations.Select(l => l.DispatchTicketId).Order().ToList();
                foreach (var id in ids)
                {
                    await _db.LockRowForUpdateAsync<DispatchTicket, int>(id, ct);
                }
                var tickets = await AllocationTickets(model.CustomerOrderSlipId, model.Id).Include(t => t.Tugboat).ThenInclude(t => t.TugboatOwner)
                    .Where(t => ids.Contains(t.DispatchTicketId)).ToListAsync(ct);
                if (tickets.Count != ids.Count
                    || tickets.Select(t => t.Tugboat.TugboatOwnerId).Distinct().Count() != 1
                    || model.Id.HasValue && tickets[0].Tugboat.TugboatOwnerId != charge.TugboatOwnerId
                    || await _db.MsapOtherTugChargeLines.AnyAsync(l => ids.Contains(l.DispatchTicketId) && l.OtherTugChargeId != model.Id, ct))
                {
                    return ServiceResult<int>.Failure("Select recorded tickets from this COS for one provider that are not already allocated to another provider bill.");
                }
                if (!tickets.Any(t => t.CustomerOrderSlipId == model.CustomerOrderSlipId))
                {
                    return ServiceResult<int>.Failure("Keep at least one allocated ticket from this COS, or open the provider bill from another linked COS.");
                }
                int ownerId = tickets[0].Tugboat.TugboatOwnerId!.Value;
                string reference = model.ProviderReference.Trim().ToUpperInvariant();
                if (await _db.MsapOtherTugCharges.AnyAsync(c => c.TugboatOwnerId == ownerId && c.ProviderReference == reference && c.OtherTugChargeId != model.Id, ct))
                {
                    return ServiceResult<int>.Failure("This provider reference already has a charge. Open the existing charge and allocate its other tickets there.");
                }
                if (document is { Length: > 0 })
                {
                    string extension = Path.GetExtension(document.FileName).ToLowerInvariant();
                    if (!await IsSupportedDocumentAsync(document, ct))
                    {
                        return ServiceResult<int>.Failure("Upload a valid PDF, PNG or JPEG document of up to 10 MB.");
                    }
                    uploaded = $"other-tug-charge-{Guid.NewGuid():N}{extension}";
                    await _storage.UploadFileAsync(document, uploaded);
                    oldDocument = charge.DocumentName;
                    charge.DocumentName = uploaded;
                }
                var oldRef = charge.ProviderReference;
                var oldDate = charge.DocumentDate;
                var oldVat = charge.IncludesVat;
                var oldTotal = charge.Lines.Sum(l => l.Amount);
                charge.TugboatOwnerId = ownerId;
                charge.ProviderName = tickets[0].Tugboat.TugboatOwner!.TugboatOwnerName;
                charge.ProviderReference = reference;
                charge.DocumentDate = model.DocumentDate;
                charge.IncludesVat = model.IncludesVat!.Value;
                charge.Remarks = model.Remarks?.Trim();
                charge.PreparedByUserId = userId;
                charge.PreparedByName = actor.Name.Trim();
                charge.UpdatedAt = DateTimeHelper.GetCurrentPhilippineTime();
                charge.Revision = model.Id.HasValue ? charge.Revision + 1 : 1;
                var removed = charge.Lines.Where(l => !ids.Contains(l.DispatchTicketId)).ToList();
                _db.MsapOtherTugChargeLines.RemoveRange(removed);
                foreach (var line in removed)
                {
                    charge.Lines.Remove(line);
                }
                foreach (var allocation in model.Allocations)
                {
                    var line = charge.Lines.SingleOrDefault(l => l.DispatchTicketId == allocation.DispatchTicketId);
                    if (line == null)
                    {
                        charge.Lines.Add(new OtherTugChargeLine { DispatchTicketId = allocation.DispatchTicketId, Amount = allocation.Amount!.Value });
                    }
                    else
                    {
                        line.Amount = allocation.Amount!.Value;
                    }
                }
                if (!model.Id.HasValue)
                {
                    _db.MsapOtherTugCharges.Add(charge);
                }
                await _db.SaveChangesAsync(ct);
                if (model.Id.HasValue && charge.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted)
                {
                    var newTotal = charge.Lines.Sum(l => l.Amount);
                    var diffs = new List<MsapFieldChange>();
                    if (charge.PeriodStatus == MsapPeriodStatus.Final)
                    {
                        charge.PeriodStatus = MsapPeriodStatus.Adjusted;
                        diffs.Add(new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted });
                    }
                    if (!string.Equals(oldRef, reference, StringComparison.Ordinal))
                    {
                        diffs.Add(new MsapFieldChange { Field = "ProviderReference", Label = "Provider Reference", Before = oldRef, After = reference });
                    }
                    if (oldDate != charge.DocumentDate)
                    {
                        diffs.Add(new MsapFieldChange { Field = "DocumentDate", Label = "Document Date", Before = oldDate.ToString("yyyy-MM-dd"), After = charge.DocumentDate.ToString("yyyy-MM-dd") });
                    }
                    if (oldVat != charge.IncludesVat)
                    {
                        diffs.Add(new MsapFieldChange { Field = "IncludesVat", Label = "Includes VAT", Before = oldVat.ToString(), After = charge.IncludesVat.ToString() });
                    }
                    if (oldTotal != newTotal)
                    {
                        diffs.Add(new MsapFieldChange { Field = "TotalAmount", Label = "Total Amount", Before = oldTotal.ToString("N2"), After = newTotal.ToString("N2") });
                    }
                    if (diffs.Count > 0)
                    {
                        _db.MsapAdjustmentLogs.Add(new MsapAdjustmentLog
                        {
                            DocumentType = "OtherTugCharge",
                            RecordId = charge.OtherTugChargeId,
                            ReferenceNumber = reference,
                            Reason = charge.Remarks ?? "Provider charge details revised",
                            ChangesJson = System.Text.Json.JsonSerializer.Serialize(diffs),
                            EditedBy = actor.UserName ?? userId,
                            EditedDate = charge.UpdatedAt
                        });
                    }
                }
                _db.AuditTrails.Add(new AuditTrail(actor.UserName ?? userId, $"Saved provider charge {reference}. Before: {before}. After: {Snapshot(charge)}",
                    "Other Tug Charges", charge.OtherTugChargeId, reference) { UserId = userId, ActorName = actor.Name.Trim() });
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                uploaded = null;
                if (oldDocument != null)
                {
                    try { await _storage.DeleteFileAsync(oldDocument); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not remove replaced provider document for {ChargeId}", charge.OtherTugChargeId); }
                }
                return ServiceResult<int>.Success(charge.OtherTugChargeId, "Provider charges saved for reviewing. AP amounts remain unchanged until confirmation.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not save provider charges");
                return ServiceResult<int>.Failure("Unable to save provider charges. Another user may have allocated these tickets or used this reference. Reload and review.");
            }
            finally
            {
                if (uploaded != null)
                {
                    try { await _storage.DeleteFileAsync(uploaded); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not remove unused provider document"); }
                }
            }
        }

        public async Task<ServiceResult> ConfirmAsync(int id, int reviewedRevision, string userId, CancellationToken ct)
        {
            var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                return ServiceResult.Failure("An active account with a full name is required.");
            }
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var charge = await LockAsync(id, ct);
            if (charge == null)
            {
                return ServiceResult.Failure("Provider charge not found.");
            }
            if (charge.ConfirmedAt.HasValue)
            {
                return ServiceResult.Success("This provider charge is already confirmed.");
            }
            if (charge.Revision != reviewedRevision)
            {
                return ServiceResult.Failure("The provider charge changed. Review it again before confirming.");
            }
            if (charge.PeriodStatus is not (MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted))
            {
                var periodError = await GuardClosedPeriodAsync(charge.DocumentDate, ct);
                if (periodError != null)
                {
                    return ServiceResult.Failure(periodError);
                }
            }
            List<int> ids = charge.Lines.Select(l => l.DispatchTicketId).ToList();
            List<int> cosIds = await _db.MsapDispatchTickets.Where(t => ids.Contains(t.DispatchTicketId))
                .Select(t => t.CustomerOrderSlipId!.Value).Distinct().Order().ToListAsync(ct);
            foreach (int cosId in cosIds)
            {
                var schedule = await _db.MsapVesselSchedules.Where(s => s.CustomerOrderSlipId == cosId).SingleOrDefaultAsync(ct);
                if (schedule != null)
                {
                    await _db.LockRowForUpdateAsync<VesselSchedule, int>(schedule.VesselScheduleId, ct);
                    await _db.Entry(schedule).ReloadAsync(ct);
                    if (schedule.ApprovedRevision != schedule.Revision || schedule.Status == MsapConstants.VesselScheduleStatus.Tentative
                        || schedule.Status == MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        return ServiceResult.Failure("Approve each linked schedule before confirming its provider charge.");
                    }
                }
                await _db.LockRowForUpdateAsync<IBS.Models.MSAP.CustomerOrderSlip, int>(cosId, ct);
            }
            foreach (var line in charge.Lines.OrderBy(l => l.DispatchTicketId))
            {
                await _db.LockRowForUpdateAsync<DispatchTicket, int>(line.DispatchTicketId, ct);
            }
            var tickets = await EligibleTickets().Include(t => t.Billing)
                .Where(t => ids.Contains(t.DispatchTicketId) && t.Tugboat.TugboatOwnerId == charge.TugboatOwnerId).ToListAsync(ct);
            if (tickets.Count != ids.Count || ids.Count == 0)
            {
                return ServiceResult.Failure("Provider assignments or actual tickets changed. Review the charge again.");
            }
            await _db.LockRowForShareAsync<TugboatOwner, int>(charge.TugboatOwnerId, ct);
            var owner = await _db.MsapTugboatOwners.AsNoTracking().SingleAsync(o => o.TugboatOwnerId == charge.TugboatOwnerId, ct);
            var totalAmount = charge.Lines.Sum(l => l.Amount);
            if (totalAmount > 0m)
            {
                var profileError = TugboatOwnerService.ValidatePaymentProfile(owner, required: true);
                if (profileError != null)
                {
                    return ServiceResult.Failure(profileError);
                }
            }
            foreach (var ticket in tickets)
            {
                ticket.ApOtherTugs = charge.Lines.Single(l => l.DispatchTicketId == ticket.DispatchTicketId).Amount;
                ticket.ApOtherTugsIncludesVat = charge.IncludesVat;
            }
            charge.ProviderName = owner.TugboatOwnerName;
            charge.ConfirmedAt = DateTimeHelper.GetCurrentPhilippineTime();
            charge.ConfirmedByUserId = userId;
            charge.ConfirmedByName = actor.Name.Trim();
            var recognized = false;
            if (totalAmount > 0m)
            {
                try
                {
                    recognized = await RecognizeLiabilityAsync(owner, charge, tickets, actor.Name.Trim(), ct);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Could not recognize AP Other Tugs liability for provider charge {ChargeId}: {Message}", id, ex.Message);
                    return ServiceResult.Failure(ex.Message);
                }
            }
            _db.AuditTrails.Add(new AuditTrail(actor.UserName ?? userId, $"Confirmed provider charge {charge.ProviderReference}: {Snapshot(charge)}.{(recognized ? $" Recognized AP Other Tugs in GL for {charge.ProviderReference}." : "")}",
                "Other Tug Charges", id, charge.ProviderReference) { UserId = userId, ActorName = actor.Name.Trim() });
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success(recognized
                ? $"Provider charges confirmed and recognized in AP Other Tugs ({charge.ProviderReference})."
                : "Provider charges confirmed and applied to AP Other Tugs reporting.");
        }

        public async Task<ServiceResult> ReopenConfirmedChargeAsync(int id, string reason, string userId, CancellationToken ct, IFormFile? authorizationFile = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                {
                    return ServiceResult.Failure("Please provide a valid reason for reopening this charge (minimum 5 characters).");
                }

                var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
                if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
                {
                    return ServiceResult.Failure("An active account with a full name is required.");
                }

                await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                var charge = await LockAsync(id, ct);
                if (charge == null)
                {
                    return ServiceResult.Failure("Provider charge not found.");
                }

                if (!charge.ConfirmedAt.HasValue)
                {
                    return ServiceResult.Failure("This provider charge is not confirmed.");
                }

                if (charge.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted && authorizationFile is not { Length: > 0 })
                {
                    return ServiceResult.Failure("An approved authorization document (PDF, PNG, or JPEG) is required to reopen a finalized-period provider charge.");
                }

                string? uploadedAttachment = null;
                if (authorizationFile is { Length: > 0 })
                {
                    if (!await IsSupportedDocumentAsync(authorizationFile, ct))
                    {
                        return ServiceResult.Failure("Upload a valid PDF, PNG, or JPEG authorization document of up to 10 MB.");
                    }
                    string extension = Path.GetExtension(authorizationFile.FileName).ToLowerInvariant();
                    uploadedAttachment = $"adjustment-auth-{Guid.NewGuid():N}{extension}";
                    await _storage.UploadFileAsync(authorizationFile, uploadedAttachment);
                }

                // Check if an active (non-voided/non-canceled) Check Voucher payment exists in IBS AP
                await using var accounting = await _accountingContextFactory.OpenAccountingContextAsync(_db, ct);

                var activePayment = await accounting.FilprideTugboatPayments
                    .Include(p => p.Voucher)
                    .Where(p => p.OtherTugChargeId == id
                        && p.Voucher.Status != nameof(CheckVoucherPaymentStatus.Voided)
                        && p.Voucher.Status != nameof(CheckVoucherPaymentStatus.Canceled))
                    .FirstOrDefaultAsync(ct);

                if (activePayment != null)
                {
                    return ServiceResult.Failure($"Cannot reopen charge: active Check Voucher {activePayment.Voucher.CheckVoucherHeaderNo} exists in IBS AP. Void or cancel the check voucher first.");
                }

                // Reverse recognized GL entries in FilprideGeneralLedgerBooks
                var glRef = BuildGlReference(charge.ProviderReference);
                var glEntries = await accounting.FilprideGeneralLedgerBooks.IgnoreQueryFilters(["TugboatWorkspace"])
                    .Where(g => g.WorkspaceTugboatId == null && g.Reference == glRef
                        && g.CounterpartyId == charge.TugboatOwnerId)
                    .ToListAsync(ct);
                if (glEntries.Count != 0)
                {
                    accounting.FilprideGeneralLedgerBooks.RemoveRange(glEntries);
                    await accounting.SaveChangesAsync(ct);
                }

                int priorRevision = charge.Revision;
                charge.ConfirmedAt = null;
                charge.ConfirmedByUserId = null;
                charge.ConfirmedByName = null;
                charge.Revision += 1;
                charge.UpdatedAt = DateTimeHelper.GetCurrentPhilippineTime();

                // Clear AP amounts on tickets until re-confirmed
                List<int> ids = charge.Lines.Select(l => l.DispatchTicketId).ToList();
                var tickets = await _db.MsapDispatchTickets.Where(t => ids.Contains(t.DispatchTicketId)).ToListAsync(ct);
                foreach (var ticket in tickets)
                {
                    ticket.ApOtherTugs = 0m;
                    ticket.ApOtherTugsIncludesVat = false;
                }

                if (charge.PeriodStatus is MsapPeriodStatus.Final or MsapPeriodStatus.Adjusted)
                {
                    var changes = new List<MsapFieldChange>();
                    if (charge.PeriodStatus == MsapPeriodStatus.Final)
                    {
                        charge.PeriodStatus = MsapPeriodStatus.Adjusted;
                        changes.Add(new MsapFieldChange { Field = "PeriodStatus", Label = "Period Status", Before = MsapPeriodStatus.Final, After = MsapPeriodStatus.Adjusted });
                    }
                    changes.Add(new MsapFieldChange { Field = "Revision", Label = "Revision", Before = priorRevision.ToString(), After = charge.Revision.ToString() });
                    changes.Add(new MsapFieldChange { Field = "ConfirmedAt", Label = "Confirmed Date", Before = "Confirmed", After = "Reopened" });

                    var log = new MsapAdjustmentLog
                    {
                        DocumentType = "OtherTugCharge",
                        RecordId = charge.OtherTugChargeId,
                        ReferenceNumber = charge.ProviderReference,
                        Reason = reason.Trim(),
                        AttachmentUrl = uploadedAttachment,
                        ChangesJson = JsonSerializer.Serialize(changes),
                        EditedBy = actor.Name.Trim(),
                        EditedDate = DateTimeHelper.GetCurrentPhilippineTime()
                    };
                    await _db.MsapAdjustmentLogs.AddAsync(log, ct);
                }

                _db.AuditTrails.Add(new AuditTrail(actor.UserName ?? userId,
                    $"Reopened provider charge {charge.ProviderReference} for revision (rev {charge.Revision}). Reason: {reason.Trim()}",
                    "Other Tug Charges", charge.OtherTugChargeId, charge.ProviderReference)
                {
                    UserId = userId,
                    ActorName = actor.Name.Trim()
                });

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return ServiceResult.Success($"Provider charge {charge.ProviderReference} reopened for revision successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reopen provider charge {Id}", id);
                return ServiceResult.Failure($"Failed to reopen provider charge: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public static (decimal Gross, decimal Net, decimal InputVat) CalculateRecognition(TugboatOwner owner, OtherTugCharge charge)
        {
            var (gross, _) = TugboatPaymentService.Calculate(owner, charge);
            var vatable = owner.VatType == "Vatable";
            var net = vatable ? Math.Round(gross / 1.12m, 2, MidpointRounding.AwayFromZero) : gross;
            var inputVat = gross - net;
            return (gross, net, inputVat);
        }

        private async Task<bool> RecognizeLiabilityAsync(TugboatOwner owner, OtherTugCharge charge, List<DispatchTicket> tickets, string actor, CancellationToken ct)
        {
            var (accounting, work) = await _accountingContextFactory.OpenAccountingScopeAsync(_db, ct);
            await using var _ = accounting;
            var (gross, net, inputVat) = CalculateRecognition(owner, charge);
            var billingDate = tickets.Where(t => t.Billing != null).Select(t => t.Billing!.Date).DefaultIfEmpty(charge.DocumentDate).Max();
            var recognitionDate = billingDate;
            var closedPeriodAdjustment = false;
            if (await work.IsPeriodPostedAsync(Module.ReceivingReport, recognitionDate, ct))
            {
                closedPeriodAdjustment = true;
                var today = DateOnly.FromDateTime(DateTimeHelper.GetCurrentPhilippineTime());
                var minOpenDate = DateOnly.FromDateTime(await work.GetMinimumPeriodBasedOnThePostedPeriods(Module.ReceivingReport, ct));
                recognitionDate = today >= minOpenDate ? today : minOpenDate;
                if (recognitionDate > charge.DocumentDate)
                {
                    recognitionDate = charge.DocumentDate >= minOpenDate ? charge.DocumentDate : recognitionDate;
                }
            }
            var glRef = BuildGlReference(charge.ProviderReference);
            var existing = await accounting.FilprideGeneralLedgerBooks.IgnoreQueryFilters(["TugboatWorkspace"])
                .AnyAsync(g => g.WorkspaceTugboatId == null && g.Reference == glRef
                    && g.CounterpartyId == owner.TugboatOwnerId, ct);
            if (existing)
            {
                return true;
            }
            var contraAccount = await accounting.FilprideChartOfAccounts.SingleOrDefaultAsync(a => a.AccountNumber == ContraRevenueAccount && !a.IsMain && !a.IsHidden, ct)
                ?? throw new InvalidOperationException("Required posting account is unavailable: " + ContraRevenueAccount);
            var apAccount = await accounting.FilprideChartOfAccounts.SingleOrDefaultAsync(a => a.AccountNumber == TugboatPaymentService.ApAccount && !a.IsMain && !a.IsHidden, ct)
                ?? throw new InvalidOperationException("Required posting account is unavailable: " + TugboatPaymentService.ApAccount);
            var now = DateTimeHelper.GetCurrentPhilippineTime();
            var particulars = closedPeriodAdjustment
                ? $"ADJUSTMENT ON SET UP A/P OTHER TUGS - {owner.TugboatOwnerName} ({charge.ProviderReference}; BILLING DATE {billingDate:MMM dd, yyyy})"
                : $"SET UP A/P OTHER TUGS - {owner.TugboatOwnerName} ({charge.ProviderReference})";

            var ledgers = new List<FilprideGeneralLedgerBook>
            {
                new()
                {
                    WorkspaceTugboatId = null,
                    Date = recognitionDate,
                    Reference = glRef,
                    Description = particulars,
                    AccountId = contraAccount.AccountId,
                    AccountNo = ContraRevenueAccount,
                    AccountTitle = contraAccount.AccountName,
                    Debit = net,
                    Credit = 0m,
                    CreatedBy = actor,
                    CreatedDate = now,
                    IsPosted = true,
                    ModuleType = nameof(ModuleType.Purchase),
                    CounterpartyType = CounterpartyType.TugboatOwner,
                    CounterpartyId = owner.TugboatOwnerId,
                    CounterpartyName = owner.TugboatOwnerName
                }
            };
            if (inputVat > 0m)
            {
                var vatAccount = await accounting.FilprideChartOfAccounts.SingleOrDefaultAsync(a => a.AccountNumber == InputVatAccount && !a.IsMain && !a.IsHidden, ct)
                    ?? throw new InvalidOperationException("Required posting account is unavailable: " + InputVatAccount);
                ledgers.Add(new FilprideGeneralLedgerBook
                {
                    WorkspaceTugboatId = null,
                    Date = recognitionDate,
                    Reference = glRef,
                    Description = particulars,
                    AccountId = vatAccount.AccountId,
                    AccountNo = InputVatAccount,
                    AccountTitle = vatAccount.AccountName,
                    Debit = inputVat,
                    Credit = 0m,
                    CreatedBy = actor,
                    CreatedDate = now,
                    IsPosted = true,
                    ModuleType = nameof(ModuleType.Purchase),
                    CounterpartyType = CounterpartyType.TugboatOwner,
                    CounterpartyId = owner.TugboatOwnerId,
                    CounterpartyName = owner.TugboatOwnerName
                });
            }
            ledgers.Add(new FilprideGeneralLedgerBook
            {
                WorkspaceTugboatId = null,
                Date = recognitionDate,
                Reference = glRef,
                Description = particulars,
                AccountId = apAccount.AccountId,
                AccountNo = TugboatPaymentService.ApAccount,
                AccountTitle = apAccount.AccountName,
                Debit = 0m,
                Credit = gross,
                CreatedBy = actor,
                CreatedDate = now,
                IsPosted = true,
                ModuleType = nameof(ModuleType.Purchase),
                CounterpartyType = CounterpartyType.TugboatOwner,
                CounterpartyId = owner.TugboatOwnerId,
                CounterpartyName = owner.TugboatOwnerName
            });
            await accounting.FilprideGeneralLedgerBooks.AddRangeAsync(ledgers, ct);
            await accounting.FilprideAuditTrails.AddAsync(new FilprideAuditTrail(actor,
                $"Posted AP Other Tugs direct GL entries for {charge.ProviderReference}", "Other Tug Charges"), ct);
            await accounting.SaveChangesAsync(ct);
            return true;
        }

        private async Task<string?> GuardClosedPeriodAsync(DateOnly date, CancellationToken ct)
        {
            if (!MsapConstants.EnforceClosedPeriods)
            {
                return null;
            }

            var isClosed = await _db.MsapPostedPeriods
                .AsNoTracking()
                .AnyAsync(p => p.Year == date.Year && p.Month == date.Month && p.IsClosed, ct);
            return isClosed ? $"Cannot modify: {date:MMMM yyyy} is closed." : null;
        }

        private async Task<OtherTugCharge?> LockAsync(int id, CancellationToken ct)
        {
            var charge = await _db.FindForUpdateAsync<OtherTugCharge, int>(id, reload: true, ct);
            if (charge != null)
            {
                await _db.Entry(charge).Collection(c => c.Lines).LoadAsync(ct);
                if (charge.Lines.Count == 0 || await _db.MsapOtherTugChargeLines.AnyAsync(l => l.OtherTugChargeId == id
                    && l.Ticket.Customer.Company != MsapConstants.Company_MMSI, ct))
                {
                    return null;
                }
            }
            return charge;
        }

        private static string BuildGlReference(string providerReference)
        {
            return providerReference.Length <= 20
                ? providerReference
                : providerReference[..20];
        }

        private static string Snapshot(OtherTugCharge charge)
        {
            return JsonSerializer.Serialize(new { charge.ProviderReference, charge.DocumentDate, charge.IncludesVat, charge.DocumentName, charge.Remarks,
                Lines = charge.Lines.OrderBy(l => l.DispatchTicketId).Select(l => new { l.DispatchTicketId, l.Amount }) });
        }
    }
}
