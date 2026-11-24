using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Helpers;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAPAdmin.Controllers
{
    [Area("MSAPAdmin")]
    [Authorize(Policy = MsapRoles.AdminPolicy)]
    [RequireAnyAccess("Access denied.", ProcedureEnum.ManagePostedPeriod)]
    public class PostedPeriodController(
        IUnitOfWork unitOfWork,
        ILogger<PostedPeriodController> logger)
        : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var periods = await unitOfWork.PostedPeriod.GetAllAsync(cancellationToken);
            return View(periods);
        }

        [HttpGet]
        public async Task<IActionResult> GetPeriodSummary(int year, int month, CancellationToken cancellationToken)
        {
            var start = new DateOnly(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            var tickets = await unitOfWork.DispatchTicket.CountAsync(
                t => (t.DateLeft ?? t.Date) >= start && (t.DateLeft ?? t.Date) <= end &&
                     (t.Status == MsapConstants.DispatchTicketStatus.ForBilling || t.Status == MsapConstants.DispatchTicketStatus.Billed),
                cancellationToken);

            var billings = await unitOfWork.Billing.CountAsync(
                b => b.Date >= start && b.Date <= end &&
                     (b.Status == MsapConstants.BillingStatus.ForCollection || b.Status == MsapConstants.BillingStatus.Collected),
                cancellationToken);

            var draftBillings = await unitOfWork.Billing.CountAsync(
                b => b.Date >= start && b.Date <= end
                    && (b.Status == MsapConstants.BillingStatus.ForPosting || b.Status == MsapConstants.BillingStatus.Reviewed),
                cancellationToken);

            var collections = await unitOfWork.Collection.CountAsync(
                c => c.Date >= start && c.Date <= end && c.VoidedDate == null && c.CanceledDate == null,
                cancellationToken);

            return Json(new
            {
                tickets,
                billings,
                draftBillings,
                collections
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var username = User.Identity?.Name ?? "System";
                var period = await unitOfWork.PostedPeriod.GetByYearMonthAsync(year, month, cancellationToken);
                if (period == null)
                {
                    period = new MsapPostedPeriod
                    {
                        Year = year,
                        Month = month,
                        IsClosed = true,
                        ClosedBy = username,
                        ClosedDate = DateTimeHelper.GetCurrentPhilippineTime()
                    };
                    await unitOfWork.PostedPeriod.AddAsync(period, cancellationToken);
                }
                else
                {
                    period.IsClosed = true;
                    period.ClosedBy = username;
                    period.ClosedDate = DateTimeHelper.GetCurrentPhilippineTime();
                    await unitOfWork.PostedPeriod.UpdateAsync(period, cancellationToken);
                }

                await unitOfWork.PostedPeriod.FinalizePeriodRecordsAsync(year, month, cancellationToken);

                var audit = new AuditTrail(username, $"Closed posting period {new DateOnly(year, month, 1):MMMM yyyy}", "Posting Period", period.Id);
                await unitOfWork.AuditTrail.AddAsync(audit, cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);
                TempData["success"] = $"{new DateOnly(year, month, 1):MMMM yyyy} closed successfully.";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to close period {Year}-{Month}", year, month);
                TempData["error"] = $"Failed to close period: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Open(int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var username = User.Identity?.Name ?? "System";
                var period = await unitOfWork.PostedPeriod.GetByYearMonthAsync(year, month, cancellationToken);
                if (period == null)
                {
                    TempData["error"] = $"Period {new DateOnly(year, month, 1):MMMM yyyy} not found.";
                    return RedirectToAction(nameof(Index));
                }

                period.IsClosed = false;
                period.OpenedBy = username;
                period.OpenedDate = DateTimeHelper.GetCurrentPhilippineTime();
                await unitOfWork.PostedPeriod.UpdateAsync(period, cancellationToken);
                var audit = new AuditTrail(username, $"Opened posting period {new DateOnly(year, month, 1):MMMM yyyy}", "Posting Period", period.Id);
                await unitOfWork.AuditTrail.AddAsync(audit, cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);

                TempData["success"] = $"{new DateOnly(year, month, 1):MMMM yyyy} opened successfully.";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to open period {Year}-{Month}", year, month);
                TempData["error"] = $"Failed to open period: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
