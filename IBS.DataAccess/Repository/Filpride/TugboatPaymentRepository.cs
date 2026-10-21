using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IBS.DataAccess.Repository.Filpride
{
    public class TugboatPaymentRepository
    {
        private readonly ApplicationDbContext _db;

        public TugboatPaymentRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        private async Task<MsapDbContext> SourceAsync(CancellationToken ct)
        {
            var source = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(_db.Database.GetDbConnection()).Options);
            if (_db.Database.CurrentTransaction != null)
            {
                await source.Database.UseTransactionAsync(_db.Database.CurrentTransaction.GetDbTransaction(), ct);
            }
            return source;
        }

        public async Task<List<TugboatOwner>> OwnersAsync(CancellationToken ct)
        {
            await using var source = await SourceAsync(ct);
            return await source.MsapTugboatOwners.AsNoTracking().OrderBy(o => o.TugboatOwnerName).ToListAsync(ct);
        }

        public async Task<List<OtherTugCharge>> ChargesAsync(int ownerId, CancellationToken ct)
        {
            List<int> reserved = await ReservedAllocationIdsAsync(ct);
            await using var source = await SourceAsync(ct);
            return await source.MsapOtherTugCharges.AsNoTracking()
                .Include(c => c.Lines).ThenInclude(l => l.Ticket).ThenInclude(t => t.Tugboat)
                .Include(c => c.Lines).ThenInclude(l => l.Ticket).ThenInclude(t => t.CustomerOrderSlip)
                .Where(c => c.TugboatOwnerId == ownerId
                    && c.ConfirmedAt != null && c.Lines.Any(l => !reserved.Contains(l.OtherTugChargeLineId)) && c.Lines.All(l => l.Ticket.Customer.Company == "MMSI"))
                .OrderBy(c => c.DocumentDate).ThenBy(c => c.OtherTugChargeId).ToListAsync(ct);
        }

        public async Task<List<int>> ReservedAllocationIdsAsync(CancellationToken ct)
        {
            return await _db.FilprideTugboatPaymentAllocations.Where(a => a.ReleasedAt == null)
                .Select(a => a.OtherTugChargeLineId).ToListAsync(ct);
        }

        public async Task<List<int>> ChargeIdsAsync(int[] allocationIds, CancellationToken ct)
        {
            await using var source = await SourceAsync(ct);
            return await source.MsapOtherTugChargeLines.Where(l => allocationIds.Contains(l.OtherTugChargeLineId))
                .Select(l => l.OtherTugChargeId).Distinct().OrderBy(id => id).ToListAsync(ct);
        }

        public async Task<(TugboatOwner Owner, OtherTugCharge Charge)> SourceAsync(int chargeId, bool forUpdate, CancellationToken ct)
        {
            await using var source = await SourceAsync(ct);
            if (forUpdate)
            {
                await source.MsapOtherTugCharges.FromSqlInterpolated($"SELECT * FROM msap.mmsi_other_tug_charges WHERE other_tug_charge_id={chargeId} FOR UPDATE").LoadAsync(ct);
                await source.MsapOtherTugChargeLines.FromSqlInterpolated($"SELECT * FROM msap.mmsi_other_tug_charge_lines WHERE other_tug_charge_id={chargeId} FOR UPDATE").LoadAsync(ct);
            }
            var charge = await source.MsapOtherTugCharges.AsNoTracking().Include(c => c.Lines).ThenInclude(l => l.Ticket).ThenInclude(t => t.Tugboat)
                .Include(c => c.Lines).ThenInclude(l => l.Ticket).ThenInclude(t => t.CustomerOrderSlip)
                .SingleOrDefaultAsync(c => c.OtherTugChargeId == chargeId, ct)
                ?? throw new InvalidOperationException("Provider bill not found.");
            if (charge.ConfirmedAt == null || charge.Lines.Count == 0)
            {
                throw new InvalidOperationException("Confirm the complete provider bill before preparing payment.");
            }
            List<int> ids = charge.Lines.Select(l => l.DispatchTicketId).ToList();
            int eligible = await DispatchTicketRepository.QueryOtherTugChargeTickets(source)
                .CountAsync(t => ids.Contains(t.DispatchTicketId) && t.Tugboat.TugboatOwnerId == charge.TugboatOwnerId
                    && source.MsapVesselSchedules.Any(s => s.CustomerOrderSlipId == t.CustomerOrderSlipId
                        && s.ApprovedRevision == s.Revision && s.Status != "Tentative" && s.Status != "Cancelled"), ct);
            if (eligible != ids.Count)
            {
                throw new InvalidOperationException("The provider bill contains unavailable services or unapproved schedules.");
            }
            if (forUpdate)
            {
                await source.MsapTugboatOwners.FromSqlInterpolated($"SELECT * FROM msap.mmsi_tugboat_owners WHERE tugboat_owner_id={charge.TugboatOwnerId} FOR SHARE").LoadAsync(ct);
            }
            var owner = await source.MsapTugboatOwners.AsNoTracking().SingleAsync(o => o.TugboatOwnerId == charge.TugboatOwnerId, ct);
            return (owner, charge);
        }
    }
}
