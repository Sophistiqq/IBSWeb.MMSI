using System.Security.Claims;
using IBS.Models.Enums;
using IBS.Models.Filpride.AccountsPayable;
using IBS.Models.Filpride.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Models.Filpride.ViewModels;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Services;
using IBS.Utility;
using IBSWeb.Workspaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Checks.MSAP
{
    internal static class RebuildRenderingCheck
    {
        public static async Task RunAsync(IServiceProvider services)
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "render-check"), new Claim(ClaimTypes.Name, "render-check"), new Claim(ClaimTypes.Role, "Admin")], "check"));
            var http = new DefaultHttpContext { RequestServices = services, User = user };
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Rebuild render check"));
            var route = new RouteData();
            route.Values["area"] = "Filpride";
            route.Values["controller"] = "CheckVoucherTrade";
            route.Values["action"] = "Create";
            http.Request.RouteValues = route.Values;
            var action = new ActionContext(http, route, new ActionDescriptor());
            var engine = services.GetRequiredService<IRazorViewEngine>();
            var workspace = services.GetRequiredService<TugboatWorkspace>();
            var selection = services.GetRequiredService<TugboatWorkspaceService>();
            List<SelectListItem> options = await selection.GetOptionsAsync(default);
            Check(options.Count >= 2, "Rendering requires two imported owned tugboats.");
            int owned = int.Parse(options[0].Value);
            workspace.Select(owned, options[0].Text);
            string selector = await RenderAsync("/Views/Shared/_TugboatWorkspace.cshtml", new object());
            Check(selector.Contains("MMSI / All tugboats", StringComparison.Ordinal)
                && selector.Contains("__RequestVerificationToken", StringComparison.Ordinal)
                && selector.Contains("class=\"mnav-workspace\"", StringComparison.Ordinal)
                && selector.Contains("aria-label=\"Switch accounting workspace\"", StringComparison.Ordinal)
                && selector.Contains("value=\"" + owned + "\" selected=\"selected\"", StringComparison.Ordinal),
                "Workspace selector lost its consolidated option, selected tugboat or antiforgery token.");

            var viewContext = new ViewContext { HttpContext = http };
            var helper = new TugboatWorkspaceFormTagHelper(workspace, selection) { ViewContext = viewContext };
            var tagContext = new TagHelperContext([], new Dictionary<object, object>(), "check");
            var tagOutput = new TagHelperOutput("form", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
            helper.Process(tagContext, tagOutput);
            using var tokenText = new StringWriter();
            tagOutput.PostContent.WriteTo(tokenText, System.Text.Encodings.Web.HtmlEncoder.Default);
            Check(tokenText.ToString().Contains("name=\"__Workspace\"", StringComparison.Ordinal), "Filpride form omitted the stale-workspace marker.");

            route.Values["area"] = "MSAP";
            route.Values["controller"] = "TugboatOwner";
            string owner = await RenderAsync("/Areas/MSAP/Views/TugboatOwner/_PaymentProfile.cshtml",
                new TugboatOwnerViewModel { Address = "<check>", Tin = "TIN", VatType = "Vatable", TaxType = "Withholding Tax", WithholdingTaxPercent = 0.0200m });
            Check(owner.Contains("&lt;check&gt;", StringComparison.Ordinal) && owner.Contains("name=\"WithholdingTaxPercent\"", StringComparison.Ordinal)
                && owner.Contains("value=\"0.02\" selected=\"selected\"", StringComparison.Ordinal)
                && !owner.Contains("SupplierId", StringComparison.Ordinal), "Owner profile rendering lost the stored decimal rate/encoded values or duplicated Supplier entry.");

            route.Values["controller"] = "DispatchTicket";
            var ticket = new DispatchTicketViewModel { COSNumber = "COS-<check>", DispatchNumber = "ACTUAL-1" };
            string actual = await RenderAsync("/Areas/MSAP/Views/CustomerOrderSlip/_ActualTicketEntry.cshtml", ticket);
            Check(actual.Contains("COS-&lt;check&gt;", StringComparison.Ordinal)
                && actual.Contains("before saving", StringComparison.Ordinal)
                && !actual.Contains("name=\"COSNumber\"", StringComparison.Ordinal)
                && actual.Contains("name=\"VesselScheduleServiceEntryId\"", StringComparison.Ordinal),
                "Actual-ticket form lost canonical COS display, approved service linkage or strict-save guidance.");
            workspace.Select(null, "MMSI / All tugboats");
            route.Values["area"] = "Filpride";
            route.Values["controller"] = "CheckVoucherTrade";
            route.Values["action"] = "CreateTugboatPayment";
            var paymentForm = new TugboatPaymentViewModel
            {
                OwnerId = 1, AllocationIds = [1],
                CosIds = [1], CustomerOrderSlips = [new SelectListItem("COS-<check>", "1")],
                Owners = [new SelectListItem("Owner <check>", "1")],
                Bills = [new TugboatPaymentBillViewModel
                {
                    ChargeId = 1, ProviderReference = "Bill <check>", IncludesVat = true, Gross = 1120m, Withholding = 20m,
                    Lines = [new TugboatPaymentLine { AllocationId = 1, Gross = 1120m, Withholding = 20m, TicketId = 1, TicketNumber = "DT-<check>", TugboatName = "Tug <check>", CosId = 1, CosNumber = "COS-<check>", Amount = 1120m }]
                }],
                Owner = new TugboatOwner { TugboatOwnerName = "Owner <check>", Address = "Saved address", Tin = "Saved TIN", VatType = "Vatable", TaxType = "Withholding Tax", WithholdingTaxPercent = 0.02m },
                Charge = new OtherTugCharge
                {
                    ProviderReference = "Bill <check>", IncludesVat = true,
                    Lines = [new OtherTugChargeLine { Amount = 1120m, Ticket = new DispatchTicket { DispatchNumber = "DT-<check>", Tugboat = new Tugboat { TugboatName = "Tug <check>" } } }]
                },
                Gross = 1120m, Withholding = 20m, WithholdingAccountNumber = "201030220"
            };
            string paymentHtml = await RenderAsync("/Areas/Filpride/Views/CheckVoucherTrade/CreateTugboatPayment.cshtml", paymentForm, renderScripts: true);
            Check(paymentHtml.Contains("Owner &lt;check&gt;", StringComparison.Ordinal)
                && paymentHtml.Contains("name=\"Documentation.IsDocumentedByOtherCompany\"", StringComparison.Ordinal)
                && paymentHtml.Contains("201020500", StringComparison.Ordinal)
                && paymentHtml.Contains("__RequestVerificationToken", StringComparison.Ordinal)
                && paymentHtml.Contains("name=\"__Workspace\"", StringComparison.Ordinal)
                && !paymentHtml.Contains("name=\"SupplierId\"", StringComparison.Ordinal),
                "Provider payment form lost encoded snapshots, nested documentation, CSRF or workspace protection.");
            Check(paymentHtml.Contains("card shadow border-0 mt-4", StringComparison.Ordinal)
                && paymentHtml.Contains("DISPATCH TICKET LIST", StringComparison.Ordinal)
                && paymentHtml.Contains("DT-\\u003Ccheck\\u003E", StringComparison.Ordinal)
                && paymentHtml.Contains("COS-\\u003Ccheck\\u003E", StringComparison.Ordinal)
                && paymentHtml.Contains("id=\"CosIds\"", StringComparison.Ordinal)
                && paymentHtml.Contains("GetTugboatPaymentDetails", StringComparison.Ordinal)
                && !paymentHtml.Contains("Load selection", StringComparison.Ordinal)
                && !paymentHtml.Contains("requestSubmit", StringComparison.Ordinal)
                && paymentHtml.Contains("Payment Amount", StringComparison.Ordinal)
                && !paymentHtml.Contains("Select posted AP journal", StringComparison.Ordinal)
                && !paymentHtml.Contains("LiabilityJournalId", StringComparison.Ordinal)
                && !paymentHtml.Contains("Accounting must record and post", StringComparison.Ordinal)
                && paymentHtml.Contains("201030220", StringComparison.Ordinal)
                && paymentHtml.Contains("value=\"1100", StringComparison.Ordinal)
                && paymentHtml.Contains("class=\"rrAmount\"", StringComparison.Ordinal)
                && !paymentHtml.Contains("id=\"AddAccEntry\"", StringComparison.Ordinal),
                "Tugboat payment lost the Supplier Trade layout, asynchronous owner/COS/ticket selection, encoded ticket data or read-only accounting preview.");
            string emptyPaymentHtml = await RenderAsync("/Areas/Filpride/Views/CheckVoucherTrade/CreateTugboatPayment.cshtml", new TugboatPaymentViewModel(), renderScripts: true);
            Check(emptyPaymentHtml.Contains("Select a COS to view dispatch allocations", StringComparison.Ordinal)
                && emptyPaymentHtml.Contains("selectedAllocations.size === 0", StringComparison.Ordinal),
                "An empty tugboat payment form failed to render or allows creation without a bill.");
            var authorization = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Authorization.AuthorizationOptions>>().Value;
            foreach (string policy in new[] { "CheckVoucherTradePost", "CheckVoucherTradeUnpost", "CheckVoucherTradeCancel" })
            {
                authorization.AddPolicy(policy, rule => rule.RequireRole("Admin"));
            }
            var voucher = new CheckVoucherVM
            {
                Header = new FilprideCheckVoucherHeader
                {
                    CheckVoucherHeaderId = 42, CheckVoucherHeaderNo = "CV-PRINT-CHECK", CvType = nameof(CVType.OtherTugs),
                    Type = nameof(DocumentType.Documented), SupplierName = "Owner <check>", Payee = "Owner <check>",
                    Address = "Saved <address>", Tin = "Saved TIN", CheckAmount = 1100m, Particulars = "Saved particulars",
                    Date = new DateOnly(2026, 10, 21), CreatedBy = "Creator", PostedBy = "Poster", TugboatPayments = [new FilprideTugboatPayment()]
                },
                Details =
                [
                    new FilprideCheckVoucherDetail { AccountNo = "201020500", AccountName = "AP Other Tugs", Debit = 1120m },
                    new FilprideCheckVoucherDetail { AccountNo = "101010100", AccountName = "Cash", Credit = 1100m },
                    new FilprideCheckVoucherDetail { AccountNo = "201030220", AccountName = "EWT", Credit = 20m },
                    new FilprideCheckVoucherDetail { IsDisplayEntry = true, AccountName = "SUPPLIER DISPLAY ONLY", Debit = 999m }
                ]
            };
            route.Values["action"] = "Print";
            string printHtml = await RenderAsync("/Areas/Filpride/Views/CheckVoucherTrade/Print.cshtml", voucher, renderScripts: true);
            Check(printHtml.Contains("DISBURSEMENT VOUCHER", StringComparison.Ordinal)
                && printHtml.Contains("I. PAYEE DETAILS", StringComparison.Ordinal)
                && printHtml.Contains("II. CHECK DETAILS", StringComparison.Ordinal)
                && printHtml.Contains("III. ACCOUNTING ENTRIES", StringComparison.Ordinal)
                && printHtml.Contains("OWNER &lt;CHECK&gt;", StringComparison.Ordinal)
                && printHtml.Contains("SAVED &lt;ADDRESS&gt;", StringComparison.Ordinal)
                && printHtml.Contains("COS # :", StringComparison.Ordinal)
                && printHtml.Contains("Bill # :", StringComparison.Ordinal)
                && printHtml.Contains("201020500 AP Other Tugs", StringComparison.Ordinal)
                && printHtml.Contains("1,120.00", StringComparison.Ordinal)
                && printHtml.Contains("One Thousand One Hundred Pesos Only", StringComparison.Ordinal)
                && printHtml.Contains("ORIGINAL COPY", StringComparison.Ordinal)
                && !printHtml.Contains("SUPPLIER DISPLAY ONLY", StringComparison.Ordinal),
                "Shared print lost the existing voucher layout, encoded owner snapshot, words or real accounting entries.");
            Check(printHtml.Contains("id=\"tugboatTransition\"", StringComparison.Ordinal)
                && printHtml.Contains("__RequestVerificationToken", StringComparison.Ordinal)
                && printHtml.Contains("name=\"__Workspace\"", StringComparison.Ordinal)
                && printHtml.Contains("PostTugboatPayment", StringComparison.Ordinal)
                && printHtml.Contains("UnpostTugboatPayment", StringComparison.Ordinal)
                && printHtml.Contains("PrintTugboatPayment", StringComparison.Ordinal)
                && !printHtml.Contains("VoidTugboatPayment", StringComparison.Ordinal)
                && !printHtml.Contains("CancelTugboatPayment", StringComparison.Ordinal)
                && !printHtml.Contains("confirmTugboatCorrection", StringComparison.Ordinal),
                "Shared print lost protected tugboat lifecycle actions.");
            voucher.Header.IsPrinted = true;
            string reprintHtml = await RenderAsync("/Areas/Filpride/Views/CheckVoucherTrade/Print.cshtml", voucher);
            Check(reprintHtml.Contains("REPRINT", StringComparison.Ordinal) && !reprintHtml.Contains("ORIGINAL COPY", StringComparison.Ordinal),
                "Shared voucher did not distinguish a reprint from the original.");
            voucher.Header.CvType = nameof(CVType.Supplier);
            voucher.Header.SupplierId = 1;
            voucher.Supplier = new FilprideSupplier { SupplierId = 1, SupplierName = "Supplier" };
            string supplierPrintHtml = await RenderAsync("/Areas/Filpride/Views/CheckVoucherTrade/Print.cshtml", voucher, renderScripts: true);
            Check(supplierPrintHtml.Contains("SUPPLIER DISPLAY ONLY", StringComparison.Ordinal)
                && !supplierPrintHtml.Contains("201020500 AP Other Tugs", StringComparison.Ordinal)
                && supplierPrintHtml.Contains("PO # :", StringComparison.Ordinal)
                && supplierPrintHtml.Contains("SI # :", StringComparison.Ordinal)
                && !supplierPrintHtml.Contains("id=\"tugboatTransition\"", StringComparison.Ordinal)
                && !supplierPrintHtml.Contains("PrintTugboatPayment", StringComparison.Ordinal),
                "Shared print changed Supplier Trade references, display entries or lifecycle controls.");
            Console.WriteLine("PASS: Existing Supplier Trade voucher reused for owner payments, accounting entries, amount in words, original/reprint, CSRF/workspace controls; Supplier Trade rendering preserved.");
            var controller = typeof(IBSWeb.Areas.Filpride.Controllers.CheckVoucherTradeController);
            var selectionEndpoint = controller.GetMethod("GetTugboatPaymentDetails")!;
            Check(selectionEndpoint.IsDefined(typeof(HttpGetAttribute), true)
                && selectionEndpoint.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true),
                "Payment selection endpoint is missing its GET or authorization restriction.");
            foreach (string method in new[] { "PostTugboatPayment", "UnpostTugboatPayment", "CancelTugboatPayment", "VoidTugboatPayment", "PrintTugboatPayment" })
            {
                var endpoint = controller.GetMethod(method)!;
                Check(endpoint.IsDefined(typeof(HttpPostAttribute), true) && endpoint.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true)
                    && endpoint.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true),
                    "A payment mutation is missing POST, CSRF or authorization: " + method);
            }
            Console.WriteLine("PASS: Razor AP Other Tugs owner snapshots, nested documentation, CSRF/workspace marker and protected payment mutations.");
            Console.WriteLine("PASS: Razor workspace selection/antiforgery, stale-form marker, owner payment profile and strict ticket form render correctly.");

            async Task<string> RenderAsync<T>(string path, T model, bool renderScripts = false)
            {
                var view = engine.GetView(null, path, false);
                Check(view.Success, "Razor view not found: " + path);
                var data = new ViewDataDictionary<T>(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
                using var output = new StringWriter();
                await view.View!.RenderAsync(new ViewContext(action, view.View, data,
                    services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
                if (renderScripts && view.View is RazorView razor && razor.RazorPage.SectionWriters.TryGetValue("Scripts", out var scripts))
                {
                    await scripts();
                }
                return output.ToString();
            }
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
