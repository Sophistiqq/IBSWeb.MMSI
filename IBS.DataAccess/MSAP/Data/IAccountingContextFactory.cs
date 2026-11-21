using IBS.DataAccess.Data;
using IBS.DataAccess.Repository.IRepository;

namespace IBS.DataAccess.MSAP.Data
{
    public interface IAccountingContextFactory
    {
        Task<ApplicationDbContext> OpenAccountingContextAsync(MsapDbContext msapDb, CancellationToken ct = default);

        Task<(ApplicationDbContext Context, IUnitOfWork UnitOfWork)> OpenAccountingScopeAsync(MsapDbContext msapDb, CancellationToken ct = default);

        Task<MsapDbContext> OpenMsapContextAsync(ApplicationDbContext accountingDb, CancellationToken ct = default);
    }
}
