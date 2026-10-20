using System.Security.Claims;
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
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "render-check")], "check"));
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
                OwnerId = 1, ChargeId = 1,
                Owners = [new SelectListItem("Owner <check>", "1")],
                Charges = [new SelectListItem("Bill <check>", "1")],
                Owner = new TugboatOwner { TugboatOwnerName = "Owner <check>", Address = "Saved address", Tin = "Saved TIN", VatType = "Vatable", TaxType = "Withholding Tax", WithholdingTaxPercent = 0.02m },
                Charge = new OtherTugCharge { ProviderReference = "Bill <check>" }, Gross = 1120m, Withholding = 20m
            };
            string paymentHtml = await RenderAsync("/Areas/Filpride/Views/CheckVoucherTrade/CreateTugboatPayment.cshtml", paymentForm);
            Check(paymentHtml.Contains("Owner &lt;check&gt;", StringComparison.Ordinal)
                && paymentHtml.Contains("name=\"Documentation.IsDocumentedByOtherCompany\"", StringComparison.Ordinal)
                && paymentHtml.Contains("__RequestVerificationToken", StringComparison.Ordinal)
                && paymentHtml.Contains("name=\"__Workspace\"", StringComparison.Ordinal)
                && !paymentHtml.Contains("name=\"SupplierId\"", StringComparison.Ordinal),
                "Provider payment form lost encoded snapshots, nested documentation, CSRF or workspace protection.");
            var controller = typeof(IBSWeb.Areas.Filpride.Controllers.CheckVoucherTradeController);
            foreach (string method in new[] { "PostTugboatPayment", "UnpostTugboatPayment", "CancelTugboatPayment", "VoidTugboatPayment", "PrintTugboatPayment" })
            {
                var endpoint = controller.GetMethod(method)!;
                Check(endpoint.IsDefined(typeof(HttpPostAttribute), true) && endpoint.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true)
                    && endpoint.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true),
                    "A payment mutation is missing POST, CSRF or authorization: " + method);
            }
            Console.WriteLine("PASS: Razor AP Other Tugs owner snapshots, nested documentation, CSRF/workspace marker and protected payment mutations.");
            Console.WriteLine("PASS: Razor workspace selection/antiforgery, stale-form marker, owner payment profile and strict ticket form render correctly.");

            async Task<string> RenderAsync<T>(string path, T model)
            {
                var view = engine.GetView(null, path, false);
                Check(view.Success, "Razor view not found: " + path);
                var data = new ViewDataDictionary<T>(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
                using var output = new StringWriter();
                await view.View!.RenderAsync(new ViewContext(action, view.View, data,
                    services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
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
