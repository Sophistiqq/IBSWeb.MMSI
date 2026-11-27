using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.Repository.Filpride
{
    public class MsapCommissionPaymentRepository
    {
        private readonly ApplicationDbContext _db;
        private readonly IAccountingContextFactory _accountingContextFactory;

        public MsapCommissionPaymentRepository(
            ApplicationDbContext db,
            IAccountingContextFactory? accountingContextFactory = null)
        {
            _db = db;
            _accountingContextFactory = accountingContextFactory ?? AccountingContextFactory.Default;
        }

        private Task<MsapDbContext> SourceAsync(CancellationToken ct)
        {
            return _accountingContextFactory.OpenMsapContextAsync(_db, ct);
        }

        public async Task<List<MsapRepresentative>> RepresentativesAsync(CancellationToken ct)
        {
            await using var source = await SourceAsync(ct);
            return await source.MsapRepresentatives.AsNoTracking()
                .OrderBy(r => r.RepresentativeName)
                .ThenBy(r => r.Category)
                .ToListAsync(ct);
        }

        public async Task<MsapRepresentative?> RepresentativeAsync(int representativeId, CancellationToken ct)
        {
            await using var source = await SourceAsync(ct);
            return await source.MsapRepresentatives.AsNoTracking()
                .SingleOrDefaultAsync(r => r.RepresentativeId == representativeId, ct);
        }

        public async Task<List<(int BillingId, string Category)>> ReservedItemsAsync(
            int? excludeCvId,
            IEnumerable<int> billingIds,
            CancellationToken ct)
        {
            var ids = billingIds.Distinct().ToArray();
            if (ids.Length == 0)
            {
                return [];
            }

            var reserved = await _db.FilprideMsapCommissionPayments
                .AsNoTracking()
                .Where(p => p.ReleasedAt == null
                    && ids.Contains(p.MsapBillingId)
                    && (!excludeCvId.HasValue || p.CheckVoucherHeaderId != excludeCvId.Value))
                .Select(p => new { p.MsapBillingId, p.Category })
                .ToListAsync(ct);
            return reserved.Select(x => (x.MsapBillingId, x.Category)).ToList();
        }

        public async Task<List<Billing>> ApprovedBillingsForRepresentativeAsync(int representativeId, CancellationToken ct)
        {
            await using var source = await SourceAsync(ct);
            return await source.MsapBillings.AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.CustomerOrderSlip)
                .Where(b => (b.Status == MsapConstants.BillingStatus.ForCollection || b.Status == MsapConstants.BillingStatus.Collected)
                    && b.ApprovedRevision == b.Revision
                    && b.ApprovedAt != null
                    && ((b.AgencyIncentiveRepresentativeId == representativeId && b.AgencyIncentiveAmount > 0m)
                        || (b.AgentCommissionRepresentativeId == representativeId && b.AgentCommissionAmount > 0m)))
                .OrderBy(b => b.Date)
                .ThenBy(b => b.MsapBillingNumber)
                .ToListAsync(ct);
        }

        public async Task<(MsapRepresentative Representative, Dictionary<int, Billing> Billings)> SourceForUpdateAsync(
            int representativeId,
            int[] billingIds,
            CancellationToken ct)
        {
            await using var source = await SourceAsync(ct);
            foreach (var billingId in billingIds.Distinct().OrderBy(id => id))
            {
                await source.LockRowForUpdateAsync<Billing, int>(billingId, ct);
            }

            await source.LockRowForShareAsync<MsapRepresentative, int>(representativeId, ct);

            var representative = await source.MsapRepresentatives.AsNoTracking()
                .SingleOrDefaultAsync(r => r.RepresentativeId == representativeId, ct)
                ?? throw new InvalidOperationException("Selected MSAP representative was not found.");

            var distinctIds = billingIds.Distinct().ToArray();
            var billings = await source.MsapBillings.AsNoTracking()
                .Include(b => b.CustomerOrderSlip)
                .Where(b => distinctIds.Contains(b.MsapBillingId))
                .ToDictionaryAsync(b => b.MsapBillingId, ct);

            if (billings.Count != distinctIds.Length)
            {
                throw new InvalidOperationException("One or more selected MSAP billings no longer exist.");
            }

            foreach (var billing in billings.Values)
            {
                if ((billing.Status != MsapConstants.BillingStatus.ForCollection && billing.Status != MsapConstants.BillingStatus.Collected)
                    || billing.ApprovedRevision != billing.Revision
                    || billing.ApprovedAt == null)
                {
                    throw new InvalidOperationException($"Billing #{billing.MsapBillingNumber} is not currently approved for settlement.");
                }
            }

            return (representative, billings);
        }

        public async Task<Dictionary<int, string?>> GetCosNumbersByBillingIdAsync(IEnumerable<int> billingIds, CancellationToken ct)
        {
            var ids = billingIds.Distinct().ToArray();
            if (ids.Length == 0)
            {
                return [];
            }

            await using var source = await SourceAsync(ct);
            return await source.MsapBillings.AsNoTracking()
                .Where(b => ids.Contains(b.MsapBillingId))
                .ToDictionaryAsync(b => b.MsapBillingId, b => b.COSNumber, ct);
        }
    }
}
