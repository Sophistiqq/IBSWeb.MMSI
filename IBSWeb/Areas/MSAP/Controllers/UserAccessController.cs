using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ApplicationUser = IBS.Models.ApplicationUser;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [Authorize(Policy = MsapRoles.AdminPolicy)]
    public class UserAccessController(
        IUserAccessService userAccessService,
        UserManager<ApplicationUser> userManager)
        : Controller
    {
        public Task<IActionResult> Index(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IActionResult>(View());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetUserAccessList([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var queried = await userAccessService.GetAllAsync(cancellationToken);

                // Global search
                if (!string.IsNullOrEmpty(parameters.Search.Value))
                {
                    var searchValue = parameters.Search.Value.ToLower();
                    queried = queried.Where(ua =>
                        (ua.UserId.ToLower().Contains(searchValue)) ||
                        ua.DisplayName.ToLower().Contains(searchValue)
                    ).ToList();
                }

                var totalRecords = queried.Count();
                var pagedData = queried
                    .Skip(parameters.Start)
                    .Take(parameters.Length)
                    .ToList();

                return Json(new
                {
                    draw = parameters.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = totalRecords,
                    data = pagedData
                });
            }
            catch (Exception)
            {
                return Json(new { error = "Internal server error" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
        {
            var model = await userAccessService.PopulateUsersAsync(null, cancellationToken);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("UserId,CanCreateCustomerOrderSlip,CanEditCustomerOrderSlip,CanApproveSchedule,CanCreateDispatchTicket,CanEditDispatchTicket,CanViewRates,CanManageRates,CanApproveRateUpload,CanCreateBilling,CanEditBilling,CanDeleteBilling,CanApproveFinalBilling,CanCreateCollection,CanAccessTreasury,CanManageOtherTugCharges,CanConfirmOtherTugCharges,CanManageMaritimeMasterFile,CanViewMaritimeReport")] UserAccess model, CancellationToken cancellationToken = default)
        {
            ApplicationUser? actor = await userManager.GetUserAsync(User);
            if (actor == null)
            {
                return Challenge();
            }
            if (!ModelState.IsValid)
            {
                await userAccessService.PopulateUsersAsync(model, cancellationToken);
                return View(model);
            }
            var result = await userAccessService.CreateAsync(model, actor.UserName!, cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["error"] = result.Message;
            await userAccessService.PopulateUsersAsync(model, cancellationToken);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
        {
            var model = await userAccessService.GetByIdAsync(id, cancellationToken);

            if (model == null)
            {
                TempData["info"] = "User access not found.";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([Bind("Id,UserId,CanCreateCustomerOrderSlip,CanEditCustomerOrderSlip,CanApproveSchedule,CanCreateDispatchTicket,CanEditDispatchTicket,CanViewRates,CanManageRates,CanApproveRateUpload,CanCreateBilling,CanEditBilling,CanDeleteBilling,CanApproveFinalBilling,CanCreateCollection,CanAccessTreasury,CanManageOtherTugCharges,CanConfirmOtherTugCharges,CanManageMaritimeMasterFile,CanViewMaritimeReport")] UserAccess model, CancellationToken cancellationToken = default)
        {
            ApplicationUser? actor = await userManager.GetUserAsync(User);
            if (actor == null)
            {
                return Challenge();
            }
            if (!ModelState.IsValid)
            {
                await userAccessService.PopulateUsersAsync(model, cancellationToken);
                var existing = await userAccessService.GetByIdAsync(model.Id, cancellationToken);
                model.DisplayName = existing?.DisplayName ?? "Name unavailable";
                return View(model);
            }
            var result = await userAccessService.UpdateAsync(model, actor.UserName!, cancellationToken);

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["error"] = result.Message;
            await userAccessService.PopulateUsersAsync(model, cancellationToken);
            return View(model);
        }
    }
}
