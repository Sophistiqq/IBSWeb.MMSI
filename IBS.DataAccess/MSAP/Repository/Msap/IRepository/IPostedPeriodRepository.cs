using IBS.Models.MSAP.MasterFile;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IPostedPeriodRepository
    {
        Task<bool> IsMonthClosedAsync(int year, int month, CancellationToken cancellationToken = default);

        Task<IEnumerable<MsapPostedPeriod>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<MsapPostedPeriod?> GetByYearMonthAsync(int year, int month, CancellationToken cancellationToken = default);

        Task AddAsync(MsapPostedPeriod period, CancellationToken cancellationToken = default);

        Task UpdateAsync(MsapPostedPeriod period, CancellationToken cancellationToken = default);
    }
}
