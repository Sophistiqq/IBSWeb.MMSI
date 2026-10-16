using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IVesselScheduleRepository : IRepository<VesselSchedule>
    {
        Task<IEnumerable<VesselSchedule>> GetSchedulesWithDetailsAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
        Task<VesselSchedule?> GetScheduleWithDetailsAsync(int id, CancellationToken ct = default);
        Task<VesselSchedule?> GetForUpdateAsync(int id, CancellationToken ct = default);
        void RemoveServiceEntries(IEnumerable<VesselScheduleServiceEntry> entries);
    }
}
