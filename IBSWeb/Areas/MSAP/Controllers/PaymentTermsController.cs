using System.Linq.Dynamic.Core;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
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
        public async Task<IActionResult> GetPaymentTerms([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var terms = await unitOfWork.Terms
                    .GetAllAsync(null, cancellationToken);
                var totalRecords = terms.Count();
                var queried = terms.AsQueryable();

                // Global search
                if (!string.IsNullOrEmpty(parameters.Search.Value))
                {
                    var searchValue = parameters.Search.Value.ToLower();

                    queried = queried
                    .Where(s =>
                        s.TermsCode.ToLower().Contains(searchValue) ||
                        s.NumberOfDays.ToString("N0").Contains(searchValue) ||
                        s.NumberOfMonths.ToString("N0").Contains(searchValue)
                        );
                }

                // Sorting
                if (parameters.Order?.Count > 0)
                {
                    var orderColumn = parameters.Order[0];
                    var columnName = parameters.Columns[orderColumn.Column].Data;
                    var sortDirection = orderColumn.Dir.ToLower() == "asc" ? "ascending" : "descending";
                    queried = queried
                        .OrderBy($"{columnName} {sortDirection}");
                }

                var recordsFiltered = queried.Count();
                var pagedData = queried
                    .Skip(parameters.Start)
                    .Take(parameters.Length)
                    .ToList();

                return Json(new
                {
                    draw = parameters.Draw,
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
                    draw = parameters.Draw,
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    error = "Failed to fetch payment terms."
                });
            }
        }
    }
}
