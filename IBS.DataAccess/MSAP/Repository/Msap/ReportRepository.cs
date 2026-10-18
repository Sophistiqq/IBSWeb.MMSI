using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class ReportRepository(MsapDbContext db): IReportRepository
    {
        public async Task<List<IBS.Models.MSAP.Books.SalesBook>> GetSalesBookAsync(DateOnly from, DateOnly to, CancellationToken ct)
        {
            return await db.MsapSalesBooks.AsNoTracking().Where(b => b.Company == MsapConstants.Company_MMSI
                && b.TransactionDate >= from && b.TransactionDate <= to && b.VoidedAt == null)
                .OrderBy(b => b.TransactionDate).ThenBy(b => b.SerialNo).ToListAsync(ct);
        }

        public async Task<List<DispatchTicket>> GetDispatchReportData(DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default, bool filterByBillingDate = false)
        {
            if (dateFrom > dateTo)
            {
                throw new ArgumentException("Date From must not be later than Date To.");
            }

            var query = db.MsapDispatchTickets.Where(dt =>
                dt.Status == MsapConstants.DispatchTicketStatus.ForBilling ||
                dt.Status == MsapConstants.DispatchTicketStatus.Billed);

            if (filterByBillingDate)
            {
                query = query.Where(dt =>
                    (dt.BillingId != null && db.MsapBillings.Any(b => b.MsapBillingId == dt.BillingId && b.Date >= dateFrom && b.Date <= dateTo))
                    || (dt.BillingId == null && dt.Date >= dateFrom && dt.Date <= dateTo && dt.CustomerId != 0));
            }
            else
            {
                query = query.Where(dt => dt.Date >= dateFrom && dt.Date <= dateTo);
            }

            var tickets = filterByBillingDate
                ? await query
                    .Include(dt => dt.ExpectedServiceEntry).ThenInclude(entry => entry!.VesselSchedule)
                    .Include(dt => dt.OtherTugChargeLine).ThenInclude(line => line!.Charge)
                    .Include(dt => dt.Customer)
                    .Include(dt => dt.Vessel)
                    .Include(dt => dt.Tugboat)
                    .ThenInclude(t => t.TugboatOwner)
                    .Include(dt => dt.TugMaster)
                    .Include(dt => dt.Port)
                    .Include(dt => dt.Terminal)
                    .ThenInclude(t => t.Port)
                    .Include(dt => dt.Service)
                    .OrderBy(dt => dt.Billing == null ? dt.Date : dt.Billing.Date)
                    .ThenBy(dt => dt.DispatchNumber)
                    .ToListAsync(cancellationToken)
                : await query
                    .Include(dt => dt.ExpectedServiceEntry).ThenInclude(entry => entry!.VesselSchedule)
                    .Include(dt => dt.OtherTugChargeLine).ThenInclude(line => line!.Charge)
                    .Include(dt => dt.Customer)
                    .Include(dt => dt.Vessel)
                    .Include(dt => dt.Tugboat)
                    .ThenInclude(t => t.TugboatOwner)
                    .Include(dt => dt.TugMaster)
                    .Include(dt => dt.Port)
                    .Include(dt => dt.Terminal)
                    .ThenInclude(t => t.Port)
                    .Include(dt => dt.Service)
                    .OrderBy(dt => dt.Date)
                    .ThenBy(dt => dt.DispatchNumber)
                    .ToListAsync(cancellationToken);

            var billingIds = tickets.Where(t => t.BillingId.HasValue).Select(t => t.BillingId!.Value).Distinct().ToList();
            if (billingIds.Count != 0)
            {
                var billings = await db.MsapBillings
                    .Where(b => billingIds.Contains(b.MsapBillingId))
                    .Include(b => b.Customer)
                    .Include(b => b.Principal)
                    .ToListAsync(cancellationToken);

                var collectionIds = billings.Where(b => b.CollectionId.HasValue).Select(b => b.CollectionId!.Value).Distinct().ToList();
                var collections = collectionIds.Count != 0
                    ? await db.MsapCollections.Where(c => collectionIds.Contains(c.MsapCollectionId)).ToListAsync(cancellationToken)
                    : [];

                foreach (var ticket in tickets.Where(t => t.BillingId.HasValue))
                {
                    ticket.Billing = billings.FirstOrDefault(b => b.MsapBillingId == ticket.BillingId);
                    if (ticket.Billing?.CollectionId != null)
                    {
                        ticket.Billing.Collection = collections.FirstOrDefault(c => c.MsapCollectionId == ticket.Billing.CollectionId);
                    }
                }
            }

            return tickets;
        }
    }
}
