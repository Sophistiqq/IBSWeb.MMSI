using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;
using IBS.Utility.MSAP.Helpers;

namespace IBS.Services.MSAP
{
    public interface IVesselScheduleService
    {
        Task<ServiceResult<VesselScheduleBoardViewModel>> GetCalendarAsync(VesselScheduleBoardViewModel filters, CancellationToken ct = default);
        Task<ServiceResult<int>> CreateAsync(VesselSchedule model, string username, CancellationToken ct = default, bool allowConflicts = false);
        Task<ServiceResult> UpdateAsync(VesselSchedule model, string username, CancellationToken ct = default, bool allowConflicts = false);
        Task<ServiceResult> DeleteAsync(int id, string username, CancellationToken ct = default);
        Task<ServiceResult<int>> StartUrgentEntryAsync(int id, string username, CancellationToken ct = default);
        Task<ServiceResult<int>> ConfirmAsync(int id, DateTime reviewedAt, string username, CancellationToken ct = default, bool allowConflicts = false, string? userId = null);
        Task<ServiceResult> ChangeStatusAsync(int id, string status, string username, CancellationToken ct = default);
        Task<VesselSchedule?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<IEnumerable<VesselSchedule>> GetSchedulesAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
        Task<List<ScheduleConflict>> CheckConflictsAsync(VesselSchedule schedule, CancellationToken ct = default,
            IEnumerable<VesselSchedule>? candidates = null, IReadOnlyDictionary<int, string>? tugboatNames = null);
    }

    public class ScheduleConflict
    {
        public string Type { get; set; } = ""; // "Vessel", "Terminal", or "Tugboat"
        public string Message { get; set; } = "";
        public int ConflictingScheduleId { get; set; }
        public string? ConflictingVessel { get; set; }
        public DateTime ConflictStart { get; set; }
        public DateTime ConflictEnd { get; set; }
    }
}
