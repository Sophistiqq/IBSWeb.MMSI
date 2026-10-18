using System.Text.Json;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP;
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
        private readonly MsapDbContext _db;
        private readonly ICloudStorageService _storage;
        private readonly ILogger<OtherTugChargeService> _logger;

        public OtherTugChargeService(MsapDbContext db, ICloudStorageService storage, ILogger<OtherTugChargeService> logger)
        {
            _db = db;
            _storage = storage;
            _logger = logger;
        }

        private IQueryable<DispatchTicket> EligibleTickets()
        {
            DateTime now = DateTimeHelper.GetCurrentPhilippineTime();
            DateOnly today = DateOnly.FromDateTime(now);
            TimeOnly currentTime = TimeOnly.FromDateTime(now);
            return _db.MsapDispatchTickets.Where(t => t.Customer.Company == MsapConstants.Company_MMSI
                && !t.Tugboat.IsCompanyOwned && t.Tugboat.TugboatOwnerId != null && t.VesselScheduleServiceEntryId != null
                && t.ExpectedServiceEntry!.CancelledAt == null
                && t.CustomerOrderSlipId != null && (t.CustomerOrderSlip!.Status == MsapConstants.CustomerOrderSlipStatus.Open
                    || t.CustomerOrderSlip.Status == MsapConstants.CustomerOrderSlipStatus.Closed)
                && t.Status != MsapConstants.DispatchTicketStatus.Deleted && t.DispatchNumber != "" && t.DateLeft != null && t.DateArrived != null
                && t.TimeLeft != null && t.TimeArrived != null
                && (t.DateArrived < today || t.DateArrived == today && t.TimeArrived <= currentTime)
                && (t.DateArrived > t.DateLeft || t.DateArrived == t.DateLeft && t.TimeArrived > t.TimeLeft));
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
                IncludesVat = charge?.IncludesVat, Remarks = charge?.Remarks, Charge = charge
            };
            var query = EligibleTickets().AsNoTracking().Where(t => t.Tugboat.TugboatOwnerId == ownerId
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
                string before = Snapshot(charge);
                List<int> ids = model.Allocations.Select(l => l.DispatchTicketId).Order().ToList();
                foreach (int id in ids)
                {
                    await _db.MsapDispatchTickets.FromSqlInterpolated($"SELECT * FROM msap.mmsi_dispatch_tickets WHERE \"RECID\" = {id} FOR UPDATE").LoadAsync(ct);
                }
                var tickets = await EligibleTickets().Include(t => t.Tugboat).ThenInclude(t => t.TugboatOwner)
                    .Where(t => ids.Contains(t.DispatchTicketId)).ToListAsync(ct);
                if (tickets.Count != ids.Count
                    || tickets.Select(t => t.Tugboat.TugboatOwnerId).Distinct().Count() != 1
                    || model.Id.HasValue && tickets[0].Tugboat.TugboatOwnerId != charge.TugboatOwnerId
                    || await _db.MsapOtherTugChargeLines.AnyAsync(l => ids.Contains(l.DispatchTicketId) && l.OtherTugChargeId != model.Id, ct))
                {
                    return ServiceResult<int>.Failure("Select recorded tickets for one provider that are not already allocated to another provider bill.");
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
            List<int> ids = charge.Lines.Select(l => l.DispatchTicketId).ToList();
            List<int> cosIds = await _db.MsapDispatchTickets.Where(t => ids.Contains(t.DispatchTicketId))
                .Select(t => t.CustomerOrderSlipId!.Value).Distinct().Order().ToListAsync(ct);
            foreach (int cosId in cosIds)
            {
                var schedule = await _db.MsapVesselSchedules.Where(s => s.CustomerOrderSlipId == cosId).SingleOrDefaultAsync(ct);
                if (schedule != null)
                {
                    await _db.MsapVesselSchedules.FromSqlInterpolated($"SELECT * FROM msap.mmsi_vessel_schedules WHERE vessel_schedule_id = {schedule.VesselScheduleId} FOR UPDATE").LoadAsync(ct);
                    await _db.Entry(schedule).ReloadAsync(ct);
                    if (schedule.ApprovedRevision != schedule.Revision || schedule.Status == MsapConstants.VesselScheduleStatus.Tentative
                        || schedule.Status == MsapConstants.VesselScheduleStatus.Cancelled)
                    {
                        return ServiceResult.Failure("Approve each linked schedule before confirming its provider charge.");
                    }
                }
                await _db.MsapCustomerOrderSlips.FromSqlInterpolated($"SELECT * FROM msap.mmsi_customer_order_slips WHERE customer_order_slip_id = {cosId} FOR UPDATE").LoadAsync(ct);
            }
            foreach (var line in charge.Lines.OrderBy(l => l.DispatchTicketId))
            {
                await _db.MsapDispatchTickets.FromSqlInterpolated($"SELECT * FROM msap.mmsi_dispatch_tickets WHERE \"RECID\" = {line.DispatchTicketId} FOR UPDATE").LoadAsync(ct);
            }
            var tickets = await EligibleTickets().Where(t => ids.Contains(t.DispatchTicketId) && t.Tugboat.TugboatOwnerId == charge.TugboatOwnerId).ToListAsync(ct);
            if (tickets.Count != ids.Count || ids.Count == 0)
            {
                return ServiceResult.Failure("Provider assignments or actual tickets changed. Review the charge again.");
            }
            foreach (var ticket in tickets)
            {
                ticket.ApOtherTugs = charge.Lines.Single(l => l.DispatchTicketId == ticket.DispatchTicketId).Amount;
                ticket.ApOtherTugsIncludesVat = charge.IncludesVat;
            }
            charge.ConfirmedAt = DateTimeHelper.GetCurrentPhilippineTime();
            charge.ConfirmedByUserId = userId;
            charge.ConfirmedByName = actor.Name.Trim();
            _db.AuditTrails.Add(new AuditTrail(actor.UserName ?? userId, $"Confirmed provider charge {charge.ProviderReference}: {Snapshot(charge)}. Payment remains in IBS.",
                "Other Tug Charges", id, charge.ProviderReference) { UserId = userId, ActorName = actor.Name.Trim() });
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ServiceResult.Success("Provider charges confirmed and applied to AP Other Tugs reporting. Payment integration remains pending in IBS.");
        }

        private async Task<OtherTugCharge?> LockAsync(int id, CancellationToken ct)
        {
            var charge = await _db.MsapOtherTugCharges.FromSqlInterpolated($"SELECT * FROM msap.mmsi_other_tug_charges WHERE other_tug_charge_id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (charge != null)
            {
                await _db.Entry(charge).ReloadAsync(ct);
                await _db.Entry(charge).Collection(c => c.Lines).LoadAsync(ct);
                if (charge.Lines.Count == 0 || await _db.MsapOtherTugChargeLines.AnyAsync(l => l.OtherTugChargeId == id
                    && l.Ticket.Customer.Company != MsapConstants.Company_MMSI, ct))
                {
                    return null;
                }
            }
            return charge;
        }

        private static string Snapshot(OtherTugCharge charge)
        {
            return JsonSerializer.Serialize(new { charge.ProviderReference, charge.DocumentDate, charge.IncludesVat, charge.DocumentName, charge.Remarks,
                Lines = charge.Lines.OrderBy(l => l.DispatchTicketId).Select(l => new { l.DispatchTicketId, l.Amount }) });
        }
    }
}
