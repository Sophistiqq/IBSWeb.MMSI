using System.Globalization;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IBS.DataAccess.MSAP.Repository.MasterFile
{
    public class CustomerRepository(MsapDbContext db): Repository<Customer>(db), ICustomerRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<string> GenerateCodeAsync(string customerType, CancellationToken cancellationToken = default)
        {
            var prefix = customerType switch
            {
                nameof(CustomerType.Retail) => "RET",
                nameof(CustomerType.Industrial) => "IND",
                nameof(CustomerType.Reseller) => "RES",
                _ => throw new ArgumentException("Unsupported customer type.", nameof(customerType))
            };

            if (_db.Database.CurrentTransaction is null)
            {
                throw new InvalidOperationException("Customer codes must be generated inside the customer creation transaction.");
            }

            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext('msap_customer_code'), hashtext({customerType}))",
                cancellationToken);

            var codes = await _db.Customers.Where(c => c.CustomerCode.StartsWith(prefix))
                .Select(c => c.CustomerCode).ToListAsync(cancellationToken);
            var lastNumber = 0;
            foreach (var code in codes)
            {
                if (code.Length != 7 || !int.TryParse(code.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    throw new InvalidOperationException($"Customer code {code} does not match the {prefix}#### format.");
                }
                lastNumber = Math.Max(lastNumber, number);
            }

            if (lastNumber >= 9999)
            {
                throw new InvalidOperationException($"The {prefix} customer code range is exhausted.");
            }

            return prefix + (lastNumber + 1).ToString("D4", CultureInfo.InvariantCulture);
        }

        public async Task<bool> IsTinNoExistAsync(string tin, string company, CancellationToken cancellationToken = default)
        {
            if (tin == "000-000-000-00000")
            {
                return false;
            }

            return await _db.Customers
                .AnyAsync(c =>
                    c.Company == company &&
                    c.CustomerTin == tin,
                    cancellationToken);
        }

        public async Task UpdateAsync(Customer model, CancellationToken cancellationToken = default)
        {
            var existingCustomer = await _db.Customers
                .FirstOrDefaultAsync(x => x.CustomerId == model.CustomerId, cancellationToken)
                                   ?? throw new InvalidOperationException($"Customer with id '{model.CustomerId}' not found.");

            existingCustomer.CustomerName = model.CustomerName;
            existingCustomer.CustomerAddress = model.CustomerAddress;
            existingCustomer.CustomerTin = model.CustomerTin;
            existingCustomer.BusinessStyle = model.BusinessStyle;
            existingCustomer.CustomerTerms = model.CustomerTerms;
            existingCustomer.CustomerType = model.CustomerType;
            existingCustomer.WithHoldingVat = model.WithHoldingVat;
            existingCustomer.WithHoldingTax = model.WithHoldingTax;
            existingCustomer.ZipCode = model.ZipCode;
            existingCustomer.VatType = model.VatType;
            existingCustomer.Type = model.Type;
            existingCustomer.DefaultCosDiscountPercent = model.DefaultCosDiscountPercent;

            if (_db.ChangeTracker.HasChanges())
            {
                existingCustomer.EditedBy = model.EditedBy;
                existingCustomer.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("No data changes!");
            }
        }

        public override async Task<Customer?> GetAsync(Expression<Func<Customer, bool>> filter, CancellationToken cancellationToken = default)
        {
            return await dbSet.Where(filter)
                    .FirstOrDefaultAsync(cancellationToken);
        }

        public override async Task<IEnumerable<Customer>> GetAllAsync(Expression<Func<Customer, bool>>? filter, CancellationToken cancellationToken = default)
        {
            IQueryable<Customer> query = dbSet;

            if (filter != null)
            {
                query = query.Where(filter);
            }

            return await query.ToListAsync(cancellationToken);
        }
        public async Task<List<Customer>> SearchCustomersAsync(string term, int limit, CancellationToken cancellationToken)
        {
            var query = dbSet.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(term))
            {
                var s = term.ToLower();
                query = query.Where(c => c.CustomerName.ToLower().Contains(s) || c.CustomerCode.ToLower().Contains(s));
            }

            return await query
                .OrderBy(c => c.CustomerName)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<object>> SearchCustomersDtoAsync(string term, int limit, CancellationToken cancellationToken)
        {
            var customers = await SearchCustomersAsync(term, limit, cancellationToken);
            var ids = customers.Select(c => c.CustomerId).ToList();

            var customerIdsWithPrincipals = await _db.Set<Principal>()
                .Where(p => ids.Contains(p.CustomerId))
                .Select(p => p.CustomerId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var principalLookup = customerIdsWithPrincipals.ToHashSet();

            return customers.Select(c => (object)new
            {
                value = c.CustomerId,
                name = c.CustomerName,
                vatType = c.VatType,
                isUndoc = c.Type,
                address = c.CustomerAddress,
                tinNo = c.CustomerTin,
                terms = c.CustomerTerms,
                businessStyle = c.BusinessStyle ?? "-",
                withholdingTax = c.WithHoldingTax,
                withholdingVat = c.WithHoldingVat,
                hasPrincipal = principalLookup.Contains(c.CustomerId)
            }).ToList();
        }
    }
}
