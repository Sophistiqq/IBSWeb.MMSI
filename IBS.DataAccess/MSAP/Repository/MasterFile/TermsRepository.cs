using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.MasterFile
{
    public class TermsRepository(MsapDbContext db) : Repository<Terms>(db), ITermsRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task UpdateAsync(Terms model, CancellationToken cancellationToken = default)
        {
            var existingTerms = await _db.Terms
                .FirstOrDefaultAsync(x => x.TermsCode == model.TermsCode, cancellationToken)
                ?? throw new InvalidOperationException($"Terms with code '{model.TermsCode}' not found.");

            if (existingTerms.NumberOfDays == model.NumberOfDays
                && existingTerms.NumberOfMonths == model.NumberOfMonths)
            {
                return;
            }

            existingTerms.NumberOfDays = model.NumberOfDays;
            existingTerms.NumberOfMonths = model.NumberOfMonths;
            existingTerms.EditedBy = model.EditedBy;
            existingTerms.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetTermsListAsyncByCode(CancellationToken cancellationToken = default)
        {
            return await _db.Terms
                .AsNoTracking()
                .OrderBy(t => t.TermsCode)
                .Select(t => new SelectListItem
                {
                    Value = t.TermsCode,
                    Text = t.TermsCode
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<DateOnly> ComputeDueDateAsync(string terms, DateOnly transactionDate, CancellationToken cancellationToken = default)
        {
            var getTerms = await _db.Terms
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TermsCode == terms, cancellationToken)
                ?? throw new ArgumentException($"Payment terms '{terms}' not found.");

            var dueDate = transactionDate
                .AddMonths(getTerms.NumberOfMonths)
                .AddDays(getTerms.NumberOfDays);

            if (!terms.Contains('M'))
            {
                return dueDate;
            }

            return dueDate.AddDays(-transactionDate.Day);
        }
    }
}
