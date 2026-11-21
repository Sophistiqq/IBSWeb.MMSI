using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.MasterFile;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.DataAccess.MSAP.Repository.MasterFile.IRepository
{
    public interface ITermsRepository : IRepository<Terms>
    {
        Task UpdateAsync(Terms model, CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetTermsListAsyncByCode(CancellationToken cancellationToken = default);

        Task<DateOnly> ComputeDueDateAsync(string terms, DateOnly transactionDate, CancellationToken cancellationToken = default);
    }
}
