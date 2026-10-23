using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Checks.MSAP
{
    internal static class MasterFileRenderingCheck
    {
        public static async Task RunAsync(IServiceProvider services)
        {
            (string Controller, object Model)[] files =
            [
                ("Customer", new CustomerViewModel()), ("MaritimeService", new MaritimeServiceViewModel()),
                ("Port", new PortViewModel()), ("Terminal", new TerminalViewModel()),
                ("Vessel", new VesselViewModel()), ("Tugboat", new TugboatViewModel()),
                ("TugboatOwner", new TugboatOwnerViewModel()), ("TugMaster", new TugMasterViewModel()),
                ("Principal", new PrincipalViewModel()), ("BankAccount", new BankAccountViewModel())
            ];
            string outputDirectory = Path.Combine(Path.GetTempPath(), "ibs-master-file-render-check");
            Directory.CreateDirectory(outputDirectory);
            var engine = services.GetRequiredService<IRazorViewEngine>();
            foreach ((string controller, object model) in files)
            {
                string[] actions = controller == "Customer"
                    ? ["Index", "Create", "Edit", "Activate", "Deactivate", "ExportIndex"]
                    : ["Index", "Create", "Edit"];
                foreach (string action in actions)
                {
                    var http = new DefaultHttpContext { RequestServices = services };
                    http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Master-file render check"));
                    var route = new RouteData();
                    route.Values["area"] = "MSAP";
                    route.Values["controller"] = controller;
                    route.Values["action"] = action;
                    http.Request.RouteValues = route.Values;
                    var context = new ActionContext(http, route, new ActionDescriptor());
                    var view = engine.GetView(null, $"/Areas/MSAP/Views/{controller}/{action}.cshtml", false);
                    var data = new ViewDataDictionary(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
                    data.ModelState.AddModelError("", "Check <validation>");
                    using var output = new StringWriter();
                    await view.View!.RenderAsync(new ViewContext(context, view.View, data,
                        services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
                    string html = output.ToString();
                    if (!html.Contains("app-ui") || !html.Contains("ui-page-action")
                        || action is "Create" or "Edit" && (!html.Contains("__RequestVerificationToken") || !html.Contains("Check &lt;validation&gt;"))
                        || controller == "Principal" && html.Contains("name=\"Mobile1\"")
                        || controller == "TugboatOwner" && html.Contains("name=\"FixedRate\""))
                    {
                        throw new InvalidOperationException($"Master-file rendering failed: {controller}/{action}");
                    }
                    await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{controller}-{action}.html"), html);
                }
            }
            Console.WriteLine($"PASS: 33 master-file pages render with shared actions, escaped validation and antiforgery tokens. HTML: {outputDirectory}");
        }
    }
}
