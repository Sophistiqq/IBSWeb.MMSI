using System.Linq.Expressions;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class DispatchTicketRepository(MsapDbContext db)
        : Repository<DispatchTicket>(db), IDispatchTicketRepository
    {
        private readonly MsapDbContext _db = db;

        public static IQueryable<DispatchTicket> QueryOtherTugChargeTickets(MsapDbContext db)
        {
            DateTime now = DateTimeHelper.GetCurrentPhilippineTime();
            DateOnly today = DateOnly.FromDateTime(now);
            TimeOnly currentTime = TimeOnly.FromDateTime(now);
            return db.MsapDispatchTickets.Where(t => t.Customer.Company == MsapConstants.Company_MMSI
                && !t.Tugboat.IsCompanyOwned && t.Tugboat.TugboatOwnerId != null && t.VesselScheduleServiceEntryId != null
                && t.ExpectedServiceEntry!.CancelledAt == null
                && t.CustomerOrderSlipId != null && (t.CustomerOrderSlip!.Status == MsapConstants.CustomerOrderSlipStatus.Open
                    || t.CustomerOrderSlip.Status == MsapConstants.CustomerOrderSlipStatus.Closed)
                && t.Status != MsapConstants.DispatchTicketStatus.Deleted && t.DispatchNumber != "" && t.DateLeft != null && t.DateArrived != null
                && t.TimeLeft != null && t.TimeArrived != null
                && (t.DateArrived < today || t.DateArrived == today && t.TimeArrived <= currentTime)
                && (t.DateArrived > t.DateLeft || t.DateArrived == t.DateLeft && t.TimeArrived > t.TimeLeft));
        }

        public async Task<List<DispatchTicket>> GetOtherTugChargeTicketsAsync(int cosId, CancellationToken ct)
        {
            return await QueryOtherTugChargeTickets(_db).AsNoTracking()
                .Where(t => t.CustomerOrderSlipId == cosId)
                .Include(t => t.OtherTugChargeLine).ThenInclude(l => l!.Charge)
                .OrderBy(t => t.DispatchTicketId).ToListAsync(ct);
        }

        public async Task<List<int>> GetOtherTugChargeIdsByCosAsync(int cosId, CancellationToken ct = default)
        {
            return await _db.MsapOtherTugChargeLines
                .AsNoTracking()
                .Where(l => l.Ticket.CustomerOrderSlipId == cosId)
                .Select(l => l.OtherTugChargeId)
                .Distinct()
                .ToListAsync(ct);
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        public override async Task<IEnumerable<DispatchTicket>> GetAllAsync(Expression<Func<DispatchTicket, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<DispatchTicket> query = dbSet
                .Include(a => a.Customer)
                .Include(a => a.Service)
                .Include(a => a.Terminal).ThenInclude(t => t.Port)
                .Include(a => a.Tugboat)
                .Include(a => a.TugMaster)
                .Include(a => a.Vessel);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public override async Task<DispatchTicket?> GetAsync(Expression<Func<DispatchTicket, bool>> filter, CancellationToken cancellationToken = default)
        {
            return await dbSet.Where(filter)
                .Include(a => a.Customer)
                .Include(a => a.Service)
                .Include(a => a.Terminal).ThenInclude(t => t.Port)
                .Include(a => a.Tugboat).ThenInclude(t => t.TugboatOwner)
                .Include(a => a.TugMaster)
                .Include(a => a.Vessel)
                .Include(a => a.OtherTugChargeLine).ThenInclude(l => l!.Charge)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<DispatchTicket?> GetDispatchTicketWithDetailsAsync(int id, CancellationToken cancellationToken = default)
        {
            return await dbSet.Where(dt => dt.DispatchTicketId == id)
                .Include(a => a.Customer)
                .Include(a => a.Service)
                .Include(a => a.Terminal).ThenInclude(t => t.Port)
                .Include(a => a.Tugboat).ThenInclude(t => t.TugboatOwner)
                .Include(a => a.TugMaster)
                .Include(a => a.Vessel)
                .Include(a => a.OtherTugChargeLine).ThenInclude(l => l!.Charge)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IEnumerable<DispatchTicket>> GetDispatchTicketsWithDetailsAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default)
        {
            var startDate = DateOnly.FromDateTime(start);
            var endDate = DateOnly.FromDateTime(end);

            return await dbSet
                .Include(a => a.Customer)
                .Include(a => a.Service)
                .Include(a => a.Terminal).ThenInclude(t => t.Port)
                .Include(a => a.Tugboat).ThenInclude(t => t.TugboatOwner)
                .Include(a => a.TugMaster)
                .Include(a => a.Vessel)
                .Where(dt => dt.DateLeft <= endDate && dt.DateArrived >= startDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<IEnumerable<DispatchTicket>> GetAllDispatchTicketsWithDetailsAsync(CancellationToken cancellationToken = default)
        {
            return await dbSet
                .Include(a => a.Customer)
                .Include(a => a.Service)
                .Include(a => a.Terminal).ThenInclude(t => t.Port)
                .Include(a => a.Tugboat).ThenInclude(t => t.TugboatOwner)
                .Include(a => a.TugMaster)
                .Include(a => a.Vessel)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> IsCustomerOrderSlipEditableAsync(int? customerOrderSlipId, CancellationToken cancellationToken = default)
        {
            if (customerOrderSlipId == null)
            {
                return true;
            }

            var customerOrderSlip = await _db.MsapCustomerOrderSlips.FindAsync(new object[] { customerOrderSlipId.Value }, cancellationToken);
            return customerOrderSlip?.Status == IBS.Utility.MSAP.Constants.MsapConstants.CustomerOrderSlipStatus.Open;
        }

        public async Task<DispatchTicketViewModel> GetDispatchTicketSelectLists(DispatchTicketViewModel model, CancellationToken cancellationToken = default)
        {
            model.Services = await _db.MsapServices.OrderBy(s => s.ServiceName).Select(s => new SelectListItem { Value = s.ServiceId.ToString(), Text = s.ServiceName }).ToListAsync(cancellationToken);
            model.Ports = await _db.MsapPorts.OrderBy(p => p.PortName).Select(p => new SelectListItem { Value = p.PortId.ToString(), Text = p.PortName }).ToListAsync(cancellationToken);
            model.Tugboats = await _db.MsapTugboats.OrderBy(t => t.TugboatName).Select(t => new SelectListItem { Value = t.TugboatId.ToString(), Text = t.TugboatName }).ToListAsync(cancellationToken);
            model.TugMasters = await _db.MsapTugMasters.OrderBy(t => t.TugMasterName).Select(t => new SelectListItem { Value = t.TugMasterId.ToString(), Text = t.TugMasterName }).ToListAsync(cancellationToken);
            model.Vessels = await _db.MsapVessels.OrderBy(v => v.VesselName).Select(v => new SelectListItem { Value = v.VesselId.ToString(), Text = v.VesselName }).ToListAsync(cancellationToken);
            var portId = model.Terminal?.Port?.PortId ?? model.PortId;
            model.Terminals = await _db.MsapTerminals.Where(t => t.PortId == portId).OrderBy(t => t.TerminalName).Select(t => new SelectListItem { Value = t.TerminalId.ToString(), Text = t.TerminalName }).ToListAsync(cancellationToken);
            return model;
        }

        public async Task<(IEnumerable<DispatchTicket> Data, int RecordsFiltered, int TotalRecords)> GetPagedDispatchTicketsAsync(DataTablesParameters parameters, string filterType, CancellationToken cancellationToken = default)
        {
            var query = dbSet.AsNoTracking()
                .Include(dt => dt.Service)
                .Include(dt => dt.Terminal).ThenInclude(dt => dt.Port)
                .Include(dt => dt.Tugboat)
                .Include(dt => dt.TugMaster)
                .Include(dt => dt.Vessel)
                .Include(dt => dt.Customer)
                .Include(dt => dt.Billing)
                .Where(dt => dt.Status != "For Posting" && dt.Status != "Incomplete" && dt.Status != "Draft" && dt.Status != "Requested");

            var requestedDeleted = parameters?.Columns?.Any(c =>
                c?.Data == "status"
                && string.Equals(c.Search?.Value, MsapConstants.DispatchTicketStatus.Deleted, StringComparison.OrdinalIgnoreCase)) == true;

            if (!string.IsNullOrEmpty(filterType))
            {
                query = filterType.ToLower() switch
                {
                    "for tariff" => query.Where(dt => dt.Status == "For Tariff"),
                    "for approval" => query.Where(dt => dt.Status == "For Approval"),
                    "disapproved" => query.Where(dt => dt.Status == "Disapproved"),
                    "for billing" => query.Where(dt => dt.Status == "For Billing"),
                    "billed" => query.Where(dt => dt.Status == "Billed"),
                    "deleted" => query.Where(dt => dt.Status == MsapConstants.DispatchTicketStatus.Deleted),
                    _ => requestedDeleted ? query : query.Where(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted)
                };
            }
            else if (!requestedDeleted)
            {
                query = query.Where(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted);
            }

            var globalSearch = parameters?.Search?.Value;
            if (!string.IsNullOrEmpty(globalSearch))
            {
                var s = globalSearch.ToLower();
                query = query.Where(dt =>
                    (dt.COSNumber != null && dt.COSNumber.ToLower().Contains(s))
                    || dt.DispatchNumber.ToLower().Contains(s)
                    || (dt.Service != null && dt.Service.ServiceName.ToLower().Contains(s))
                    || (dt.Tugboat != null && dt.Tugboat.TugboatName.ToLower().Contains(s))
                    || (dt.Customer != null && dt.Customer.CustomerName.ToLower().Contains(s))
                    || (dt.Vessel != null && dt.Vessel.VesselName.ToLower().Contains(s))
                    || (dt.Billing != null && dt.Billing.MsapBillingNumber.ToLower().Contains(s))
                    || (dt.Terminal != null && dt.Terminal.TerminalName.ToLower().Contains(s))
                    || (dt.Terminal != null && dt.Terminal.Port != null && dt.Terminal.Port.PortName.ToLower().Contains(s))
                    || (dt.VoyageNumber != null && dt.VoyageNumber.ToLower().Contains(s))
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
                            var normalizedStatus = searchValue.ToLower();
                            query = query.Where(dt => dt.Status.ToLower() == normalizedStatus);
                        }
                        else if (column.Data == "date" || column.Data == "Date")
                        {
                            var range = searchValue.Split("..");
                            if (range.Length == 2
                                && DateOnly.TryParseExact(range[0], "yyyy-MM-dd", out var from)
                                && DateOnly.TryParseExact(range[1], "yyyy-MM-dd", out var to) && from <= to)
                            {
                                query = query.Where(dt => dt.Date >= from && dt.Date <= to);
                            }
                            else if (DateOnly.TryParse(searchValue, out var parsedDate))
                            {
                                query = query.Where(dt => dt.Date == parsedDate);
                            }
                        }
                    }
                }
            }

            var totalRecords = await dbSet.CountAsync(dt => dt.Status != "For Posting" && dt.Status != "Incomplete" && dt.Status != "Draft" && dt.Status != "Requested" && dt.Status != MsapConstants.DispatchTicketStatus.Deleted, cancellationToken);
            var recordsFiltered = await query.CountAsync(cancellationToken);

            var order = parameters?.Order?.FirstOrDefault();
            if (order != null
                && parameters?.Columns != null
                && order.Column >= 0
                && order.Column < parameters.Columns.Count)
            {
                var col = parameters.Columns[order.Column]?.Data?.ToLowerInvariant();
                var isAscending = string.Equals(order.Dir, "asc", StringComparison.OrdinalIgnoreCase);
                query = col switch
                {
                    "date" => isAscending ? query.OrderBy(dt => dt.Date) : query.OrderByDescending(dt => dt.Date),
                    "cosnumber" => isAscending ? query.OrderBy(dt => dt.COSNumber) : query.OrderByDescending(dt => dt.COSNumber),
                    "dispatchnumber" => isAscending ? query.OrderBy(dt => dt.DispatchNumber) : query.OrderByDescending(dt => dt.DispatchNumber),
                    "billing.msapbillingnumber" => isAscending
                        ? query.OrderBy(dt => dt.Billing != null ? dt.Billing.MsapBillingNumber : null)
                        : query.OrderByDescending(dt => dt.Billing != null ? dt.Billing.MsapBillingNumber : null),
                    "dateleft" => isAscending
                        ? query.OrderBy(dt => dt.DateLeft).ThenBy(dt => dt.TimeLeft)
                        : query.OrderByDescending(dt => dt.DateLeft).ThenByDescending(dt => dt.TimeLeft),
                    "datearrived" => isAscending
                        ? query.OrderBy(dt => dt.DateArrived).ThenBy(dt => dt.TimeArrived)
                        : query.OrderByDescending(dt => dt.DateArrived).ThenByDescending(dt => dt.TimeArrived),
                    "service.servicename" => isAscending ? query.OrderBy(dt => dt.Service.ServiceName) : query.OrderByDescending(dt => dt.Service.ServiceName),
                    "tugboat.tugboatname" => isAscending ? query.OrderBy(dt => dt.Tugboat.TugboatName) : query.OrderByDescending(dt => dt.Tugboat.TugboatName),
                    "customer.customername" => isAscending ? query.OrderBy(dt => dt.Customer.CustomerName) : query.OrderByDescending(dt => dt.Customer.CustomerName),
                    "vessel.vesselname" => isAscending ? query.OrderBy(dt => dt.Vessel.VesselName) : query.OrderByDescending(dt => dt.Vessel.VesselName),
                    "status" => isAscending ? query.OrderBy(dt => dt.Status) : query.OrderByDescending(dt => dt.Status),
                    _ => query.OrderByDescending(dt => dt.Date)
                };
            }
            else
            {
                query = query.OrderByDescending(dt => dt.Date);
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
