using System.Security.Claims;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ApplicationUser = IBS.Models.ApplicationUser;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess("Access denied. You don't have permission to manage the Customer master file.", ProcedureEnum.ManageCustomerMasterFile)]
    public class CustomerController(
        IUnitOfWork unitOfWork,
        ILogger<CustomerController> logger,
        UserManager<ApplicationUser> userManager)
        : Controller
    {
        private string GetUserFullName()
        {
            return User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value
                   ?? User.Identity?.Name!;
        }

        private async Task<string?> GetCompanyClaimAsync()
        {
            var user = await userManager.GetUserAsync(User);

            if (user == null)
            {
                return null;
            }

            var claims = await userManager.GetClaimsAsync(user);
            return claims.FirstOrDefault(c => c.Type == "Company")?.Value ?? MsapConstants.Company_MMSI;
        }

        public IActionResult Index()
        {
            return View(Enumerable.Empty<Customer>());
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var companyClaims = await GetCompanyClaimAsync();
            if (companyClaims == null)
            {
                return BadRequest();
            }
            var model = new CustomerViewModel()
            {

                PaymentTerms = await unitOfWork.Terms
                    .GetFilprideTermsListAsyncByCode(cancellationToken),
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CustomerViewModel model, CancellationToken cancellationToken)
        {
            var companyClaims = await GetCompanyClaimAsync();

            if (companyClaims == null)
            {
                return BadRequest();
            }

            model.PaymentTerms = await unitOfWork.Terms
                .GetFilprideTermsListAsyncByCode(cancellationToken);

            // Customer codes are generated after validation, never supplied by the create form.
            ModelState.Remove(nameof(CustomerViewModel.CustomerCode));
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var isTinExist = await unitOfWork.Customer.IsTinNoExistAsync(model.CustomerTin, companyClaims, cancellationToken);

            if (isTinExist)
            {
                ModelState.AddModelError("CustomerTin", "Tin No already exist.");
                return View(model);
            }

            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    model.Company = companyClaims;
                    model.CustomerCode = await unitOfWork.Customer.GenerateCodeAsync(model.CustomerType, cancellationToken);
                    model.CreatedBy = GetUserFullName();
                    await unitOfWork.Customer.AddAsync(model, cancellationToken);

                    #region -- Audit Trail Recording

                    AuditTrail auditTrailBook = new(model.CreatedBy!,
                        $"Created new Customer #{model.CustomerCode}; default COS discount: {model.DefaultCosDiscountPercent}%", "Customer");
                    await unitOfWork.AuditTrail.AddAsync(auditTrailBook, cancellationToken);

                    #endregion -- Audit Trail Recording --

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                TempData["success"] = "Customer created successfully";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create customer master file. Created by: {UserName}", userManager.GetUserName(User));
                TempData["error"] = ex.Message;
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken)
        {
            if (id == null || id == 0)
            {
                return NotFound();
            }

            var customer = await unitOfWork.Customer.GetAsync(c => c.CustomerId == id, cancellationToken);
            var companyClaims = await GetCompanyClaimAsync();
            if (companyClaims == null)
            {
                return BadRequest();
            }

            if (customer != null)
            {
                var model = new CustomerViewModel(customer);
                model.PaymentTerms = await unitOfWork.Terms
                    .GetFilprideTermsListAsyncByCode(cancellationToken);
                return View(model);
            }

            return NotFound();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CustomerViewModel model, CancellationToken cancellationToken)
        {
            string? company = await GetCompanyClaimAsync();
            if (company == null)
            {
                return BadRequest();
            }

            model.PaymentTerms = await unitOfWork.Terms
                .GetFilprideTermsListAsyncByCode(cancellationToken);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    model.EditedBy = GetUserFullName();
                    await unitOfWork.Customer.UpdateAsync(model, cancellationToken);

                    #region --Audit Trail Recording

                    AuditTrail auditTrailBook = new (model.EditedBy,
                        $"Edited Customer #{model.CustomerCode}; default COS discount: {model.DefaultCosDiscountPercent}%", "Customer" );
                    await unitOfWork.AuditTrail.AddAsync(auditTrailBook, cancellationToken);

                    #endregion --Audit Trail Recording

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                TempData["success"] = "Customer updated successfully";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to edit customer master file. Created by: {UserName}", userManager.GetUserName(User));
                TempData["error"] = $"Error: '{ex.Message}'";
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetCustomersList([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var query = await unitOfWork.Customer
                    .GetAllAsync(null, cancellationToken);
                var totalRecords = query.Count();

                // Global search
                var searchValue = parameters?.Search?.Value;
                if (!string.IsNullOrEmpty(searchValue))
                {
                    var searchLower = searchValue.ToLower();

                    query = query
                        .Where(c =>
                            c.CustomerCode.ToLower().Contains(searchLower)
                            || c.CustomerName.ToLower().Contains(searchLower)
                            || c.CustomerTerms.ToLower().Contains(searchLower)
                            || c.VatType.ToLower().Contains(searchLower))
                        .ToList();
                }

                // Sorting
                query = ApplyCustomerSort(query, parameters);

                var filteredRecords = query.Count();
                var start = Math.Max(0, parameters?.Start ?? 0);
                var length = parameters?.Length >= 0 ? parameters.Length : 10;
                var pagedData = query
                    .Skip(start)
                    .Take(length)
                    .ToList();

                return Json(new
                {
                    draw = parameters?.Draw ?? 0,
                    recordsTotal = totalRecords,
                    recordsFiltered = filteredRecords,
                    data = pagedData
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get customer");
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Activate(int? id, CancellationToken cancellationToken)
        {
            if (id == null || id == 0)
            {
                return NotFound();
            }

            var customer = await unitOfWork
                .Customer
                .GetAsync(c => c.CustomerId == id, cancellationToken);

            if (customer == null)
            {
                return NotFound();
            }

            customer.PaymentTerms = await unitOfWork.Terms
                .GetFilprideTermsListAsyncByCode(cancellationToken);

            return View(customer);
        }

        [HttpPost, ActionName("Activate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivatePost(int? id, CancellationToken cancellationToken)
        {
            if (id == null || id == 0)
            {
                return NotFound();
            }

            var customer = await unitOfWork
                .Customer
                .GetAsync(c => c.CustomerId == id, cancellationToken);

            if (customer == null)
            {
                return NotFound();
            }

            customer.PaymentTerms = await unitOfWork.Terms
                .GetFilprideTermsListAsyncByCode(cancellationToken);

            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    customer.IsActive = true;

                    #region --Audit Trail Recording

                    var user = GetUserFullName();
                    AuditTrail auditTrailBook = new(
                        user, $"Activated Customer #{customer.CustomerCode}",
                        "Customer");
                    await unitOfWork.AuditTrail.AddAsync(auditTrailBook, cancellationToken);

                    #endregion --Audit Trail Recording

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                TempData["success"] = "Customer has been activated";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to activate customer master file. Activated by: {UserName}", userManager.GetUserName(User));
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Activate), new { id });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Deactivate(int? id, CancellationToken cancellationToken)
        {
            if (id == null || id == 0)
            {
                return NotFound();
            }

            var customer = await unitOfWork
                .Customer
                .GetAsync(c => c.CustomerId == id, cancellationToken);

            if (customer != null)
            {
                customer.PaymentTerms = await unitOfWork.Terms
                    .GetFilprideTermsListAsyncByCode(cancellationToken);

                return View(customer);
            }

            return NotFound();
        }

        [HttpPost, ActionName("Deactivate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivatePost(int? id, CancellationToken cancellationToken)
        {
            if (id == null || id == 0)
            {
                return NotFound();
            }

            var customer = await unitOfWork
                .Customer
                .GetAsync(c => c.CustomerId == id, cancellationToken);

            if (customer == null)
            {
                return NotFound();
            }

            customer.PaymentTerms = await unitOfWork.Terms
                .GetFilprideTermsListAsyncByCode(cancellationToken);

            try
            {
                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    customer.IsActive = false;

                    #region -- Audit Trail Recording --

                    AuditTrail auditTrailBook = new(GetUserFullName(),
                        $"Deactivated Customer #{customer.CustomerCode}", "Customer");
                    await unitOfWork.AuditTrail.AddAsync(auditTrailBook, cancellationToken);

                    #endregion -- Audit Trail Recording --

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                TempData["success"] = "Customer has been deactivated";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deactivate customer master file. Deactivated by: {UserName}", userManager.GetUserName(User));
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Deactivate), new { id });
            }
        }

        private static IEnumerable<Customer> ApplyCustomerSort(IEnumerable<Customer> customers, DataTablesParameters? parameters)
        {
            var orderColumn = parameters?.Order?.FirstOrDefault();
            if (orderColumn == null)
            {
                return customers.OrderBy(c => c.CustomerCode);
            }

            var columnName = parameters?.Columns != null
                && orderColumn.Column >= 0
                && orderColumn.Column < parameters.Columns.Count
                ? parameters.Columns[orderColumn.Column]?.Data?.ToLowerInvariant()
                : null;
            var isDescending = string.Equals(orderColumn.Dir, "desc", StringComparison.OrdinalIgnoreCase);

            return columnName switch
            {
                "customerid" => isDescending ? customers.OrderByDescending(c => c.CustomerId) : customers.OrderBy(c => c.CustomerId),
                "customercode" => isDescending ? customers.OrderByDescending(c => c.CustomerCode) : customers.OrderBy(c => c.CustomerCode),
                "customername" => isDescending ? customers.OrderByDescending(c => c.CustomerName) : customers.OrderBy(c => c.CustomerName),
                "customertin" => isDescending ? customers.OrderByDescending(c => c.CustomerTin) : customers.OrderBy(c => c.CustomerTin),
                "businessstyle" => isDescending ? customers.OrderByDescending(c => c.BusinessStyle) : customers.OrderBy(c => c.BusinessStyle),
                "customerterms" => isDescending ? customers.OrderByDescending(c => c.CustomerTerms) : customers.OrderBy(c => c.CustomerTerms),
                "customertype" => isDescending ? customers.OrderByDescending(c => c.CustomerType) : customers.OrderBy(c => c.CustomerType),
                "vattype" => isDescending ? customers.OrderByDescending(c => c.VatType) : customers.OrderBy(c => c.VatType),
                "isactive" => isDescending ? customers.OrderByDescending(c => c.IsActive) : customers.OrderBy(c => c.IsActive),
                "createddate" => isDescending ? customers.OrderByDescending(c => c.CreatedDate) : customers.OrderBy(c => c.CreatedDate),
                _ => isDescending ? customers.OrderByDescending(c => c.CustomerCode) : customers.OrderBy(c => c.CustomerCode)
            };
        }
    }
}
