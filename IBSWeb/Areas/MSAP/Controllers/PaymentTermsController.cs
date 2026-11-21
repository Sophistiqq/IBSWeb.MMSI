using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [RequireAnyAccess("Access denied. You don't have permission to view Payment Terms.",
        ProcedureEnum.ManageCustomerMasterFile, ProcedureEnum.ManageBankAccountMasterFile)]
    public class PaymentTermsController(
        IUnitOfWork unitOfWork,
        ILogger<PaymentTermsController> logger)
        : Controller
    {
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
                var terms = await unitOfWork.Terms
                    .GetAllAsync(null, cancellationToken);
                var totalRecords = terms.Count();
                var queried = terms.AsQueryable();

                // Global search
                var searchValue = parameters?.Search?.Value;
                if (!string.IsNullOrEmpty(searchValue))
                {
                    var searchLower = searchValue.ToLower();

                    queried = queried
                        .Where(s =>
                            s.TermsCode.ToLower().Contains(searchLower)
                            || s.NumberOfDays.ToString("N0").Contains(searchLower)
                            || s.NumberOfMonths.ToString("N0").Contains(searchLower));
                }

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

                var recordsFiltered = queried.Count();
                var start = Math.Max(0, parameters?.Start ?? 0);
                var length = parameters?.Length >= 0 ? parameters.Length : 10;
                var pagedData = queried
                    .Skip(start)
                    .Take(length)
                    .ToList();

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
    }
}
