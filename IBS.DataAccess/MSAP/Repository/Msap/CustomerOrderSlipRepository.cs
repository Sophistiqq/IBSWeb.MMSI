using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class CustomerOrderSlipRepository(MsapDbContext db): Repository<CustomerOrderSlip>(db), ICustomerOrderSlipRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<IEnumerable<CustomerOrderSlip>> GetAllCustomerOrderSlipsWithDetailsAsync(CancellationToken cancellationToken)
        {
            return await _db.MsapCustomerOrderSlips.AsNoTracking()
                .Include(j => j.Customer)
                .Include(j => j.Vessel)
                .Include(j => j.Port)
                .Include(j => j.Terminal)
                .Include(j => j.DispatchTickets.Where(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted))
                .OrderByDescending(j => j.CustomerOrderSlipNumber)
                .ToListAsync(cancellationToken);
        }

        public async Task<IEnumerable<CustomerOrderSlip>> GetCustomerOrderSlipsWithDetailsAsync(DateTime start, DateTime end, CancellationToken cancellationToken)
        {
            return await _db.MsapCustomerOrderSlips.AsNoTracking()
                .Include(j => j.Customer)
                .Include(j => j.Vessel)
                .Include(j => j.Port)
                .Include(j => j.Terminal)
                .Include(j => j.DispatchTickets.Where(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted))
                .Where(j => j.PlannedStartTime <= end && j.PlannedEndTime >= start)
                .ToListAsync(cancellationToken);
        }

        public async Task<CustomerOrderSlip?> GetCustomerOrderSlipWithDetailsAsync(int id, CancellationToken cancellationToken)
        {
            return await _db.MsapCustomerOrderSlips
                .Include(j => j.Customer)
                .Include(j => j.Vessel)
                .Include(j => j.Port)
                .Include(j => j.Terminal)
                .Include(j => j.DispatchTickets)
                    .ThenInclude(dt => dt.Service)
                .Include(j => j.DispatchTickets)
                    .ThenInclude(dt => dt.Terminal)
                    .ThenInclude(t => t.Port)
                .Include(j => j.DispatchTickets)
                    .ThenInclude(dt => dt.Tugboat)
                    .ThenInclude(t => t.TugboatOwner)
                .Include(j => j.DispatchTickets)
                    .ThenInclude(dt => dt.TugMaster)
                .FirstOrDefaultAsync(j => j.CustomerOrderSlipId == id, cancellationToken);
        }

        public async Task<CustomerOrderSlip?> GetForUpdateAsync(int id, CancellationToken ct = default)
        {
            var job = await _db.MsapCustomerOrderSlips
                .FromSqlInterpolated($"SELECT * FROM msap.mmsi_customer_order_slips WHERE customer_order_slip_id = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (job != null)
            {
                await _db.Entry(job).ReloadAsync(ct);
            }
            return job;
        }

        public async Task<string> GenerateCustomerOrderSlipNumber(CancellationToken cancellationToken)
        {
            // Direct creation and schedule confirmation share the same number sequence.
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20771001)", cancellationToken);
            var year = DateTime.Now.Year;
            var lastRecord = await _db.MsapCustomerOrderSlips
                .Where(j => j.CustomerOrderSlipNumber.StartsWith($"COS-{year}"))
                .OrderByDescending(j => j.CustomerOrderSlipNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastRecord == null)
            {
                return $"COS-{year}-0001";
            }

            var parts = lastRecord.CustomerOrderSlipNumber.Split('-');
            if (parts.Length >= 3 && int.TryParse(parts[2], out int lastNumber))
            {
                return $"COS-{year}-{(lastNumber + 1):D4}";
            }

            return $"COS-{year}-0001";
        }
        public async Task<List<CustomerOrderSlip>> SearchBillableCustomerOrderSlipsAsync(string term, int customerId, int limit, CancellationToken cancellationToken, int? currentCustomerOrderSlipId = null)
        {
            var query = dbSet.AsNoTracking()
                .Where(j => j.CustomerId == customerId &&
                            j.DispatchTickets.Any(dt => dt.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling
                                && (dt.BillingId == null || (currentCustomerOrderSlipId.HasValue && j.CustomerOrderSlipId == currentCustomerOrderSlipId.Value && dt.Billing!.Status == IBS.Utility.MSAP.Constants.MsapConstants.BillingStatus.ForPosting))) &&
                            j.DispatchTickets.All(dt => dt.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling ||
                                                        dt.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.Billed ||
                                                        dt.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.Deleted));

            if (!string.IsNullOrWhiteSpace(term))
            {
                var s = term.ToLower();
                query = query.Where(j => j.CustomerOrderSlipNumber.ToLower().Contains(s));
            }

            return await query
                .OrderByDescending(j => j.Date)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task<(IEnumerable<CustomerOrderSlip> Data, int RecordsFiltered, int TotalRecords)> GetPagedCustomerOrderSlipsAsync(DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            IQueryable<CustomerOrderSlip> query = dbSet
                .Include(j => j.Customer)
                .Include(j => j.Vessel)
                .Include(j => j.Port)
                .Include(j => j.Terminal)
                .Include(j => j.DispatchTickets.Where(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted));

            var globalSearch = parameters?.Search?.Value;
            if (!string.IsNullOrEmpty(globalSearch))
            {
                var s = globalSearch.ToLower();
                query = query.Where(dt =>
                    dt.CustomerOrderSlipNumber.ToLower().Contains(s)
                    || dt.Customer.CustomerName.ToLower().Contains(s)
                    || dt.Status.ToLower().Contains(s));
            }

            // Column-specific search
            if (parameters?.Columns != null)
            {
                foreach (var column in parameters.Columns)
                {
                    if (column?.Search?.Value is { Length: > 0 } searchValue)
                    {
                        if (column.Data == "status")
                        {
                            query = query.Where(b => b.Status.ToLower() == searchValue);
                        }
                        else if (column.Data == "date" || column.Data == "Date")
                        {
                            if (DateOnly.TryParse(searchValue, out var parsedDate))
                            {
                                query = query.Where(b => b.Date == parsedDate);
                            }
                        }
                    }
                }
            }

            var totalRecords = await dbSet.CountAsync(cancellationToken);
            var recordsFiltered = await query.CountAsync(cancellationToken);

            var order = parameters?.Order?.FirstOrDefault();
            if (order != null
                && parameters?.Columns != null
                && order.Column >= 0
                && order.Column < parameters.Columns.Count)
            {
                var col = parameters.Columns[order.Column]?.Data?.ToLowerInvariant();
                var isDescending = string.Equals(order.Dir, "desc", StringComparison.OrdinalIgnoreCase);

                query = col switch
                {
                    "date" => isDescending ? query.OrderByDescending(j => j.Date) : query.OrderBy(j => j.Date),
                    "customerorderslipnumber" => isDescending ? query.OrderByDescending(j => j.CustomerOrderSlipNumber) : query.OrderBy(j => j.CustomerOrderSlipNumber),
                    "customer.customername" => isDescending ? query.OrderByDescending(j => j.Customer.CustomerName) : query.OrderBy(j => j.Customer.CustomerName),
                    "vessel.vesselname" => isDescending ? query.OrderByDescending(j => j.Vessel.VesselName) : query.OrderBy(j => j.Vessel.VesselName),
                    "cosnumber" => isDescending ? query.OrderByDescending(j => j.COSNumber) : query.OrderBy(j => j.COSNumber),
                    "voyagenumber" => isDescending ? query.OrderByDescending(j => j.VoyageNumber) : query.OrderBy(j => j.VoyageNumber),
                    "dispatchtickets" => isDescending
                        ? query.OrderByDescending(j => j.DispatchTickets.Count(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted))
                        : query.OrderBy(j => j.DispatchTickets.Count(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted)),
                    "status" => isDescending ? query.OrderByDescending(j => j.Status) : query.OrderBy(j => j.Status),
                    _ => query.OrderByDescending(j => j.Date)
                };
            }
            else
            {
                query = query.OrderByDescending(j => j.Date);
            }

            var start = Math.Max(0, parameters?.Start ?? 0);
            var length = parameters?.Length >= 0 ? parameters.Length : 10;
            var data = await query
                .Skip(start)
                .Take(length)
                .ToListAsync(cancellationToken);

            return (data, recordsFiltered, totalRecords);
        }
    }
}
