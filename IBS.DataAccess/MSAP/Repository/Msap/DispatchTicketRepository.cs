using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class DispatchTicketRepository(MsapDbContext db)
        : Repository<DispatchTicket>(db), IDispatchTicketRepository
    {
        private readonly MsapDbContext _db = db;

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
            var query = dbSet
                .Include(dt => dt.Service)
                .Include(dt => dt.Terminal).ThenInclude(dt => dt.Port)
                .Include(dt => dt.Tugboat)
                .Include(dt => dt.TugMaster)
                .Include(dt => dt.Vessel)
                .Include(dt => dt.Customer)
                .Include(dt => dt.Billing)
                .Where(dt => dt.Status != "For Posting" && dt.Status != "Incomplete" && dt.Status != "Draft" && dt.Status != "Requested");

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
                    _ => query.Where(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted)
                };
            }
            else
            {
                query = query.Where(dt => dt.Status != MsapConstants.DispatchTicketStatus.Deleted);
            }

            if (!string.IsNullOrEmpty(parameters.Search.Value))
            {
                var s = parameters.Search.Value.ToLower();
                query = query.Where(dt =>
                    (dt.COSNumber != null && dt.COSNumber.ToLower().Contains(s)) ||
                    dt.DispatchNumber.ToLower().Contains(s) ||
                    (dt.Service != null && dt.Service.ServiceName.ToLower().Contains(s)) ||
                    (dt.Tugboat != null && dt.Tugboat.TugboatName.ToLower().Contains(s)) ||
                    (dt.Customer != null && dt.Customer.CustomerName.ToLower().Contains(s)) ||
                    (dt.Vessel != null && dt.Vessel.VesselName.ToLower().Contains(s)) ||
                    (dt.Billing != null && dt.Billing.MsapBillingNumber.ToLower().Contains(s)) ||
                    dt.Status.ToLower().Contains(s));
            }

            // Column-specific search
            if (parameters.Columns != null)
            {
                foreach (var column in parameters.Columns)
                {
                    if (column.Search?.Value is { Length: > 0 } searchValue)
                    {
                        if (column.Data == "status")
                        {
                            query = query.Where(dt => dt.Status.ToLower() == searchValue);
                        }
                        else if (column.Data == "date" || column.Data == "Date")
                        {
                            var range = searchValue.Split("..");
                            if (range.Length == 2
                                && DateOnly.TryParseExact(range[0], "yyyy-MM-dd", out var from)
                                && DateOnly.TryParseExact(range[1], "yyyy-MM-dd", out var to) && from <= to)
                                query = query.Where(dt => dt.Date >= from && dt.Date <= to);
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

            if (parameters.Order?.Count > 0 && parameters.Columns != null)
            {
                var col = parameters.Columns[parameters.Order[0].Column].Data;
                var dir = parameters.Order[0].Dir.ToLower() == "asc" ? "ascending" : "descending";
                query = query.OrderBy($"{col} {dir}");
            }
            else
            {
                query = query.OrderByDescending(dt => dt.Date);
            }

            var data = await query
                .Skip(parameters.Start)
                .Take(parameters.Length)
                .ToListAsync(cancellationToken);

            return (data, recordsFiltered, totalRecords);
        }
    }
}
