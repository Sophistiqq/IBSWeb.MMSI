using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IUserAccessRepository : IRepository<UserAccess>
    {
        Task SaveAsync(CancellationToken cancellationToken);

        Task<List<string>> GetUserIdsWithAccessAsync(ProcedureEnum procedure, CancellationToken cancellationToken = default);
    }
}
