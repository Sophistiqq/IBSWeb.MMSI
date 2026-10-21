using System.Security.Claims;
using System.Text.RegularExpressions;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Checks.MSAP
{
    internal static class CalendarRenderingCheck
    {
        public static async Task RunAsync(IServiceProvider services, string? previewDirectory)
        {
            var port = new Port { PortId = 1, PortName = "Port A" };
            var terminal = new Terminal { TerminalId = 1, TerminalName = "Terminal A", Port = port };
            var customer = new Customer { CustomerId = 1, CustomerName = "Example Customer <review>" };
            var model = new VesselScheduleBoardViewModel
            {
                Date = new DateTime(2026, 10, 1), SelectedDate = new DateTime(2026, 10, 16),
                PortId = 1, TerminalId = 1, Search = "Example", LocationHeading = "Port A / Terminal A",
                Ports = [new SelectListItem("Port A", "1")], Terminals = [new SelectListItem("Terminal A", "1")],
                Tugboats = [new Tugboat { TugboatId = 1, TugboatName = "Tugboat A", TugboatNumber = "001" },
                    new Tugboat { TugboatId = 2, TugboatName = "Tugboat B", TugboatNumber = "002" }]
            };
            for (int i = 1; i <= 6; i++)
            {
                model.Schedules.Add(new VesselSchedule
                {
                    VesselScheduleId = i,
                    Vessel = new Vessel { VesselName = $"Example Vessel {i}" }, Customer = customer, Port = port, Terminal = terminal,
                    PlannedStart = new DateTime(2026, 10, 16, i + 6, 0, 0), PlannedEnd = new DateTime(2026, 10, 16, i + 8, 0, 0),
                    AssignedTugboatIds = i == 2 ? null : "[1]", RequiredTugCount = i == 2 ? 2 : 1,
                    Status = i == 1 ? "In Progress" : "Tentative", VoyageNumber = "EXAMPLE-001"
                });
            }
            model.TugBookings = model.Schedules.ToList();
            model.ConflictsByDay[model.SelectedDate.Value] = [1, 3];
            var engine = services.GetRequiredService<IRazorViewEngine>();
            var http = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };
            var route = new RouteData();
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Calendar rendering check"));
            route.Values["area"] = "MSAP";
            route.Values["controller"] = "VesselSchedule";
            route.Values["action"] = "Index";
            http.Request.RouteValues = route.Values;
            ControllerActionDescriptor descriptor = services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>().First(a => a.ControllerName == "VesselSchedule" && a.ActionName == "Index");
            var context = new ActionContext(http, route, descriptor);
            var metadata = services.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>();
            foreach (bool showPanel in new[] { true, false })
            {
                var view = engine.GetView(null, "/Areas/MSAP/Views/VesselSchedule/Index.cshtml", false);
                model.SelectedDate = showPanel ? new DateTime(2026, 10, 16) : null;
                using var output = new StringWriter();
                var data = new ViewDataDictionary<VesselScheduleBoardViewModel>(metadata, new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()) { Model = model };
                await view.View!.RenderAsync(new ViewContext(context, view.View, data,
                    services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
                string html = output.ToString();
                Check(html.Contains("Example Customer &lt;review&gt;", StringComparison.Ordinal), "Preview text was not encoded.");
                Check(html.Contains("Port A / Terminal A", StringComparison.Ordinal), "Calendar location is missing.");
                Check(Regex.Matches(html, "\\sid=\"schedule-preview-1\"").Count == 1, "A booking has duplicate preview IDs.");
                Check(html.Contains("search=Example", StringComparison.Ordinal) && html.Contains("portId=1", StringComparison.Ordinal), "Calendar links lost filters.");
                Check(html.Contains("/MSAP/VesselSchedule", StringComparison.Ordinal) && !html.Contains("/User/VesselSchedule", StringComparison.Ordinal), "Calendar links left the MSAP area.");
                string thisMonth = Regex.Match(html, "<a[^>]+href=\"([^\"]+)\"[^>]*>This month</a>").Groups[1].Value;
                Check(!thisMonth.Contains("month=", StringComparison.Ordinal) && !thisMonth.Contains("date=", StringComparison.Ordinal), "Month navigation mutated shared filter values.");
                Check(html.Contains("id=\"dayAgenda\"", StringComparison.Ordinal) == showPanel, "Day panel does not follow the selected date.");
                if (previewDirectory != null)
                {
                    Directory.CreateDirectory(previewDirectory);
                    string document = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
                        + "<link rel=\"stylesheet\" href=\"/msap/lib/bootstrap/dist/css/bootstrap.css\">"
                        + "<link rel=\"stylesheet\" href=\"/msap/css/modern-ui.css\"></head><body>"
                        + html + "<script src=\"/msap/js/vessel-schedule-calendar.js\"></script></body></html>";
                    await File.WriteAllTextAsync(Path.Combine(previewDirectory, showPanel ? "calendar-day.html" : "calendar.html"), document);
                }
            }
            Console.WriteLine("PASS: calendar/day Razor rendering, escaped preview content, unique preview IDs and preserved filter links.");
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
