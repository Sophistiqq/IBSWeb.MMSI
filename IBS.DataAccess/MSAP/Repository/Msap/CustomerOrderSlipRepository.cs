using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
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

        public Task<CustomerOrderSlip?> GetForUpdateAsync(int id, CancellationToken ct = default)
        {
            return _db.FindForUpdateAsync<CustomerOrderSlip, int>(id, ct: ct);
        }

        public async Task<string> GenerateCustomerOrderSlipNumber(CancellationToken cancellationToken)
        {
            // Direct creation and schedule confirmation share the same number sequence.
            await _db.AcquireAdvisoryXactLockAsync(MsapLockExtensions.CustomerOrderSlipNumberSeries, cancellationToken);
            var year = DateTimeHelper.GetCurrentPhilippineTime().Year;
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
                    || dt.Vessel.VesselName.ToLower().Contains(s)
                    || (dt.VoyageNumber != null && dt.VoyageNumber.ToLower().Contains(s))
                    || (dt.COSNumber != null && dt.COSNumber.ToLower().Contains(s))
                    || dt.Port.PortName.ToLower().Contains(s)
                    || dt.Terminal.TerminalName.ToLower().Contains(s)
                    || dt.Status.ToLower().Contains(s)
                    || dt.DispatchTickets.Any(t => t.Status != MsapConstants.DispatchTicketStatus.Deleted
                        && t.DispatchNumber.ToLower().Contains(s)));
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
                            var normalizedStatus = searchValue.ToLower();
                            query = query.Where(b => b.Status.ToLower() == normalizedStatus);
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

            if (data.Count > 0)
            {
                var cosIds = data.Select(j => j.CustomerOrderSlipId).ToList();

                var schedules = await _db.MsapVesselSchedules.AsNoTracking()
                    .Where(s => s.CustomerOrderSlipId != null && cosIds.Contains(s.CustomerOrderSlipId.Value))
                    .Select(s => new
                    {
                        CosId = s.CustomerOrderSlipId!.Value,
                        s.Status,
                        s.Revision,
                        s.ApprovedRevision,
                        ExpectedCount = s.ServiceEntries.Count(e => e.CancelledAt == null)
                    })
                    .ToListAsync(cancellationToken);
                var scheduleMap = schedules
                    .GroupBy(s => s.CosId)
                    .ToDictionary(g => g.Key, g => g.First());

                var billings = await _db.MsapBillings.AsNoTracking()
                    .Where(b => b.CustomerOrderSlipId != null && cosIds.Contains(b.CustomerOrderSlipId.Value))
                    .OrderBy(b => b.MsapBillingId)
                    .Select(b => new
                    {
                        CosId = b.CustomerOrderSlipId!.Value,
                        b.MsapBillingId,
                        b.MsapBillingNumber,
                        b.Status
                    })
                    .ToListAsync(cancellationToken);
                var billingMap = billings
                    .GroupBy(b => b.CosId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var cos in data)
                {
                    scheduleMap.TryGetValue(cos.CustomerOrderSlipId, out var sched);
                    billingMap.TryGetValue(cos.CustomerOrderSlipId, out var bill);

                    if (sched != null)
                    {
                        cos.ExpectedTicketCount = sched.ExpectedCount;
                    }

                    if (bill != null)
                    {
                        cos.LinkedBillingId = bill.MsapBillingId;
                        cos.LinkedBillingNumber = bill.MsapBillingNumber;
                    }

                    if (cos.Status == MsapConstants.CustomerOrderSlipStatus.Open)
                    {
                        var activeTickets = cos.DispatchTickets;
                        if (sched != null && (sched.Status == MsapConstants.VesselScheduleStatus.Tentative || sched.ApprovedRevision != sched.Revision))
                        {
                            cos.WorkflowStage = "Approval pending";
                        }
                        else if (bill != null && (bill.Status == MsapConstants.BillingStatus.ForPosting || bill.Status == MsapConstants.BillingStatus.Reviewed))
                        {
                            cos.WorkflowStage = "Under billing";
                        }
                        else if (activeTickets.Any(t => t.Status == MsapConstants.DispatchTicketStatus.AwaitingRate))
                        {
                            cos.WorkflowStage = "Awaiting rate";
                        }
                        else if (sched != null && sched.ExpectedCount > 0 && activeTickets.Count < sched.ExpectedCount)
                        {
                            cos.WorkflowStage = "Missing tickets";
                        }
                        else if (activeTickets.Any(t => t.Status == MsapConstants.DispatchTicketStatus.ForBilling))
                        {
                            cos.WorkflowStage = "Ready for billing";
                        }
                        else
                        {
                            cos.WorkflowStage = "Open";
                        }
                    }
                }
            }

            return (data, recordsFiltered, totalRecords);
        }
    }
}
