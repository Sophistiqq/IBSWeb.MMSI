using System.Diagnostics;
using IBS.DataAccess.MSAP.Repository.IRepository;
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
        IUnitOfWork unitOfWork,
        IAccessControlService accessControl,
        DashboardService dashboardService,
        IWebHostEnvironment webHostEnvironment) : Controller
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

            await dashboardService.SetAccessAsync(model, userId, accessControl);
            if (!model.ShowDashboard)
            {
                return View(model);
            }

            await dashboardService.LoadAsync(model, ct);
            return View(model);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        public async Task<IActionResult> Maintenance(CancellationToken ct)
        {
            if (await unitOfWork.AppSetting.GetAllQuery(s => s.SettingKey == "MaintenanceMode")
                    .Select(s => s.Value == "true")
                    .FirstOrDefaultAsync(ct))
            {
                return View("Maintenance");
            }

            return RedirectToAction(nameof(Index));
        }

        [Authorize]
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GlobalSearch(string? q, CancellationToken ct)
        {
            var term = q?.Trim();
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2 || term.Length > 100)
            {
                return Json(Array.Empty<object>());
            }

            var userId = userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var s = term.ToLower();
            var normalizedId = term.TrimStart('#');
            if (normalizedId.StartsWith("VS-", StringComparison.OrdinalIgnoreCase))
            {
                normalizedId = normalizedId[3..];
            }
            var hasNumericId = int.TryParse(normalizedId, out var parsedId);

            var results = new List<object>();

            var canViewOperations = await accessControl.HasAccessAsync(userId, ProcedureEnum.ViewOperations);
            var canViewBillings = await accessControl.HasAccessAsync(userId, ProcedureEnum.ViewBillings);
            var canViewCollections = await accessControl.HasAccessAsync(userId, ProcedureEnum.ViewCollections);

            if (canViewOperations)
            {
                var schedules = await unitOfWork.VesselSchedule.GetAllQuery()
                    .AsNoTracking()
                    .Where(v =>
                        (hasNumericId && v.VesselScheduleId == parsedId)
                        || (v.Vessel != null && v.Vessel.VesselName.ToLower().Contains(s))
                        || (v.Customer != null && v.Customer.CustomerName.ToLower().Contains(s))
                        || (v.VoyageNumber != null && v.VoyageNumber.ToLower().Contains(s))
                        || (v.CustomerOrderSlip != null && v.CustomerOrderSlip.CustomerOrderSlipNumber.ToLower().Contains(s)))
                    .OrderByDescending(v => v.PlannedStart)
                    .Take(5)
                    .Select(v => new
                    {
                        v.VesselScheduleId,
                        VesselName = v.Vessel != null ? v.Vessel.VesselName : "Vessel pending",
                        CustomerName = v.Customer != null ? v.Customer.CustomerName : "Customer pending",
                        v.PlannedStart,
                        v.Status
                    })
                    .ToListAsync(ct);

                foreach (var v in schedules)
                {
                    results.Add(new
                    {
                        section = "Vessel Schedules",
                        icon = "calendar_month",
                        label = $"Booking #{v.VesselScheduleId} · {v.VesselName}",
                        sub = $"{v.CustomerName} · {v.PlannedStart:dd MMM yyyy} · {MsapConstants.VesselScheduleStatus.GetDisplayName(v.Status)}",
                        url = Url.Action("Details", "VesselSchedule", new { area = "MSAP", id = v.VesselScheduleId })
                    });
                }

                var cosList = await unitOfWork.CustomerOrderSlip.GetAllQuery()
                    .AsNoTracking()
                    .Where(c =>
                        c.CustomerOrderSlipNumber.ToLower().Contains(s)
                        || (c.COSNumber != null && c.COSNumber.ToLower().Contains(s))
                        || (c.Customer != null && c.Customer.CustomerName.ToLower().Contains(s))
                        || (c.Vessel != null && c.Vessel.VesselName.ToLower().Contains(s))
                        || (c.VoyageNumber != null && c.VoyageNumber.ToLower().Contains(s)))
                    .OrderByDescending(c => c.Date)
                    .Take(5)
                    .Select(c => new
                    {
                        c.CustomerOrderSlipId,
                        c.CustomerOrderSlipNumber,
                        VesselName = c.Vessel != null ? c.Vessel.VesselName : "Vessel pending",
                        CustomerName = c.Customer != null ? c.Customer.CustomerName : "Customer pending",
                        c.Date,
                        c.Status
                    })
                    .ToListAsync(ct);

                foreach (var c in cosList)
                {
                    results.Add(new
                    {
                        section = "COS Records",
                        icon = "assignment",
                        label = $"{c.CustomerOrderSlipNumber} · {c.VesselName}",
                        sub = $"{c.CustomerName} · {c.Date:dd MMM yyyy} · {MsapConstants.CustomerOrderSlipStatus.GetDisplayName(c.Status)}",
                        url = Url.Action("Details", "CustomerOrderSlip", new { area = "MSAP", id = c.CustomerOrderSlipId })
                    });
                }

                var tickets = await unitOfWork.DispatchTicket.GetAllQuery()
                    .AsNoTracking()
                    .Where(t =>
                        t.Status != MsapConstants.DispatchTicketStatus.Deleted
                        && (t.DispatchNumber.ToLower().Contains(s)
                            || (t.COSNumber != null && t.COSNumber.ToLower().Contains(s))
                            || (t.Vessel != null && t.Vessel.VesselName.ToLower().Contains(s))
                            || (t.Customer != null && t.Customer.CustomerName.ToLower().Contains(s))
                            || (t.Tugboat != null && t.Tugboat.TugboatName.ToLower().Contains(s))))
                    .OrderByDescending(t => t.Date)
                    .Take(5)
                    .Select(t => new
                    {
                        t.DispatchTicketId,
                        t.DispatchNumber,
                        VesselName = t.Vessel != null ? t.Vessel.VesselName : "Vessel pending",
                        TugboatName = t.Tugboat != null ? t.Tugboat.TugboatName : "Tug pending",
                        CustomerName = t.Customer != null ? t.Customer.CustomerName : "Customer pending",
                        t.Status
                    })
                    .ToListAsync(ct);

                foreach (var t in tickets)
                {
                    results.Add(new
                    {
                        section = "Dispatch Tickets",
                        icon = "confirmation_number",
                        label = $"Ticket #{t.DispatchNumber} · {t.VesselName}",
                        sub = $"{t.TugboatName} · {t.CustomerName} · {t.Status}",
                        url = Url.Action("Preview", "DispatchTicket", new { area = "MSAP", id = t.DispatchTicketId })
                    });
                }
            }

            if (canViewBillings)
            {
                var billings = await unitOfWork.Billing.GetAllQuery()
                    .AsNoTracking()
                    .Where(b =>
                        b.MsapBillingNumber.ToLower().Contains(s)
                        || (b.COSNumber != null && b.COSNumber.ToLower().Contains(s))
                        || (b.Customer != null && b.Customer.CustomerName.ToLower().Contains(s))
                        || (b.Vessel != null && b.Vessel.VesselName.ToLower().Contains(s)))
                    .OrderByDescending(b => b.Date)
                    .Take(5)
                    .Select(b => new
                    {
                        b.MsapBillingId,
                        b.MsapBillingNumber,
                        CustomerName = b.Customer != null ? b.Customer.CustomerName : "Customer pending",
                        VesselName = b.Vessel != null ? b.Vessel.VesselName : "Vessel pending",
                        b.Amount,
                        b.Status
                    })
                    .ToListAsync(ct);

                foreach (var b in billings)
                {
                    var billStatus = b.Status switch
                    {
                        MsapConstants.BillingStatus.ForPosting => "For review",
                        MsapConstants.BillingStatus.Reviewed => "For approval",
                        _ => b.Status
                    };
                    results.Add(new
                    {
                        section = "Billing",
                        icon = "receipt_long",
                        label = $"Bill #{b.MsapBillingNumber} · {b.CustomerName}",
                        sub = $"{b.VesselName} · ₱{b.Amount:N2} · {billStatus}",
                        url = Url.Action("Review", "Billing", new { area = "MSAP", id = b.MsapBillingId })
                    });
                }
            }

            if (canViewCollections)
            {
                var collections = await unitOfWork.Collection.GetAllQuery()
                    .AsNoTracking()
                    .Where(c =>
                        c.MsapCollectionNumber.ToLower().Contains(s)
                        || (c.Customer != null && c.Customer.CustomerName.ToLower().Contains(s))
                        || (c.CheckNumber != null && c.CheckNumber.ToLower().Contains(s))
                        || (c.ReferenceNo != null && c.ReferenceNo.ToLower().Contains(s)))
                    .OrderByDescending(c => c.Date)
                    .Take(5)
                    .Select(c => new
                    {
                        c.MsapCollectionId,
                        c.MsapCollectionNumber,
                        CustomerName = c.Customer != null ? c.Customer.CustomerName : "Customer pending",
                        c.Date,
                        c.Amount,
                        c.VoidedDate,
                        c.CanceledDate,
                        c.ClearedDate,
                        c.DepositDate,
                        c.ReturnedDate
                    })
                    .ToListAsync(ct);

                foreach (var c in collections)
                {
                    var paymentStatus = (c.VoidedDate.HasValue || c.CanceledDate.HasValue) ? "Voided"
                        : c.ClearedDate.HasValue ? "Cleared"
                        : c.DepositDate.HasValue ? (c.ReturnedDate.HasValue ? "Redeposited" : "Deposited")
                        : c.ReturnedDate.HasValue ? "Returned" : "For Deposit";
                    results.Add(new
                    {
                        section = "Collections",
                        icon = "payments",
                        label = $"Receipt #{c.MsapCollectionNumber} · {c.CustomerName}",
                        sub = $"{c.Date:dd MMM yyyy} · ₱{c.Amount:N2} · {paymentStatus}",
                        url = Url.Action("Preview", "Collection", new { area = "MSAP", id = c.MsapCollectionId })
                    });
                }
            }

            return Json(results);
        }

        [Authorize]
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> WhatsNew(CancellationToken ct)
        {
            var releases = await MsapChangelogParser.ReadReleasesAsync(webHostEnvironment.ContentRootPath, ct);
            return Json(new { releases });
        }
    }
}
