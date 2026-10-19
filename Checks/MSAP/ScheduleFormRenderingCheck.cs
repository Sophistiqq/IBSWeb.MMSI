using System.Text.RegularExpressions;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.ViewModels;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Checks.MSAP
{
    internal static class ScheduleFormRenderingCheck
    {
        public static async Task RunAsync(IServiceProvider services)
        {
            var http = new DefaultHttpContext { RequestServices = services };
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Schedule form check"));
            var route = new RouteData();
            route.Values["area"] = "MSAP";
            route.Values["controller"] = "VesselSchedule";
            route.Values["action"] = "Edit";
            http.Request.RouteValues = route.Values;
            var descriptor = services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>().First(a => a.ControllerName == "VesselSchedule" && a.ActionName == "Edit");
            var context = new ActionContext(http, route, descriptor);
            var model = new VesselScheduleViewModel
            {
                VesselScheduleId = 5, CustomerId = 1, VesselId = 1, PortId = 1, TerminalId = 1,
                Revision = 2, ApprovedRevision = 1, SkipCosDiscount = true, CosDiscountPercent = 12.5m, SbmaEnabled = true,
                PlannedStart = new DateTime(2026, 10, 16, 23, 0, 0), PlannedEnd = new DateTime(2026, 10, 17, 1, 0, 0),
                SelectedTugboatIds = [1, 2], Tugboats = [new SelectListItem("Tug <A>", "1"), new SelectListItem("Tug B", "2")],
                Services = [new SelectListItem("Docking", "1")],
                ServiceEntries = [new VesselScheduleServiceEntryViewModel
                {
                    VesselScheduleServiceEntryId = 7, TugboatId = 1, ServiceId = 1,
                    ManualTariffRate = 0m, ManualBafRate = 29000m, BafMethod = BafCalculationMethod.PerMove
                }]
            };
            var data = new ViewDataDictionary<VesselScheduleViewModel>(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
            data.ModelState.AddModelError("", "Save <failed>");
            var engine = services.GetRequiredService<IRazorViewEngine>();
            var view = engine.GetView(null, "/Areas/MSAP/Views/VesselSchedule/_ScheduleForm.cshtml", false);
            using var output = new StringWriter();
            await view.View!.RenderAsync(new ViewContext(context, view.View, data,
                services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
            string html = output.ToString();
            Check(html.Contains("Save for reviewing", StringComparison.Ordinal) && html.Contains("__RequestVerificationToken", StringComparison.Ordinal), "Schedule save action or anti-forgery token is missing.");
            Check(Regex.Matches(html, "name=\"ServiceEntries.Index\"").Count == 2, "Saved service rows and the new-row template must retain collection binding indices.");
            Check(Regex.IsMatch(html, "name=\"ServiceEntries\\[[^\\]]+\\].ManualTariffRate\" value=\"0.00\""), "A zero manual rate was lost when rendering.");
            Check(Regex.IsMatch(html, "name=\"ServiceEntries\\[[^\\]]+\\].VesselScheduleServiceEntryId\" value=\"7\""), "Saved row identity was lost.");
            Check(html.Contains("Save &lt;failed&gt;", StringComparison.Ordinal) && html.Contains("Tug &lt;A&gt;", StringComparison.Ordinal), "Validation or lookup text was not encoded.");
            Check(html.Contains("2026-10-17T01:00", StringComparison.Ordinal), "Overnight planned end was lost.");
            Check(html.Contains("data-rate-url=\"/MSAP/VesselSchedule/GetTariffRate\"", StringComparison.Ordinal), "Rate lookup left the MSAP area.");
            Console.WriteLine("PASS: schedule form Razor rendering preserves service IDs, zero/manual rates, overnight dates, encoded errors, anti-forgery and module routes.");

            var tug = new Tugboat { TugboatId = 1, TugboatName = "Tug <A>", IsCompanyOwned = true };
            var service = new Service { ServiceId = 1, ServiceName = "Docking <review>" };
            var schedule = new VesselSchedule
            {
                VesselScheduleId = 5, CustomerId = 1, Vessel = new Vessel { VesselName = "Vessel A" },
                Customer = new Customer { CustomerName = "Customer <A>", CustomerTin = "000-000", CustomerTerms = "COD" },
                Port = new Port { PortName = "Port A" }, Terminal = new Terminal { TerminalName = "Terminal A" },
                PlannedStart = model.PlannedStart, PlannedEnd = model.PlannedEnd, CreatedDate = model.PlannedStart,
                CosDiscountPercent = 12.5m, SbmaEnabled = true, Notes = "Coordinate <safely>",
                ServiceEntries = [new VesselScheduleServiceEntry
                {
                    TugboatId = 1, Tugboat = tug, ServiceId = 1, Service = service,
                    TariffBasis = TariffChargeBasis.PerHour, BafMethod = BafCalculationMethod.PerMove,
                    ManualTariffRate = 0m, ManualBafRate = 29000m
                }]
            };
            foreach ((bool hasCos, bool reapproval) in new[] { (false, false), (true, false), (true, true) })
            {
                schedule.CustomerOrderSlipId = hasCos ? 7 : null;
                schedule.Revision = reapproval ? 2 : 1;
                schedule.ApprovedRevision = reapproval ? 1 : null;
                var reviewData = new ViewDataDictionary<VesselSchedule>(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = schedule };
                reviewData["AssignedTugboatModels"] = new List<Tugboat> { tug };
                reviewData["CanConfirm"] = true;
                reviewData["JobProgress"] = new JobProgressViewModel { HasSchedule = true, VesselScheduleId = 5, Stage = 1 };
                reviewData["JobProgressActionStage"] = 1;
                var reviewView = engine.GetView(null, "/Areas/MSAP/Views/VesselSchedule/Details.cshtml", false);
                using var reviewOutput = new StringWriter();
                await reviewView.View!.RenderAsync(new ViewContext(context, reviewView.View, reviewData,
                    services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), reviewOutput, new HtmlHelperOptions()));
                string reviewHtml = reviewOutput.ToString();
                Check(reviewHtml.Contains("Customer &lt;A&gt;", StringComparison.Ordinal) && reviewHtml.Contains("Coordinate &lt;safely&gt;", StringComparison.Ordinal), "Review text was not encoded.");
                Check(reviewHtml.Contains("Tug &lt;A&gt;", StringComparison.Ordinal) && reviewHtml.Contains("Docking &lt;review&gt;", StringComparison.Ordinal), "Review did not group the recorded service under its tug.");
                Check(reviewHtml.Contains(">0.00</span>", StringComparison.Ordinal) && reviewHtml.Contains("29,000.00", StringComparison.Ordinal), "Review lost zero/manual rates.");
                Check(reviewHtml.Contains("__RequestVerificationToken", StringComparison.Ordinal) && reviewHtml.Contains("name=\"reviewedAt\"", StringComparison.Ordinal), "Review is missing approval protections.");
                Check(reviewHtml.Contains(reapproval ? "Reapprove schedule" : "Approve schedule", StringComparison.Ordinal), "Review action does not match revision state.");
                Check(reviewHtml.Contains($"data-reapproval=\"{reapproval.ToString().ToLowerInvariant()}\"", StringComparison.Ordinal)
                    && reviewHtml.Contains(reapproval ? "Pending reapproval" : "Pending review", StringComparison.Ordinal), "Review confirmation and status do not match approval history.");
                Check(Regex.Matches(reviewHtml, "aria-current=\"step\"").Count == 1 && reviewHtml.Contains("COS approval", StringComparison.Ordinal), "Review must show COS approval as its single current progress step.");
            }
            schedule.CustomerOrderSlipId = null;
            var viewerData = new ViewDataDictionary<VesselSchedule>(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = schedule };
            viewerData["AssignedTugboatModels"] = new List<Tugboat> { tug };
            viewerData["CanConfirm"] = false;
            viewerData["JobProgress"] = new JobProgressViewModel { HasSchedule = true, VesselScheduleId = 5, Stage = 1 };
            var detailsView = engine.GetView(null, "/Areas/MSAP/Views/VesselSchedule/Details.cshtml", false);
            using var viewerOutput = new StringWriter();
            await detailsView.View!.RenderAsync(new ViewContext(context, detailsView.View, viewerData,
                services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), viewerOutput, new HtmlHelperOptions()));
            Check(!viewerOutput.ToString().Contains("id=\"confirmSchedule\"", StringComparison.Ordinal)
                && viewerOutput.ToString().Contains("Waiting for an authorized reviewer", StringComparison.Ordinal), "A viewer received an approval action.");
            foreach (string status in new[] { MsapConstants.VesselScheduleStatus.Confirmed, MsapConstants.VesselScheduleStatus.Cancelled })
            {
                schedule.Status = status;
                schedule.CustomerOrderSlipId = status == MsapConstants.VesselScheduleStatus.Confirmed ? 7 : null;
                schedule.ApprovedRevision = schedule.Revision;
                viewerData["CanConfirm"] = true;
                viewerData["JobProgress"] = JobProgressCalculator.Calculate(schedule.CustomerOrderSlipId.HasValue
                    ? new CustomerOrderSlip { CustomerOrderSlipId = 7, Status = MsapConstants.CustomerOrderSlipStatus.Open } : null, schedule, [], []);
                detailsView = engine.GetView(null, "/Areas/MSAP/Views/VesselSchedule/Details.cshtml", false);
                using var readonlyOutput = new StringWriter();
                await detailsView.View!.RenderAsync(new ViewContext(context, detailsView.View, viewerData,
                    services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), readonlyOutput, new HtmlHelperOptions()));
                Check(!readonlyOutput.ToString().Contains("id=\"confirmSchedule\"", StringComparison.Ordinal)
                    && !readonlyOutput.ToString().Contains("Ready for review", StringComparison.Ordinal), "Approved/closed details still showed pending approval.");
            }
            schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
            schedule.CustomerOrderSlipId = 7;
            schedule.ServiceEntries[0].VesselScheduleServiceEntryId = 12;
            var cos = new CustomerOrderSlip
            {
                CustomerOrderSlipId = 7, CustomerOrderSlipNumber = "COS-2026-0001", Status = MsapConstants.CustomerOrderSlipStatus.Open,
                Customer = schedule.Customer!, Vessel = schedule.Vessel, Port = schedule.Port, Terminal = schedule.Terminal,
                Date = new DateOnly(2026, 10, 16)
            };
            var cosData = new ViewDataDictionary<CustomerOrderSlip>(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = cos };
            cosData["VesselSchedule"] = schedule;
            cosData["HasBilling"] = false;
            cosData["CanCreateDispatchTicket"] = true;
            cosData["CanEditCustomerOrderSlip"] = false;
            cosData["CanSetTariff"] = false;
            cosData["CanApproveTariff"] = false;
            cosData["JobProgress"] = JobProgressCalculator.Calculate(cos, schedule, [], []);
            var cosRoute = new RouteData();
            cosRoute.Values["area"] = "MSAP";
            cosRoute.Values["controller"] = "CustomerOrderSlip";
            cosRoute.Values["action"] = "Details";
            http.Request.RouteValues = cosRoute.Values;
            var cosDescriptor = services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>().First(a => a.RouteValues["area"] == "MSAP"
                    && a.ControllerName == "CustomerOrderSlip" && a.ActionName == "Details");
            var cosContext = new ActionContext(http, cosRoute, cosDescriptor);
            var cosView = engine.GetView(null, "/Areas/MSAP/Views/CustomerOrderSlip/Details.cshtml", false);
            using var cosOutput = new StringWriter();
            await cosView.View!.RenderAsync(new ViewContext(cosContext, cosView.View, cosData,
                services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), cosOutput, new HtmlHelperOptions()));
            Check(cosOutput.ToString().Contains("id=\"cosServices\"", StringComparison.Ordinal)
                && cosOutput.ToString().Contains("expectedServiceEntryId=12", StringComparison.Ordinal)
                && cosOutput.ToString().Contains("Awaiting ticket", StringComparison.Ordinal), "COS omitted pending service requirements or their actual-entry links.");
            Console.WriteLine("PASS: full schedule/revision review renders tug services, manual/zero rates, encoded details, current approval stage and protected approval actions.");
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
