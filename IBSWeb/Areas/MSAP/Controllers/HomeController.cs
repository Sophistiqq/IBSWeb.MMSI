using System.Diagnostics;
using System.Text.Json;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApplicationUser = IBS.Models.ApplicationUser;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    public class HomeController(
        UserManager<ApplicationUser> userManager,
        MsapDbContext dbContext,
        IAccessControlService accessControl,
        IVesselScheduleService scheduleService,
        ILogger<HomeController> logger) : Controller
    {
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var model = new DashboardCountViewModel { UpdatedAt = DateTimeHelper.GetCurrentPhilippineTime() };
            model.ShowDashboard = User.Identity?.IsAuthenticated == true;
            if (!model.ShowDashboard)
            {
                return View(model);
            }

            var userId = userManager.GetUserId(User);
            if (userId == null)
            {
                return Challenge();
            }

            model.CanViewDispatch = await accessControl.HasAnyAccessAsync(userId,
                ProcedureEnum.CreateDispatchTicket, ProcedureEnum.EditDispatchTicket, ProcedureEnum.DeleteDispatchTicket);
            model.CanViewBilling = await accessControl.HasAnyAccessAsync(userId,
                ProcedureEnum.CreateBilling, ProcedureEnum.EditBilling, ProcedureEnum.DeleteBilling, ProcedureEnum.ReverseBilling);
            model.CanCreateDispatch = await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateDispatchTicket);
            model.CanCreateBilling = await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateBilling);
            model.CanCreateCollection = await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateCollection);
            model.CanCreateCustomerOrderSlip = await accessControl.HasAccessAsync(userId, ProcedureEnum.CreateCustomerOrderSlip);
            model.CanViewFinance = model.CanViewBilling || model.CanCreateCollection
                || await accessControl.HasAccessAsync(userId, ProcedureEnum.ViewMaritimeReport);

            var today = DateOnly.FromDateTime(model.UpdatedAt);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            try
            {
                if (model.CanViewDispatch)
                {
                    model.DispatchCounts = await dbContext.MsapDispatchTickets.AsNoTracking()
                        .Where(d => d.Status == MsapConstants.DispatchTicketStatus.ForTariff
                            || d.Status == MsapConstants.DispatchTicketStatus.ForApproval
                            || d.Status == MsapConstants.DispatchTicketStatus.ForBilling)
                        .GroupBy(d => d.Status).Select(g => new { Status = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(g => g.Status, g => g.Count, ct);
                }
                var bills = dbContext.MsapBillings.AsNoTracking()
                    .Where(b => b.CanceledDate == null && b.VoidedDate == null);
                if (model.CanViewBilling)
                {
                    model.ForPosting = await bills.CountAsync(b => b.Status == MsapConstants.BillingStatus.ForPosting, ct);
                }

                if (model.CanViewFinance)
                {
                    var posted = bills.Where(b => b.Status == MsapConstants.BillingStatus.ForCollection || b.Status == MsapConstants.BillingStatus.Collected);
                    model.BilledMonth = await posted.Where(b => b.Date >= monthStart && b.Date <= today)
                        .SumAsync(b => (decimal?)b.Amount, ct) ?? 0m;
                    var activity = await dbContext.MsapDispatchTickets.AsNoTracking()
                        .Where(d => d.Date >= monthStart && d.Date <= today
                            && d.Status != MsapConstants.DispatchTicketStatus.Deleted && d.Status != "For Posting"
                            && d.Status != "Incomplete" && d.Status != "Draft" && d.Status != "Requested")
                        .GroupBy(d => 1).Select(g => new { Dispatches = g.Count(), Vessels = g.Select(d => d.VesselId).Distinct().Count() })
                        .FirstOrDefaultAsync(ct);
                    model.DispatchesMonth = activity?.Dispatches ?? 0;
                    model.VesselsMonth = activity?.Vessels ?? 0;
                    model.ReceivedMonth = await dbContext.MsapCollections.AsNoTracking()
                        .Where(c => c.Date >= monthStart && c.Date <= today && c.CanceledDate == null && c.VoidedDate == null)
                        .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to load dashboard workflow and financial data");
                model.DataError = "Workflow and financial data could not be loaded. Refresh to try again.";
            }

            try
            {
                var day = model.UpdatedAt.Date;
                var window = (await scheduleService.GetSchedulesAsync(day, model.UpdatedAt.AddDays(7), ct))
                    .Where(s => s.Status != MsapConstants.VesselScheduleStatus.Cancelled).ToList();
                var schedules = window.Where(s => s.PlannedStart < day.AddDays(1) && s.PlannedEnd > day).ToList();
                model.UpcomingSchedules = window
                    .Where(s => s.PlannedStart >= model.UpdatedAt && s.PlannedStart < model.UpdatedAt.AddDays(7)
                        && s.Status != MsapConstants.VesselScheduleStatus.Completed)
                    .OrderBy(s => s.PlannedStart).ThenBy(s => s.VesselScheduleId).Take(5).ToList();
                var names = await dbContext.MsapTugboats.AsNoTracking().ToDictionaryAsync(t => t.TugboatId, t => t.TugboatName, ct);
                foreach (var schedule in schedules.Concat(model.UpcomingSchedules).DistinctBy(s => s.VesselScheduleId))
                {
                    model.AssignedTugs[schedule.VesselScheduleId] =
                        (string.IsNullOrEmpty(schedule.AssignedTugboatIds) ? [] : JsonSerializer.Deserialize<List<int>>(schedule.AssignedTugboatIds) ?? [])
                        .Distinct().Count(names.ContainsKey);
                }

                // ponytail: pairwise checks for today's plans; index resource intervals if daily volume grows.
                foreach (var schedule in schedules.Where(s => s.Status != MsapConstants.VesselScheduleStatus.Completed))
                {
                    var conflicts = await scheduleService.CheckConflictsAsync(schedule, ct, schedules, names);
                    if (conflicts.Any(c => c.ConflictStart < day.AddDays(1) && c.ConflictEnd > day))
                    {
                        model.ConflictingScheduleIds.Add(schedule.VesselScheduleId);
                    }
                }
                model.ScheduleCount = schedules.Count;
                model.Schedules = schedules.OrderBy(s => s.Status == MsapConstants.VesselScheduleStatus.Completed)
                    .ThenBy(s => s.PlannedStart).Take(6).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to load dashboard vessel schedules");
                model.ScheduleError = "Vessel schedules could not be loaded. Open the calendar or refresh to try again.";
            }
            return View(model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        public async Task<IActionResult> Maintenance()
        {
            if (await dbContext.AppSettings
                    .Where(s => s.SettingKey == "MaintenanceMode")
                    .Select(s => s.Value == "true")
                    .FirstOrDefaultAsync())
            {
                return View("Maintenance");
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
