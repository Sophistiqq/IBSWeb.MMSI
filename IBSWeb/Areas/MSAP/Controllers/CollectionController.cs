using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Services.MSAP.Attributes;
using IBS.Utility.MSAP.Helpers;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    /// <summary>
    /// Controller for managing Collections and payment allocations in the MMSI system.
    /// </summary>
    [Area("MSAP")]
    public class CollectionController(
        IUnitOfWork unitOfWork,
        CollectionService collectionService,
        ILogger<CollectionController> logger)
        : Controller
    {
        #region Index

        /// <summary>
        /// Displays the list of Collections.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewCollections)]
        public IActionResult Index()
        {
            return View();
        }

        #endregion

        #region Create

        /// <summary>
        /// Displays the form to create a new Collection.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateCollection, "Access denied. You don't have permission to create Collections.")]
        public async Task<IActionResult> Create(CancellationToken cancellationToken = default, int? customerOrderSlipId = null)
        {
            var model = await collectionService.PopulateCreateViewModelAsync(cancellationToken);
            if (customerOrderSlipId.HasValue)
            {
                var job = await unitOfWork.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == customerOrderSlipId.Value, cancellationToken);
                if (job == null)
                {
                    return NotFound();
                }
                model.CustomerId = job.CustomerId;
                model.CustomerOrderSlipId = job.CustomerOrderSlipId;
            }
            return View(model);
        }

        /// <summary>
        /// Processes the creation of a new Collection, including billing allocation and accounting posting.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateCollection, "Access denied. You don't have permission to create Collections.")]
        public async Task<IActionResult> Create(CreateCollectionViewModel viewModel, CancellationToken cancellationToken = default)
        {
            var result = ModelState.IsValid
                ? await collectionService.CreateCollectionAsync(viewModel, User.Identity?.Name ?? "System", cancellationToken)
                : ServiceResult<int>.Failure("Review the highlighted collection fields.");

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
                return RedirectToAction(nameof(Preview), new { id = result.Data });
            }

            TempData["error"] = result.Message;
            viewModel.Customers = await collectionService.GetCustomerSelectListAsync(0, viewModel.CustomerId, cancellationToken);
            return View(viewModel);
        }

        #endregion

        #region Edit

        /// <summary>
        /// Displays the form to edit an existing Collection.
        /// </summary>
        [HttpGet]
        [RequireAccess(ProcedureEnum.CreateCollection, "Access denied. You don't have permission to edit Collections.")]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
        {
            var viewModel = await collectionService.PopulateEditViewModelAsync(id, cancellationToken);

            if (viewModel == null)
            {
                return NotFound();
            }

            return View(viewModel);
        }

        /// <summary>
        /// Processes the update of an existing Collection, including reverting old allocations and applying new ones.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.CreateCollection, "Access denied. You don't have permission to edit Collections.")]
        public async Task<IActionResult> Edit(CreateCollectionViewModel viewModel, CancellationToken cancellationToken = default)
        {
            var result = ModelState.IsValid
                ? await collectionService.UpdateCollectionAsync(viewModel, User.Identity?.Name ?? "System", cancellationToken)
                : ServiceResult.Failure("Review the highlighted collection fields.");

            if (result.IsSuccess)
            {
                TempData["success"] = result.Message;
                return RedirectToAction(nameof(Preview), new { id = viewModel.MsapCollectionId });
            }

            if (result.Status == ServiceResultStatus.NotFound)
            {
                return NotFound();
            }

            TempData["error"] = result.Message;
            viewModel.Customers = await collectionService.GetCustomerSelectListAsync(viewModel.MsapCollectionId ?? 0, viewModel.CustomerId, cancellationToken);
            return View(viewModel);
        }

        #endregion

        #region Preview & Listing

        /// <summary>
        /// Displays the details of a specific Collection, including associated paid billings.
        /// </summary>
        [RequireAccess(ProcedureEnum.ViewCollections)]
        public async Task<IActionResult> Preview(int id, CancellationToken cancellationToken = default)
        {
            var collection = await collectionService.GetCollectionByIdAsync(id, cancellationToken);

            if (collection != null)
            {
                collection.IsMonthClosed = await unitOfWork.PostedPeriod.IsMonthClosedAsync(collection.Date.Year, collection.Date.Month, cancellationToken);
                var bills = await unitOfWork.Billing.GetBillingsByCollectionIdAsync(id, cancellationToken);
                ViewBag.JobProgressItems = await JobProgressCalculator.LoadForBillingsAsync(unitOfWork, bills.Select(b => b.MsapBillingId), cancellationToken);
                return View(collection);
            }

            TempData["error"] = "Error: collection record not found.";
            return RedirectToAction("Index");
        }

        /// <summary>
        /// Retrieves a paged and filtered list of Collections for DataTables.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.ViewCollections)]
        public async Task<IActionResult> GetCollectionList([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var (data, filtered, total) = await collectionService.GetPagedCollectionsAsync(parameters, cancellationToken);

                var closedMonths = (await unitOfWork.PostedPeriod.GetAllAsync(cancellationToken))
                    .Where(p => MsapConstants.EnforceClosedPeriods && p.IsClosed)
                    .Select(p => (p.Year, p.Month))
                    .ToHashSet();
                IEnumerable<Collection> collections = data as Collection[] ?? data.ToArray();
                foreach (var item in collections)
                {
                    item.IsMonthClosed = closedMonths.Contains((item.Date.Year, item.Date.Month));
                }

                return Json(new
                {
                    draw = parameters.Draw,
                    recordsTotal = total,
                    recordsFiltered = filtered,
                    data = collections
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get collections");
                TempData["error"] = ExceptionHelper.GetErrorMessage(ex);
                return RedirectToAction(nameof(Index));
            }
        }

        #endregion

        [HttpGet]
        [RequireAccess(ProcedureEnum.AccessTreasury, "Access denied. You don't have permission to record banking updates.")]
        public async Task<IActionResult> Banking(int id, CollectionBankingAction bankingAction, CancellationToken ct)
        {
            var collection = await collectionService.GetCollectionByIdAsync(id, ct);
            if (collection == null)
            {
                return NotFound();
            }
            ViewBag.BankAccounts = await collectionService.GetBankAccountsAsync(ct);
            ViewBag.BankingAction = bankingAction;
            return PartialView("_Banking", collection);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAccess(ProcedureEnum.AccessTreasury, "Access denied. You don't have permission to record banking updates.")]
        public async Task<IActionResult> Banking(int id, CollectionBankingAction bankingAction, DateOnly date, int? bankId, CancellationToken ct)
        {
            var result = ModelState.IsValid
                ? await collectionService.UpdateBankingAsync(id, bankingAction, date, bankId, User.Identity?.Name ?? "System", ct)
                : ServiceResult.Failure("Enter a valid transaction date and bank account.");
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                if (result.IsSuccess)
                {
                    TempData["success"] = result.Message;
                }
                return Json(new { success = result.IsSuccess, message = result.Message });
            }
            TempData[result.IsSuccess ? "success" : "error"] = result.Message;
            return RedirectToAction(nameof(Preview), new { id });
        }

        #region AJAX Endpoints

        /// <summary>
        /// Retrieves detailed information for a list of selected billings.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireAnyAccess(ProcedureEnum.CreateCollection)]
        public async Task<IActionResult> GetSelectedBillings(List<string> billingIds, CancellationToken cancellationToken = default)
        {
            var result = await collectionService.GetSelectedBillingsAsync(billingIds, cancellationToken);
            return Json(new { success = result.IsSuccess, data = result.Data, message = result.Message });
        }

        /// <summary>
        /// Checks if a customer is vatable based on their ID.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.CreateCollection)]
        public async Task<IActionResult> IsCustomerVatable(int customerId, CancellationToken cancellationToken = default)
        {
            var isVatable = await collectionService.IsCustomerVatableAsync(customerId, cancellationToken);
            return Json(isVatable);
        }

        /// <summary>
        /// Retrieves detailed information for a specific bank account.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.CreateCollection, ProcedureEnum.AccessTreasury)]
        public async Task<IActionResult> GetBankAccountDetails(int bankId, CancellationToken cancellationToken = default)
        {
            var result = await collectionService.GetBankAccountDetailsAsync(bankId, cancellationToken);
            return Json(result.IsSuccess ? new { success = true, bank = result.Data } : new { success = false, message = result.Message });
        }

        /// <summary>
        /// Retrieves uncollected billings (and optionally currently associated ones) for a specific customer in a detailed format for DataTables.
        /// </summary>
        [HttpGet]
        [RequireAnyAccess(ProcedureEnum.CreateCollection)]
        public async Task<IActionResult> GetUncollectedBillingsForTable(int customerId, int? collectionId, CancellationToken cancellationToken = default, int? customerOrderSlipId = null)
        {
            var result = await collectionService.GetUncollectedBillingsForTableAsync(customerId, collectionId, cancellationToken, customerOrderSlipId);
            return Json(new { success = result.IsSuccess, data = result.Data, message = result.Message });
        }

        #endregion
    }
}
