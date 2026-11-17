using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ApplicationUser = IBS.Models.ApplicationUser;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess("Access denied. You don't have permission to manage Representatives.",
        ProcedureEnum.ManageCustomerMasterFile, ProcedureEnum.ManagePrincipalMasterFile, ProcedureEnum.ManageRates)]
    public class RepresentativeController(
        MsapDbContext db,
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager)
        : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetRepresentativeList(CancellationToken cancellationToken)
        {
            var list = await db.MsapRepresentatives.AsNoTracking()
                .Include(r => r.Customer)
                .Include(r => r.Principal)
                .OrderBy(r => r.RepresentativeName)
                .ThenBy(r => r.Category)
                .Select(r => new
                {
                    representativeId = r.RepresentativeId,
                    representativeName = r.RepresentativeName,
                    category = r.Category,
                    customerName = r.Customer != null ? r.Customer.CustomerName : "All customers",
                    principalName = r.Principal != null ? r.Principal.PrincipalName : null,
                    rateMode = r.RateMode,
                    rateValue = r.RateValue,
                    calculationBase = r.CalculationBase,
                    isActive = r.IsActive
                })
                .ToListAsync(cancellationToken);

            return Json(new { data = list });
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            await PopulateSelectListsAsync(null, cancellationToken);
            return View("Form", new MsapRepresentative());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("RepresentativeName,Category,CustomerId,PrincipalId,RateMode,RateValue,CalculationBase,Address,Tin,VatType,TaxType,WithholdingTaxPercent,IsActive")]
            MsapRepresentative model,
            CancellationToken cancellationToken)
        {
            ValidateRepresentative(model);
            if (!ModelState.IsValid)
            {
                await PopulateSelectListsAsync(model.CustomerId, cancellationToken);
                return View("Form", model);
            }

            var username = userManager.GetUserName(User) ?? "System";
            model.RepresentativeName = model.RepresentativeName.Trim();
            (model.Address, model.Tin, model.WithholdingTaxPercent) = PaymentProfileHelper.Normalize(
                model.Address,
                model.Tin,
                model.TaxType,
                model.WithholdingTaxPercent);
            model.CreatedBy = username;
            model.CreatedDate = DateTimeHelper.GetCurrentPhilippineTime();
            await db.MsapRepresentatives.AddAsync(model, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await unitOfWork.AuditTrail.AddAsync(new AuditTrail(
                username,
                $"Created Representative {model.RepresentativeName} ({model.Category}, {model.RateMode} {model.RateValue}, base {model.CalculationBase})",
                "Representative", model.RepresentativeId, model.RepresentativeName), cancellationToken);
            await unitOfWork.SaveAsync(cancellationToken);

            TempData["success"] = "Representative created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var entity = await db.MsapRepresentatives.FindAsync([id], cancellationToken);
            if (entity == null)
            {
                return NotFound();
            }

            await PopulateSelectListsAsync(entity.CustomerId, cancellationToken);
            return View("Form", entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("RepresentativeId,RepresentativeName,Category,CustomerId,PrincipalId,RateMode,RateValue,CalculationBase,Address,Tin,VatType,TaxType,WithholdingTaxPercent,IsActive")]
            MsapRepresentative model,
            CancellationToken cancellationToken)
        {
            if (id != model.RepresentativeId)
            {
                return NotFound();
            }

            ValidateRepresentative(model);
            if (!ModelState.IsValid)
            {
                await PopulateSelectListsAsync(model.CustomerId, cancellationToken);
                return View("Form", model);
            }

            var existing = await db.MsapRepresentatives.FindAsync([id], cancellationToken);
            if (existing == null)
            {
                return NotFound();
            }

            var username = userManager.GetUserName(User) ?? "System";
            existing.RepresentativeName = model.RepresentativeName.Trim();
            existing.Category = model.Category;
            existing.CustomerId = model.CustomerId;
            existing.PrincipalId = model.PrincipalId;
            existing.RateMode = model.RateMode;
            existing.RateValue = model.RateValue;
            existing.CalculationBase = model.CalculationBase;
            (existing.Address, existing.Tin, existing.WithholdingTaxPercent) = PaymentProfileHelper.Normalize(
                model.Address,
                model.Tin,
                model.TaxType,
                model.WithholdingTaxPercent);
            existing.VatType = model.VatType;
            existing.TaxType = model.TaxType;
            existing.IsActive = model.IsActive;
            existing.EditedBy = username;
            existing.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

            await db.SaveChangesAsync(cancellationToken);
            await unitOfWork.AuditTrail.AddAsync(new AuditTrail(
                username,
                $"Updated Representative {existing.RepresentativeName} ({existing.Category}, {existing.RateMode} {existing.RateValue}, base {existing.CalculationBase}, active={existing.IsActive})",
                "Representative", existing.RepresentativeId, existing.RepresentativeName), cancellationToken);
            await unitOfWork.SaveAsync(cancellationToken);

            TempData["success"] = "Representative updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            var existing = await db.MsapRepresentatives.FindAsync([id], cancellationToken);
            if (existing == null)
            {
                return Json(new { success = false, message = "Representative not found." });
            }

            var inUse = await db.MsapBillings.AnyAsync(
                b => b.AgencyIncentiveRepresentativeId == id || b.AgentCommissionRepresentativeId == id,
                cancellationToken);
            if (inUse)
            {
                existing.IsActive = false;
                existing.EditedBy = userManager.GetUserName(User);
                existing.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();
                await db.SaveChangesAsync(cancellationToken);
                return Json(new { success = true, message = "Representative is linked to existing billings and was marked inactive." });
            }

            db.MsapRepresentatives.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            return Json(new { success = true, message = "Representative deleted successfully." });
        }

        [HttpGet]
        public async Task<JsonResult> GetPrincipalsByCustomer(int customerId, CancellationToken cancellationToken)
        {
            var principals = await db.MsapPrincipals.AsNoTracking()
                .Where(p => p.CustomerId == customerId && p.IsActive)
                .OrderBy(p => p.PrincipalName)
                .Select(p => new { value = p.PrincipalId, text = p.PrincipalName })
                .ToListAsync(cancellationToken);
            return Json(principals);
        }

        private void ValidateRepresentative(MsapRepresentative model)
        {
            if (model.Category is not (CommissionCalculator.CategoryAgencyIncentive or CommissionCalculator.CategoryAgentCommission))
            {
                ModelState.AddModelError(nameof(model.Category), "Invalid category.");
            }
            if (model.RateMode is not (CommissionCalculator.ModePercentage or CommissionCalculator.ModeFlat))
            {
                ModelState.AddModelError(nameof(model.RateMode), "Invalid rate mode.");
            }
            if (model.RateValue < 0 || (model.RateMode == CommissionCalculator.ModePercentage && model.RateValue > 100))
            {
                ModelState.AddModelError(nameof(model.RateValue), model.RateMode == CommissionCalculator.ModePercentage
                    ? "Percentage rate must be between 0 and 100."
                    : "Flat amount must be 0 or greater.");
            }
            if (model.CalculationBase is not (CommissionCalculator.BaseNetOfDiscount or CommissionCalculator.BaseNetOfVat
                or CommissionCalculator.BaseGross or CommissionCalculator.BaseBasicOnly or CommissionCalculator.BaseBafOnly))
            {
                ModelState.AddModelError(nameof(model.CalculationBase), "Invalid calculation base.");
            }
            if (!model.CustomerId.HasValue)
            {
                model.PrincipalId = null;
            }
            var profileError = PaymentProfileHelper.Validate(
                model,
                model.Address,
                model.Tin,
                model.VatType,
                model.TaxType,
                model.WithholdingTaxPercent,
                required: false,
                "representative");
            if (profileError != null)
            {
                ModelState.AddModelError(string.Empty, profileError);
            }
        }

        private async Task PopulateSelectListsAsync(int? customerId, CancellationToken cancellationToken)
        {
            ViewBag.Customers = await db.Customers.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .Select(c => new SelectListItem(c.CustomerName, c.CustomerId.ToString()))
                .ToListAsync(cancellationToken);

            ViewBag.Principals = customerId.HasValue
                ? await db.MsapPrincipals.AsNoTracking()
                    .Where(p => p.CustomerId == customerId.Value && p.IsActive)
                    .OrderBy(p => p.PrincipalName)
                    .Select(p => new SelectListItem(p.PrincipalName, p.PrincipalId.ToString()))
                    .ToListAsync(cancellationToken)
                : new List<SelectListItem>();
        }
    }
}
