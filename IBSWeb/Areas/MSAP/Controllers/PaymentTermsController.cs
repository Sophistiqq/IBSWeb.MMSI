using System.Security.Claims;
using System.Text.RegularExpressions;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess("Access denied. You don't have permission to manage Payment Terms.",
        ProcedureEnum.ManageCustomerMasterFile, ProcedureEnum.ManageBankAccountMasterFile)]
    public class PaymentTermsController(
        IUnitOfWork unitOfWork,
        ILogger<PaymentTermsController> logger)
        : Controller
    {
        private string GetUserFullName()
        {
            return User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value
                ?? User.Identity?.Name
                ?? "SYSTEM";
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetPaymentTerms([FromForm] DataTablesParameters? parameters, CancellationToken cancellationToken)
        {
            try
            {
                var baseQuery = unitOfWork.Terms.GetAllQuery();
                var totalRecords = await baseQuery.CountAsync(cancellationToken);
                var queried = baseQuery;

                // Global search
                var searchValue = parameters?.Search?.Value;
                if (!string.IsNullOrEmpty(searchValue))
                {
                    var searchLower = searchValue.ToLower();
                    var searchNumeric = searchLower.Replace(",", string.Empty);

                    queried = string.IsNullOrEmpty(searchNumeric)
                        ? queried.Where(s => s.TermsCode.ToLower().Contains(searchLower))
                        : queried.Where(s =>
                            s.TermsCode.ToLower().Contains(searchLower)
                            || s.NumberOfDays.ToString().Contains(searchNumeric)
                            || s.NumberOfMonths.ToString().Contains(searchNumeric));
                }

                var recordsFiltered = await queried.CountAsync(cancellationToken);

                // Sorting
                var orderColumn = parameters?.Order?.FirstOrDefault();
                var columnName = orderColumn != null
                    && parameters?.Columns != null
                    && orderColumn.Column >= 0
                    && orderColumn.Column < parameters.Columns.Count
                    ? parameters.Columns[orderColumn.Column]?.Data?.ToLowerInvariant()
                    : null;
                var isDescending = string.Equals(orderColumn?.Dir, "desc", StringComparison.OrdinalIgnoreCase);

                queried = columnName switch
                {
                    "termscode" => isDescending ? queried.OrderByDescending(s => s.TermsCode) : queried.OrderBy(s => s.TermsCode),
                    "numberofdays" => isDescending ? queried.OrderByDescending(s => s.NumberOfDays) : queried.OrderBy(s => s.NumberOfDays),
                    "numberofmonths" => isDescending ? queried.OrderByDescending(s => s.NumberOfMonths) : queried.OrderBy(s => s.NumberOfMonths),
                    "createdby" => isDescending ? queried.OrderByDescending(s => s.CreatedBy) : queried.OrderBy(s => s.CreatedBy),
                    "createddate" => isDescending ? queried.OrderByDescending(s => s.CreatedDate) : queried.OrderBy(s => s.CreatedDate),
                    _ => isDescending ? queried.OrderByDescending(s => s.TermsCode) : queried.OrderBy(s => s.TermsCode)
                };

                var start = Math.Max(0, parameters?.Start ?? 0);
                var length = parameters?.Length >= 0 ? parameters.Length : 10;
                var pagedData = await queried
                    .Skip(start)
                    .Take(length)
                    .ToListAsync(cancellationToken);

                return Json(new
                {
                    draw = parameters?.Draw ?? 0,
                    recordsTotal = totalRecords,
                    recordsFiltered,
                    data = pagedData
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get payment terms");
                return Json(new
                {
                    draw = parameters?.Draw ?? 0,
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    error = "Failed to fetch payment terms."
                });
            }
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new PaymentTermsViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PaymentTermsViewModel model, CancellationToken cancellationToken)
        {
            model.TermsCode = (model.TermsCode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(model.TermsCode))
            {
                ModelState.AddModelError(nameof(model.TermsCode), "Terms code is required.");
            }
            else if (!Regex.IsMatch(model.TermsCode, "^[A-Z0-9+\\-]{1,10}$"))
            {
                ModelState.AddModelError(nameof(model.TermsCode), "Terms code may only contain uppercase letters, digits, '+', or '-' (max 10 characters).");
            }
            if (model.NumberOfDays < 0)
            {
                ModelState.AddModelError(nameof(model.NumberOfDays), "Number of days cannot be negative.");
            }
            if (model.NumberOfMonths < 0)
            {
                ModelState.AddModelError(nameof(model.NumberOfMonths), "Number of months cannot be negative.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var exists = await unitOfWork.Terms.GetAsync(t => t.TermsCode == model.TermsCode, cancellationToken);
            if (exists != null)
            {
                ModelState.AddModelError(nameof(model.TermsCode), $"Payment term '{model.TermsCode}' already exists.");
                return View(model);
            }

            var actor = GetUserFullName();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = User.Identity?.Name ?? actor;

            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var entity = new Terms
                {
                    TermsCode = model.TermsCode,
                    NumberOfDays = model.NumberOfDays,
                    NumberOfMonths = model.NumberOfMonths,
                    CreatedBy = actor,
                    CreatedDate = DateTimeHelper.GetCurrentPhilippineTime(),
                    EditedBy = string.Empty
                };

                await unitOfWork.Terms.AddAsync(entity, cancellationToken);
                await unitOfWork.AuditTrail.AddAsync(
                    new AuditTrail(username, $"Created payment term '{entity.TermsCode}' ({entity.NumberOfDays}d, {entity.NumberOfMonths}m).", "Payment Terms", referenceNumber: entity.TermsCode, userId: userId, actorName: actor),
                    cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);
            }, cancellationToken);

            TempData["success"] = $"Payment term '{model.TermsCode}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var entity = await unitOfWork.Terms.GetAsync(t => t.TermsCode == id, cancellationToken);
            if (entity == null)
            {
                return NotFound();
            }

            return View(new PaymentTermsViewModel(entity));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PaymentTermsViewModel model, CancellationToken cancellationToken)
        {
            model.TermsCode = (model.TermsCode ?? string.Empty).Trim();
            if (model.NumberOfDays < 0)
            {
                ModelState.AddModelError(nameof(model.NumberOfDays), "Number of days cannot be negative.");
            }
            if (model.NumberOfMonths < 0)
            {
                ModelState.AddModelError(nameof(model.NumberOfMonths), "Number of months cannot be negative.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var existing = await unitOfWork.Terms.GetAsync(t => t.TermsCode == model.TermsCode, cancellationToken);
            if (existing == null)
            {
                return NotFound();
            }

            if (existing.NumberOfDays == model.NumberOfDays && existing.NumberOfMonths == model.NumberOfMonths)
            {
                TempData["info"] = $"No changes were made to payment term '{model.TermsCode}'.";
                return RedirectToAction(nameof(Index));
            }

            var actor = GetUserFullName();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = User.Identity?.Name ?? actor;

            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var updated = new Terms
                {
                    TermsCode = model.TermsCode,
                    NumberOfDays = model.NumberOfDays,
                    NumberOfMonths = model.NumberOfMonths,
                    EditedBy = actor
                };

                await unitOfWork.Terms.UpdateAsync(updated, cancellationToken);
                await unitOfWork.AuditTrail.AddAsync(
                    new AuditTrail(username, $"Updated payment term '{model.TermsCode}' ({model.NumberOfDays}d, {model.NumberOfMonths}m).", "Payment Terms", referenceNumber: model.TermsCode, userId: userId, actorName: actor),
                    cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);
            }, cancellationToken);

            TempData["success"] = $"Payment term '{model.TermsCode}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["error"] = "Invalid payment term code.";
                return RedirectToAction(nameof(Index));
            }

            var actor = GetUserFullName();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = User.Identity?.Name ?? actor;
            string? errorMessage = null;

            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var existing = await unitOfWork.Terms.GetAsync(t => t.TermsCode == id, cancellationToken);
                if (existing == null)
                {
                    errorMessage = $"Payment term '{id}' was not found.";
                    return;
                }

                var inUseByCustomer = await unitOfWork.Customer.CountAsync(c => c.CustomerTerms == id, cancellationToken) > 0;
                var inUseByPrincipal = await unitOfWork.Principal.CountAsync(p => p.Terms == id, cancellationToken) > 0;
                var inUseByBilling = await unitOfWork.Billing.CountAsync(b => b.Terms == id, cancellationToken) > 0;
                if (inUseByCustomer || inUseByPrincipal || inUseByBilling)
                {
                    errorMessage = $"Cannot delete payment term '{id}' because it is referenced by existing MSAP customers, principals, or billings.";
                    return;
                }

                await unitOfWork.Terms.RemoveAsync(existing, cancellationToken);
                await unitOfWork.AuditTrail.AddAsync(
                    new AuditTrail(username, $"Deleted payment term '{id}'.", "Payment Terms", referenceNumber: id, userId: userId, actorName: actor),
                    cancellationToken);
                await unitOfWork.SaveAsync(cancellationToken);
            }, cancellationToken);

            if (errorMessage != null)
            {
                TempData["error"] = errorMessage;
                return RedirectToAction(nameof(Index));
            }

            TempData["success"] = $"Payment term '{id}' deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
