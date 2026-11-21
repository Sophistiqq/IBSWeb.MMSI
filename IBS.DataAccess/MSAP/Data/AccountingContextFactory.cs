using IBS.DataAccess.Data;
using IBS.DataAccess.Repository.IRepository;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IBS.DataAccess.MSAP.Data
{
    public sealed class AccountingContextFactory : IAccountingContextFactory
    {
        public static AccountingContextFactory Default { get; } = new();

        public async Task<ApplicationDbContext> OpenAccountingContextAsync(MsapDbContext msapDb, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(msapDb);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(msapDb.Database.GetDbConnection())
                .Options;
            var accounting = new ApplicationDbContext(options);

            try
            {
                if (msapDb.Database.CurrentTransaction != null)
                {
                    await accounting.Database.UseTransactionAsync(msapDb.Database.CurrentTransaction.GetDbTransaction(), ct);
                }

                return accounting;
            }
            catch
            {
                await accounting.DisposeAsync();
                throw;
            }
        }

        public async Task<(ApplicationDbContext Context, IUnitOfWork UnitOfWork)> OpenAccountingScopeAsync(MsapDbContext msapDb, CancellationToken ct = default)
        {
            var accounting = await OpenAccountingContextAsync(msapDb, ct);
            return (accounting, new IBS.DataAccess.Repository.UnitOfWork(accounting));
        }

        public async Task<MsapDbContext> OpenMsapContextAsync(ApplicationDbContext accountingDb, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(accountingDb);

            var options = new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(accountingDb.Database.GetDbConnection())
                .Options;
            var msap = new MsapDbContext(options);

            try
            {
                if (accountingDb.Database.CurrentTransaction != null)
                {
                    await msap.Database.UseTransactionAsync(accountingDb.Database.CurrentTransaction.GetDbTransaction(), ct);
                }

                return msap;
            }
            catch
            {
                await msap.DisposeAsync();
                throw;
            }
        }
    }
}
