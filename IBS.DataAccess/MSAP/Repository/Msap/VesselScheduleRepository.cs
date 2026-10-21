using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using Microsoft.EntityFrameworkCore;

namespace IBS.DataAccess.MSAP.Repository.Msap
{
    public class VesselScheduleRepository(MsapDbContext db) : Repository<VesselSchedule>(db), IVesselScheduleRepository
    {
        private readonly MsapDbContext _db = db;

        public async Task<VesselSchedule?> GetForUpdateAsync(int id, CancellationToken ct = default)
        {
            // Serialize confirmation, revisions and deletion so one booking creates only one order.
            var schedule = await _db.MsapVesselSchedules
                .FromSqlInterpolated($"SELECT * FROM msap.mmsi_vessel_schedules WHERE vessel_schedule_id = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (schedule != null)
            {
                await _db.Entry(schedule).ReloadAsync(ct);
                await _db.Entry(schedule).Collection(s => s.ServiceEntries).LoadAsync(ct);
            }
            return schedule;
        }

        public async Task<VesselSchedule?> GetScheduleWithDetailsAsync(int id, CancellationToken ct = default)
        {
            return await _db.MsapVesselSchedules
                .Include(s => s.Customer)
                .Include(s => s.CustomerOrderSlip)
                .Include(s => s.Vessel)
                .Include(s => s.Port)
                .Include(s => s.Terminal)
                .Include(s => s.ApprovedByUser)
                .Include(s => s.ServiceEntries)
                    .ThenInclude(e => e.Tugboat)
                .Include(s => s.ServiceEntries)
                    .ThenInclude(e => e.Service)
                .FirstOrDefaultAsync(s => s.VesselScheduleId == id, ct);
        }

        public async Task<IEnumerable<VesselSchedule>> GetSchedulesWithDetailsAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var query = _db.MsapVesselSchedules
                .Include(s => s.Customer)
                .Include(s => s.CustomerOrderSlip)
                .Include(s => s.Vessel)
                .Include(s => s.Port)
                .Include(s => s.Terminal)
                .Include(s => s.ApprovedByUser)
                .Include(s => s.ServiceEntries)
                    .ThenInclude(e => e.Tugboat)
                .Include(s => s.ServiceEntries)
                    .ThenInclude(e => e.Service)
                .AsQueryable();

            if (from.HasValue)
                query = query.Where(s => s.PlannedEnd > from.Value);

            if (to.HasValue)
                query = query.Where(s => s.PlannedStart < to.Value);

            return await query
                .OrderBy(s => s.PlannedStart)
                .ToListAsync(ct);
        }

        public void RemoveServiceEntries(IEnumerable<VesselScheduleServiceEntry> entries)
        {
            _db.MsapVesselScheduleServiceEntries.RemoveRange(entries);
        }
    }
}
