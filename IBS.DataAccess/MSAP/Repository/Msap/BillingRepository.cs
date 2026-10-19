using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class BillingRepository(MsapDbContext db): Repository<Billing>(db), IBillingRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<Billing?> GetForUpdateAsync(int id, CancellationToken ct = default)
        {
            var record = await _db.MsapBillings
                .FromSqlInterpolated($"SELECT * FROM msap.mmsi_billings WHERE \"RECID\" = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (record != null)
            {
                await _db.Entry(record).ReloadAsync(ct);
            }
            return record;
        }

        public async Task AddSalesBookAsync(IBS.Models.MSAP.Books.SalesBook entry, CancellationToken ct)
        {
            var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == entry.ApprovedByUserId && u.IsActive, ct);
            if (actor == null || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Trim().Length > 255)
            {
                throw new InvalidOperationException("The approver needs an active account with a full name.");
            }
            entry.ApprovedByName = actor.Name.Trim();
            await _db.MsapSalesBooks.AddAsync(entry, ct);
        }

        public async Task<bool> HasSalesBookAsync(int billingId, CancellationToken ct)
        {
            return await _db.MsapSalesBooks.AnyAsync(b => b.DocumentId == billingId, ct);
        }

        public async Task VoidSalesBookAsync(int billingId, string actor, string? reason, DateTime voidedAt, CancellationToken ct)
        {
            var entries = await _db.MsapSalesBooks.Where(b => b.DocumentId == billingId && b.VoidedAt == null).ToListAsync(ct);
            foreach (var entry in entries)
            {
                entry.VoidedAt = voidedAt;
                entry.VoidedBy = actor;
                entry.VoidReason = reason;
            }
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        public override async Task<IEnumerable<Billing>> GetAllAsync(Expression<Func<Billing, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<Billing> query = dbSet
                .Include(a => a.Terminal)
                .Include(a => a.Vessel)
                .Include(a => a.Customer)
                .Include(a => a.BasicBilling)
                .Include(a => a.BafBilling)
                .Include(a => a.Port);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public override async Task<Billing?> GetAsync(Expression<Func<Billing, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<Billing> query = dbSet
                .Include(b => b.Terminal)
                .ThenInclude(t => t.Port)
                .Include(b => b.Vessel)
                .Include(b => b.Customer)
                .Include(b => b.Principal)
                .Include(b => b.ApprovedByUser)
                .Include(b => b.BasicBilling)
                .Include(b => b.BafBilling)
                .Include(b => b.CustomerOrderSlip);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<List<string>?> GetToBillDispatchTicketListAsync(int billingId, CancellationToken cancellationToken = default)
        {
            return await _db.MsapDispatchTickets.Where(t => t.BillingId == billingId)
                .Select(d => d.DispatchTicketId.ToString()).ToListAsync(cancellationToken);
        }

        public async Task<List<string>?> GetUniqueTugboatsListAsync(int billingId, CancellationToken cancellationToken = default)
        {
            int sourceId = await _db.MsapBillings.Where(b => b.MsapBillingId == billingId)
                .Select(b => b.BasicBillingId ?? b.MsapBillingId).SingleAsync(cancellationToken);
            return await _db.MsapDispatchTickets
                .Where(dt => dt.BillingId == sourceId)
                .Select(dt => dt.Tugboat.TugboatName.ToString())
                .Distinct() // Ensures unique values
                .ToListAsync(cancellationToken);
        }

        public async Task<List<DispatchTicket>?> GetPaidDispatchTicketsAsync(int billingId, CancellationToken cancellationToken = default)
        {
            int sourceId = await _db.MsapBillings.Where(b => b.MsapBillingId == billingId)
                .Select(b => b.BasicBillingId ?? b.MsapBillingId).SingleAsync(cancellationToken);
            return await _db.MsapDispatchTickets.AsNoTracking()
                .Where(dt => dt.BillingId == sourceId)
                .Include(a => a.Service)
                .Include(a => a.Terminal).ThenInclude(t => t.Port)
                .Include(a => a.Tugboat)
                .Include(a => a.TugMaster)
                .Include(a => a.Vessel)
                .OrderBy(dt => dt.DateLeft).ThenBy(dt => dt.TimeLeft)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapTerminalsByPortId(int portId, CancellationToken cancellationToken)
        {
            var terminals = await _db
                .MsapTerminals
                .Where(t => t.PortId == portId)
                .OrderBy(t => t.TerminalName)
                .ToListAsync(cancellationToken);

            var terminalsList = terminals.Select(t => new SelectListItem
            {
                Value = t.TerminalId.ToString(),
                Text = t.TerminalName
            }).ToList();

            return terminalsList;
        }

        public async Task<List<SelectListItem>?> GetMsapCustomersWithBillablesSelectList(int? currentCustomerId, string type, CancellationToken cancellationToken = default)
        {
            var dispatchToBeBilled = await _db.MsapDispatchTickets
                .Where(t => t.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling || (currentCustomerId.GetValueOrDefault() != 0 && t.CustomerId == currentCustomerId))
                .Include(t => t.Customer)
                .ToListAsync(cancellationToken);

            var listOfCustomerWithBillableTickets = dispatchToBeBilled
                .Where(t => t.Customer != null)
                .Select(t => t.Customer.CustomerId)
                .Distinct()
                .ToList();

            return await _db.Customers
                .Where(c => listOfCustomerWithBillableTickets.Contains(c.CustomerId) &&
                            (string.IsNullOrEmpty(type) || c.Type == type) &&
                            c.IsActive)
                .OrderBy(s => s.CustomerName)
                .Select(s => new SelectListItem
                {
                    Value = s.CustomerId.ToString(),
                    Text = s.CustomerName
                }).ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapUnbilledTicketsById(string type, CancellationToken cancellationToken = default)
        {
            var dispatchTicketList = await _db.MsapDispatchTickets
                .Where(dt => dt.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling)
                .OrderBy(dt => dt.DispatchNumber)
                .Select(s => new SelectListItem
                {
                    Value = s.DispatchTicketId.ToString(),
                    Text = s.DispatchNumber
                }).ToListAsync(cancellationToken);

            return dispatchTicketList;
        }

        public async Task<List<SelectListItem>?> GetMsapUnbilledTicketsByCustomer(int? customerId, CancellationToken cancellationToken)
        {
            var tickets = await _db
                .MsapDispatchTickets
                .Where(b => b.CustomerId == customerId && b.Status == IBS.Utility.MSAP.Constants.MsapConstants.DispatchTicketStatus.ForBilling)
                .Include(b => b.Customer)
                .OrderBy(b => b.DispatchNumber)
                .ToListAsync(cancellationToken);

            var ticketsList = tickets.Select(b => new SelectListItem
            {
                Value = b.DispatchTicketId.ToString(),
                Text = b.DispatchNumber
            }).ToList();

            return ticketsList;
        }

        public async Task<List<SelectListItem>> GetMsapBilledTicketsById(int id, CancellationToken cancellationToken = default)
        {
            var dispatchTicketList = await _db.MsapDispatchTickets
                .Where(dt => dt.BillingId == id)
                .OrderBy(dt => dt.DispatchNumber).Select(s => new SelectListItem
                {
                    Value = s.DispatchTicketId.ToString(),
                    Text = s.DispatchNumber
                }).ToListAsync(cancellationToken);

            return dispatchTicketList;
        }

        public async Task<string> GenerateBillingNumber(int year, CancellationToken cancellationToken = default)
        {
            var lastRecord = await _db.MsapBillings
                .Where(b => b.Year == year)
                .Where(b => !string.IsNullOrEmpty(b.MsapBillingNumber) && b.MsapBillingNumber.StartsWith("B"))
                .OrderByDescending(b => b.MsapBillingNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastRecord == null)
            {
                return "B000001";
            }

            var lastSeries = lastRecord.MsapBillingNumber.Substring(1);
            if (int.TryParse(lastSeries, out int lastNumber))
            {
                return "B" + ((lastNumber + 1).ToString("D6"));
            }

            return "B" + (DateTime.Now.Ticks % 1000000).ToString("D6");
        }

        public Billing ProcessAddress(Billing model, CancellationToken cancellationToken = default)
        {
            // ... (rest of ProcessAddress implementation)
            var words = model.PrincipalId != null
                ? model.Principal?.Address1.Split(' ')
                : model.Customer?.CustomerAddress.Split(' ');
            var resultStrings = new List<string>();
            var currentString = "";

            if (words == null)
            {
                model.AddressLine1 = string.Empty;
                model.AddressLine2 = string.Empty;
                model.AddressLine3 = string.Empty;
                model.AddressLine4 = string.Empty;

                return model;
            }
            foreach (var word in words)
            {
                if (currentString.Length + word.Length + (currentString.Length > 0 ? 1 : 0) > 40)
                {
                    if (currentString.Length > 0)
                    {
                        resultStrings.Add(currentString.Trim());
                    }
                    currentString = word;
                }
                else
                {
                    currentString += (currentString.Length > 0 ? " " : "") + word;
                }
            }

            if (currentString.Length > 0)
            {
                resultStrings.Add(currentString.Trim());
            }
            var firstString = resultStrings.Count > 0 ? resultStrings[0] : "";
            var secondString = resultStrings.Count > 1 ? resultStrings[1] : "";
            var thirdString = resultStrings.Count > 2 ? resultStrings[2] : "";
            var fourthString = resultStrings.Count > 3 ? resultStrings[3] : "";

            model.AddressLine1 = firstString;
            model.AddressLine2 = secondString;
            model.AddressLine3 = thirdString;
            model.AddressLine4 = fourthString;

            return model;
        }

        public async Task<(IEnumerable<Billing> Data, int RecordsFiltered, int TotalRecords)> GetPagedBillingsAsync(DataTablesParameters parameters, CancellationToken cancellationToken, int? customerOrderSlipId = null)
        {
            IQueryable<Billing> query = dbSet
                .Include(b => b.Customer)
                .Include(b => b.Terminal).ThenInclude(b => b.Port)
                .Include(b => b.Vessel)
                .Include(b => b.BafBilling);

            if (customerOrderSlipId.HasValue)
            {
                query = query.Where(b => b.CustomerOrderSlipId == customerOrderSlipId.Value || _db.Set<DispatchTicket>()
                    .Any(t => t.CustomerOrderSlipId == customerOrderSlipId.Value && t.BillingId == b.MsapBillingId));
            }
            int totalRecords = await query.CountAsync(cancellationToken);

            if (!string.IsNullOrEmpty(parameters.Search.Value))
            {
                var s = parameters.Search.Value.ToLower();
                var matchingBillingIds = await _db.Set<DispatchTicket>()
                    .Where(dt => dt.BillingId != null && dt.DispatchNumber.ToLower().Contains(s))
                    .Select(dt => dt.BillingId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                query = query.Where(dt =>
                    dt.MsapBillingNumber.ToLower().Contains(s) ||
                    dt.Customer.CustomerName.ToLower().Contains(s) ||
                    dt.Vessel.VesselName.ToLower().Contains(s) ||
                    dt.Status.ToLower().Contains(s) ||
                    matchingBillingIds.Contains(dt.MsapBillingId)
                );
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
                            query = query.Where(b => b.Status.ToLower() == searchValue.ToLower());
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

            var recordsFiltered = await query.CountAsync(cancellationToken);

            if (parameters.Order?.Count > 0 && parameters.Columns != null)
            {
                var col = parameters.Columns[parameters.Order[0].Column].Data;
                var dir = parameters.Order[0].Dir.ToLower() == "asc" ? "ascending" : "descending";
                query = query.OrderBy($"{col} {dir}");
            }

            var data = await query
                .Skip(parameters.Start)
                .Take(parameters.Length)
                .ToListAsync(cancellationToken);

            var billingIds = data.Select(b => b.MsapBillingId).ToList();
            var ticketGroups = await _db.Set<DispatchTicket>()
                .Where(dt => dt.BillingId != null && billingIds.Contains(dt.BillingId.Value))
                .GroupBy(dt => dt.BillingId!.Value)
                .Select(g => new { BillingId = g.Key, Numbers = g.Select(dt => dt.DispatchNumber) })
                .ToListAsync(cancellationToken);
            var ticketMap = ticketGroups.ToDictionary(g => g.BillingId, g => string.Join(", ", g.Numbers));
            foreach (var b in data)
            {
                if (ticketMap.TryGetValue(b.MsapBillingId, out var numbers))
                    b.DispatchTicketNumbers = numbers;
            }

            return (data, recordsFiltered, totalRecords);
        }

        public async Task<List<Billing>> GetBillingsByCollectionIdAsync(int collectionId, CancellationToken cancellationToken)
        {
            return await _db.MsapBillings
                .Where(b => b.CollectionId == collectionId)
                .ToListAsync(cancellationToken);
        }
    }
}
