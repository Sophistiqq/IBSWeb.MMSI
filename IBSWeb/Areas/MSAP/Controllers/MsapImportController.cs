using System.Security.Claims;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [Authorize(Policy = MsapRoles.AdminPolicy)]
    public class MsapImportController(MasterFileImportService imports, ILogger<MsapImportController> logger) : Controller
    {
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var model = new MasterFileImportViewModel();
            await imports.PopulateAsync(model, ct);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(22 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
        public async Task<IActionResult> Index([Bind("Files,UpdateExisting,NewTugboatPortCode,NonVatableTaxType,ReviewedFingerprint,EditedFilesJson")] MasterFileImportViewModel model, bool import, CancellationToken ct)
        {
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Challenge();
            }
            if (ModelState.IsValid)
            {
                try
                {
                    await imports.ProcessAsync(model, userId, import, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Master-file import failed");
                    model.ReviewedFingerprint = null;
                    model.Errors.Add("The import did not return a confirmed result. Review stored records and validate again before retrying.");
                }
            }
            else
            {
                model.Errors.Add("Some import options are invalid. Review the form and validate again.");
            }
            // Never reuse the posted fingerprint when returning a failed validation.
            ModelState.Remove(nameof(model.ReviewedFingerprint));
            await imports.PopulateAsync(model, ct);
            if (Request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.Ordinal) == true))
            {
                return Json(new { model.Items, model.Errors, model.IgnoredFiles, model.ReviewedFingerprint, model.Imported, model.Ports, model.Documents,
                    RateDrafts = model.Imported ? model.RateDraftIds.Select(id => new { Url = Url.Action("Details", "RateUpload", new { id }) }) : [] });
            }
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Template(CancellationToken ct)
        {
            return File(await MasterFileImportWorkbook.WriteAsync(null, ct),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MSAP-Master-File-Headers.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(22 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
        public async Task<IActionResult> Export([Bind("Files,UpdateExisting,NewTugboatPortCode,NonVatableTaxType,ReviewedFingerprint,EditedFilesJson")] MasterFileImportViewModel model, CancellationToken ct)
        {
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Challenge();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest("Review the import options and validate again.");
            }
            string? reviewed = model.ReviewedFingerprint;
            try
            {
                await imports.ProcessAsync(model, userId, false, ct);
                if (model.Errors.Count > 0 || string.IsNullOrEmpty(reviewed) || reviewed != model.ReviewedFingerprint)
                {
                    return BadRequest("Correct and validate the current editor data before exporting the cleaned workbook.");
                }
                return File(await MasterFileImportWorkbook.WriteAsync(model.Documents, ct),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MSAP-Master-Files-Cleaned.xlsx");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Master-file export failed");
                return StatusCode(500, "The workbook could not be created. Validate again and retry.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(22 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
        public async Task<IActionResult> Corrections([Bind("Files,UpdateExisting,NewTugboatPortCode,NonVatableTaxType,EditedFilesJson")] MasterFileImportViewModel model, CancellationToken ct)
        {
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Challenge();
            }
            if (!ModelState.IsValid)
            {
                return BadRequest("Review the import options and validate again.");
            }
            try
            {
                await imports.ProcessAsync(model, userId, false, ct);
                return File(await MasterFileImportWorkbook.WriteAsync(model.Documents, ct, model),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MSAP-Master-File-Corrections.xlsx");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Master-file correction workbook failed");
                return StatusCode(500, "The correction workbook could not be created. Retry validation before exporting.");
            }
        }
    }
}
