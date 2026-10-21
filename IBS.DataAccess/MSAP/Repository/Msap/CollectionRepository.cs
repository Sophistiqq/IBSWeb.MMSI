using System.Linq.Expressions;
using System.Linq.Dynamic.Core;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class CollectionRepository(MsapDbContext db): Repository<Collection>(db), ICollectionRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<Collection?> GetForUpdateAsync(int id, CancellationToken ct = default)
        {
            var record = await _db.MsapCollections
                .FromSqlInterpolated($"SELECT * FROM msap.mmsi_collections WHERE \"RECID\" = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (record != null)
            {
                await _db.Entry(record).ReloadAsync(ct);
            }
            return record;
        }

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        public override async Task<Collection?> GetAsync(Expression<Func<Collection, bool>> filter, CancellationToken cancellationToken = default)
        {
            return await dbSet.Where(filter)
                .Include(c => c.Customer)
                .Include(c => c.BankAccount)
                .Include(c => c.PaidBills)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public override async Task<IEnumerable<Collection>> GetAllAsync(Expression<Func<Collection, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<Collection> query = dbSet
                .Include(c => c.Customer)
                .Include(c => c.BankAccount);

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapCustomersById(CancellationToken cancellationToken = default)
        {
            return await _db.Customers
                .OrderBy(s => s.CustomerName)
                .Select(s => new SelectListItem
                {
                    Value = s.CustomerId.ToString(),
                    Text = s.CustomerName
                }).ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapCustomersWithCollectiblesSelectList(int collectionId, string type, CancellationToken cancellationToken = default)
        {
            var billingsToBeCollected = await _db.MsapBillings
                .Where(t => (t.Balance > 0 && t.Status == MsapConstants.BillingStatus.ForCollection) || (collectionId != 0 && t.CollectionId == collectionId))
                .Include(t => t.Customer)
                .ToListAsync(cancellationToken);

            var listOfCustomerWithCollectibleBillings = billingsToBeCollected
                .Where(t => t.Customer != null)
                .Select(t => t.Customer.CustomerId)
                .Distinct()
                .ToList();

            return await _db.Customers
                .Where(c => listOfCustomerWithCollectibleBillings.Contains(c.CustomerId) &&
                            (string.IsNullOrEmpty(type) || c.Type == type))
                .OrderBy(s => s.CustomerName)
                .Select(s => new SelectListItem
                {
                    Value = s.CustomerId.ToString(),
                    Text = s.CustomerName
                }).ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetMsapUncollectedBillingsById(CancellationToken cancellationToken = default)
        {
            var billingsList = await _db.MsapBillings
                .Where(dt => dt.Balance > 0)
                .OrderBy(dt => dt.MsapBillingNumber).Select(s => new SelectListItem
                {
                    Value = s.MsapBillingId.ToString(),
                    Text = $"{s.MsapBillingNumber} - {s.Customer.CustomerName}, {s.Date}"
                }).ToListAsync(cancellationToken);

            return billingsList;
        }

        public async Task<List<SelectListItem>> GetMsapCollectedBillsById(int collectionId, CancellationToken cancellationToken = default)
        {
            var billingsList = await _db.MsapBillings
                .Where(dt => dt.CollectionId == collectionId)
                .OrderBy(dt => dt.MsapBillingNumber).Select(b => new SelectListItem
                {
                    Value = b.MsapBillingId.ToString(),
                    Text = $"{b.MsapBillingNumber}"
                }).ToListAsync(cancellationToken);

            return billingsList;
        }

        public async Task<List<SelectListItem>?> GetMsapUncollectedBillingsByCustomer(int? customerId, CancellationToken cancellationToken)
        {
            var billings = await _db
                .MsapBillings
                .Where(b => b.CustomerId == customerId && b.Balance > 0 && b.Status == MsapConstants.BillingStatus.ForCollection)
                .Include(b => b.Customer)
                .OrderBy(b => b.MsapBillingNumber)
                .ToListAsync(cancellationToken);

            var billingsList = billings.Select(b => new SelectListItem
            {
                Value = b.MsapBillingId.ToString(),
                Text = $"{b.MsapBillingNumber}"
            }).ToList();

            return billingsList;
        }

        public async Task<List<Billing>> GetMsapUncollectedBillingsByCustomerList(int? customerId, CancellationToken cancellationToken)
        {
            return await _db
                .MsapBillings
                .Where(b => b.CustomerId == customerId && b.Balance > 0 && b.Status == MsapConstants.BillingStatus.ForCollection)
                .OrderBy(b => b.MsapBillingNumber)
                .ToListAsync(cancellationToken);
        }

        public async Task UpdateBillingPayment(int billingId, decimal paidAmount, CancellationToken cancellationToken = default)
        {
            var billing = await _db.MsapBillings.FirstOrDefaultAsync(b => b.MsapBillingId == billingId, cancellationToken);
            if (billing != null)
            {
                billing.AmountPaid += paidAmount;
                billing.Balance = billing.Amount - billing.AmountPaid;

                if (billing.Balance <= 0)
                {
                    billing.IsPaid = true;
                    billing.Status = MsapConstants.BillingStatus.Collected;
                }
                else
                {
                    billing.IsPaid = false;
                    billing.Status = MsapConstants.BillingStatus.ForCollection;
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task RemoveBillingPayment(int billingId, decimal paidAmount, decimal offsetAmount, CancellationToken cancellationToken = default)
        {
            var billing = await _db.MsapBillings.FirstOrDefaultAsync(b => b.MsapBillingId == billingId, cancellationToken);
            if (billing != null)
            {
                var total = paidAmount + offsetAmount;
                billing.AmountPaid -= total;
                billing.Balance += total;
                billing.IsPaid = false;
                billing.Status = MsapConstants.BillingStatus.ForCollection;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<string> GenerateCollectionNumber(CancellationToken cancellationToken = default)
        {
            var lastRecord = await _db.MsapCollections
                .Where(b => b.IsUndocumented && !string.IsNullOrEmpty(b.MsapCollectionNumber))
                .OrderByDescending(b => b.MsapCollectionNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastRecord == null)
            {
                return "C000001";
            }

            var lastSeries = lastRecord.MsapCollectionNumber.Substring(1); // "C" is 1 char
            if (int.TryParse(lastSeries, out int lastNumber))
            {
                return "C" + ((lastNumber + 1).ToString("D6"));
            }

            return "C" + (DateTimeHelper.GetCurrentPhilippineTime().Ticks % 1000000).ToString("D6");
        }

        public async Task<(IEnumerable<Collection> Data, int RecordsFiltered, int TotalRecords)> GetPagedCollectionsAsync(DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            var query = dbSet
                .Include(c => c.Customer)
                .Include(c => c.BankAccount)
                .AsQueryable();

            if (!string.IsNullOrEmpty(parameters.Search.Value))
            {
                var s = parameters.Search.Value.ToLower();
                query = query.Where(c =>
                    c.MsapCollectionNumber.ToLower().Contains(s) ||
                    c.Customer.CustomerName.ToLower().Contains(s)
                );
            }

            // Column-specific search
            if (parameters.Columns != null)
            {
                foreach (var column in parameters.Columns)
                {
                    if (column.Search?.Value is { Length: > 0 } searchValue)
                    {
                        if (column.Data == "date" || column.Data == "Date")
                        {
                            if (DateOnly.TryParse(searchValue, out var parsedDate))
                            {
                                query = query.Where(c => c.Date == parsedDate);
                            }
                        }
                    }
                }
            }

            var totalRecords = await dbSet.CountAsync(cancellationToken);
            var recordsFiltered = await query.CountAsync(cancellationToken);

            if (parameters.Order?.Count > 0 && parameters.Columns != null)
            {
                var col = parameters.Columns[parameters.Order[0].Column].Data;
                var dir = parameters.Order[0].Dir.ToLower() == "asc" ? "ascending" : "descending";
                query = query.OrderBy($"{col} {dir}");
            }
            else
            {
                query = query.OrderByDescending(c => c.Date);
            }

            var data = await query
                .Skip(parameters.Start)
                .Take(parameters.Length)
                .ToListAsync(cancellationToken);

            return (data, recordsFiltered, totalRecords);
        }
    }
}
