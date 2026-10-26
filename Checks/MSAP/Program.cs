using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.Models;
using IBS.Models.MSAP;
using IBS.Services.MSAP;
using IBSWeb.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Contains("--rebuild-database", StringComparer.Ordinal))
{
    await Checks.MSAP.RebuildDatabaseCheck.RunAsync();
    return;
}

if (args.Contains("--source-database-audit", StringComparer.Ordinal))
{
    await Checks.MSAP.RebuildDatabaseCheck.AuditSourceAsync();
    return;
}

if (args.Contains("--workspace-only", StringComparer.Ordinal))
{
    await Checks.MSAP.TugboatWorkspaceCheck.RunAsync();
    return;
}

if (args.Contains("--tugboat-owner-database", StringComparer.Ordinal))
{
    await Checks.MSAP.TugboatOwnerCheck.RunAsync();
    return;
}

if (args.Contains("--ticket-integrity-database", StringComparer.Ordinal))
{
    await Checks.MSAP.TicketIntegrityDatabaseCheck.RunAsync();
    return;
}

if (args.Contains("--billing-integrity-database", StringComparer.Ordinal))
{
    await Checks.MSAP.TicketIntegrityDatabaseCheck.RunAsync(includeBilling: true);
    return;
}

if (args.Contains("--tugboat-payment-database", StringComparer.Ordinal))
{
    await Checks.MSAP.TugboatPaymentDatabaseCheck.RunAsync();
    return;
}

if (args.Contains("--other-tug-progress-database", StringComparer.Ordinal))
{
    await Checks.MSAP.OtherTugProgressDatabaseCheck.RunAsync();
    return;
}

if (args.Contains("--workspace-database", StringComparer.Ordinal))
{
    await Checks.MSAP.TugboatWorkspaceDatabaseCheck.RunAsync();
    return;
}

if (args.Contains("--master-import-only", StringComparer.Ordinal))
{
    await Checks.MSAP.MasterFileImportCheck.RunAsync();
    return;
}

if (args.Contains("--workflow-access-only", StringComparer.Ordinal))
{
    await Checks.MSAP.WorkflowAccessCheck.RunAsync();
    await Checks.MSAP.BillingDiscardCheck.RunAsync();
    await Checks.MSAP.RateAccessCheck.RunAsync();
    Checks.MSAP.JobProgressCheck.Run();
    return;
}

if (args.Contains("--ar-monitoring-only", StringComparer.Ordinal))
{
    await Checks.MSAP.ArMonitoringCheck.RunAsync();
    return;
}

if (args.Contains("--accounting-gaps-only", StringComparer.Ordinal))
{
    Checks.MSAP.AccountingGapsCheck.Run();
    return;
}

if (args.Contains("--schedule-rates-only", StringComparer.Ordinal))
{
    await Checks.MSAP.ScheduleRateCheck.RunAsync();
    return;
}

if (args.Contains("--operational-rates-database", StringComparer.Ordinal))
{
    await Checks.MSAP.OperationalRateDatabaseCheck.RunAsync();
    return;
}

if (args.Contains("--other-tugs-only", StringComparer.Ordinal))
{
    await Checks.MSAP.OtherTugChargeCheck.RunAsync();
    await Checks.MSAP.RateAccessCheck.RunAsync();
    return;
}

if (args.Contains("--workflow-gaps-only", StringComparer.Ordinal))
{
    await Checks.MSAP.WorkflowGapCheck.RunAsync();
    return;
}

if (args.Contains("--collection-only", StringComparer.Ordinal))
{
    await Checks.MSAP.CollectionCheck.RunAsync();
    return;
}

if (args.Contains("--urgent-entry-only", StringComparer.Ordinal))
{
    Checks.MSAP.JobProgressCheck.Run();
    Checks.MSAP.UrgentEntryCheck.Run();
    await Checks.MSAP.UrgentEntryCheck.RunSaveAsync();
    return;
}

if (args.Contains("--rate-access-only", StringComparer.Ordinal))
{
    await Checks.MSAP.RateAccessCheck.RunAsync();
    return;
}

if (args.Contains("--rate-export-only", StringComparer.Ordinal))
{
    await Checks.MSAP.TariffExportCheck.RunAsync();
    return;
}

Checks.MSAP.JobProgressCheck.Run();
await Checks.MSAP.CollectionCheck.RunAsync();

var webRoot = Path.GetFullPath("IBSWeb");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ApplicationName = typeof(MsapModule).Assembly.GetName().Name,
    ContentRootPath = webRoot,
    EnvironmentName = "Development"
});
builder.Logging.SetMinimumLevel(LogLevel.Error);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:MMSIConnection"] = args.Contains("--rebuild-render-only", StringComparer.Ordinal)
        || args.Contains("--master-access-database", StringComparer.Ordinal)
        ? Checks.MSAP.TugboatOwnerCheck.TestConnection()
        : "Host=localhost;Database=msap_check;Username=postgres"
});
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("MMSIConnection")));
builder.Services.AddScoped<IBS.Utility.TugboatWorkspace>();
builder.Services.AddScoped<IBS.Services.TugboatPaymentService>();
builder.Services.AddScoped<IBS.Services.ICloudStorageService, IBS.Services.CloudStorageService>();
builder.Services.AddScoped<IBS.Services.CheckVoucherDocumentationService>();
builder.Services.AddScoped<IBS.Services.TugboatWorkspaceService>();
builder.Services.AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddScoped<IBS.DataAccess.Repository.IRepository.IUnitOfWork, IBS.DataAccess.Repository.UnitOfWork>();
builder.Services.AddMsapModule(builder.Configuration);
if (args.Contains("--calendar-render-only", StringComparer.Ordinal)
    || args.Contains("--schedule-render-only", StringComparer.Ordinal)
    || args.Contains("--rebuild-render-only", StringComparer.Ordinal)
    || args.Contains("--master-render-only", StringComparer.Ordinal)
    || args.Contains("--master-access-database", StringComparer.Ordinal))
{
    builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();
}
if (!args.Contains("--master-access-database", StringComparer.Ordinal))
{
    builder.Services.AddSingleton<IBS.Services.MSAP.AccessControl.IAccessControlService, Checks.MSAP.JobProgressCheck.CheckAccess>();
}
await using var app = builder.Build();
app.UseRouting();
#pragma warning disable ASP0014 // Populate routing without starting a server.
app.UseEndpoints(endpoints => endpoints.MapControllerRoute("default", "{area=User}/{controller=Home}/{action=Index}/{id?}"));
#pragma warning restore ASP0014
using var scope = app.Services.CreateScope();
var services = scope.ServiceProvider;
if (args.Contains("--master-access-database", StringComparer.Ordinal))
{
    await Checks.MSAP.MasterFileAccessDatabaseCheck.RunAsync(services);
    return;
}
if (args.Contains("--master-render-only", StringComparer.Ordinal))
{
    await Checks.MSAP.MasterFileRenderingCheck.RunAsync(services);
    return;
}
if (args.Contains("--rebuild-render-only", StringComparer.Ordinal))
{
    await Checks.MSAP.RebuildRenderingCheck.RunAsync(services);
    return;
}
if (args.Contains("--schedule-render-only", StringComparer.Ordinal))
{
    await Checks.MSAP.ScheduleFormRenderingCheck.RunAsync(services);
    return;
}

var module = services.GetRequiredService<MsapDbContext>();
var original = services.GetRequiredService<ApplicationDbContext>();
void Check(bool passed, string message)
{
    if (!passed)
    {
        throw new InvalidOperationException(message);
    }
}

var originalTables = original.Model.GetRelationalModel().Tables.Select(t => (Schema: t.Schema ?? "public", t.Name)).ToHashSet();
var moduleTables = module.Model.GetRelationalModel().Tables;
Check(moduleTables.Where(t => t.Schema == "msap").All(t => !t.Name.StartsWith("msap_", StringComparison.Ordinal)), "An MSAP table still uses the old prefix.");
Check(moduleTables.Where(t => t.Schema == "msap").All(t => !originalTables.Contains((t.Schema!, t.Name))), "MSAP owns a base table.");
Check(module.Model.FindEntityType(typeof(AuditTrail))?.GetSchema() == "msap", "MSAP audits are not isolated.");
Check(original.Model.FindEntityType(typeof(AuditTrail)) == null, "Base context discovered MSAP audits.");
Check(module.Model.GetEntityTypes().Where(e => e.ClrType.Namespace?.StartsWith("IBS.Models.MSAP", StringComparison.Ordinal) == true)
    .All(e => e.GetSchema() == "msap"), "An MSAP entity maps outside its schema.");
var sql = module.GetService<IMigrator>().GenerateScript();
Check(!sql.Contains("CREATE TABLE public.", StringComparison.Ordinal) && !sql.Contains("ALTER TABLE public.", StringComparison.Ordinal), "MSAP migration writes shared tables.");
Check(sql.Contains("msap.audit_trails", StringComparison.Ordinal), "Audit migration is missing.");
Check(services.GetRequiredService<IBS.DataAccess.Repository.IRepository.IUnitOfWork>().GetType() == typeof(IBS.DataAccess.Repository.UnitOfWork), "Base unit of work was replaced.");
Check(services.GetRequiredService<IBS.DataAccess.MSAP.Repository.IRepository.IUnitOfWork>().GetType() == typeof(IBS.DataAccess.MSAP.Repository.UnitOfWork), "MSAP unit of work is missing.");

var actions = services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToList();
var moduleActions = actions.Where(a => a.ControllerTypeInfo.Namespace?.StartsWith("IBSWeb.Areas.MSAP", StringComparison.Ordinal) == true).ToList();
Check(moduleActions.Select(a => a.ControllerTypeInfo).Distinct().Count() == 31,
    "Expected 31 active MSAP controllers; found " + moduleActions.Select(a => a.ControllerTypeInfo).Distinct().Count()
    + ": " + string.Join(", ", moduleActions.Select(a => a.ControllerName).Distinct().Order()));
Check(!moduleActions.Any(a => a.ControllerName == "Data"), "Retired direct data changes are exposed as endpoints.");
Check(moduleActions.All(a => a.RouteValues["area"] is "MSAP" or "MSAPAdmin" or "MSAPSuperAdmin"), "An MSAP controller collides with a base area.");
Check(moduleActions.All(action => action.FilterDescriptors.Any(filter =>
    filter.Filter is AuthorizeFilter authorize && authorize.AuthorizeData?.Any(data => data.Policy == MsapRoles.AccessPolicy) == true)),
    "An MSAP endpoint lacks the module membership policy.");
Check(!actions.Where(action => action.RouteValues["area"] == "Filpride").Any(action => action.FilterDescriptors.Any(filter =>
    filter.Filter is AuthorizeFilter authorize && authorize.AuthorizeData?.Any(data => data.Policy == MsapRoles.AccessPolicy) == true)),
    "MSAP membership policy leaked into Filpride.");
Check(!moduleActions.Any(action => action.ControllerTypeInfo.GetCustomAttributes(typeof(AuthorizeAttribute), true)
    .Cast<AuthorizeAttribute>().Any(authorize => !string.IsNullOrEmpty(authorize.Roles))),
    "MSAP still requires shared IBSWeb roles.");
Check(moduleActions.Where(action => action.ControllerName == "PaymentTerms")
    .All(action => action.ActionName is "Index" or "GetPaymentTerms"), "MSAP still exposes payment term maintenance.");
Check(!moduleActions.Any(action => action.ControllerName == "User"), "MSAP still exposes shared account management.");
Check(actions.Any(action => action.RouteValues["area"] == "Admin" && action.ControllerName == "User"),
    "Shared IBSWeb/Filpride user management was removed.");
foreach (var controller in moduleActions.Select(a => a.ControllerTypeInfo.AsType()).Distinct())
{
    ActivatorUtilities.CreateInstance(services, controller);
}
foreach (var group in actions.Where(a => a.AttributeRouteInfo == null).GroupBy(a => (a.RouteValues["area"], a.ControllerName)))
{
    Check(group.Select(a => a.ControllerTypeInfo).Distinct().Count() == 1, $"Controller route collision: {group.Key}");
}
var links = services.GetRequiredService<LinkGenerator>();
Check(links.GetPathByAction("Index", "Home", new { area = "MSAP" })?.StartsWith("/MSAP", StringComparison.Ordinal) == true, "MSAP dashboard link is wrong.");
Check(links.GetPathByAction("GetTimeline", "AuditTrail", new { area = "MSAP", id = 1 }) == "/MSAP/AuditTrail/GetTimeline/1", "MSAP audit link is wrong.");
Check(links.GetPathByAction("Index", "Home", new { area = "User" }) != null, "Base dashboard route is missing.");
Check(links.GetPathByAction("Details", "CustomerOrderSlip", new { area = "MSAP", id = 1 }) == "/MSAP/COS/Details/1", "COS links did not use the renamed route.");
var engine = services.GetRequiredService<IRazorViewEngine>();
await Checks.MSAP.ScheduleFormRenderingCheck.RunAsync(services);
await Checks.MSAP.CalendarRenderingCheck.RunAsync(services, Environment.GetEnvironmentVariable("MSAP_CALENDAR_PREVIEW_DIRECTORY"));
if (args.Contains("--calendar-render-only", StringComparer.Ordinal))
{
    return;
}
foreach (string customerAction in new[] { "Create", "Edit" })
{
    var http = new DefaultHttpContext { RequestServices = services };
    http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Customer discount rendering check"));
    var context = new ActionContext(http, new RouteData(), moduleActions.First(a => a.ControllerName == "Customer" && a.ActionName == customerAction));
    context.RouteData.Values["area"] = "MSAP";
    context.RouteData.Values["controller"] = "Customer";
    context.RouteData.Values["action"] = customerAction;
    var data = new ViewDataDictionary<IBS.Models.MSAP.MasterFile.CustomerViewModel>(services.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
    {
        Model = new IBS.Models.MSAP.MasterFile.CustomerViewModel { DefaultCosDiscountPercent = 12.5m }
    };
    using var output = new StringWriter();
    var view = engine.GetView(null, $"/Areas/MSAP/Views/Customer/{customerAction}.cshtml", false);
    await view.View!.RenderAsync(new ViewContext(context, view.View, data,
        services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
    string html = output.ToString();
    Check(html.Contains("name=\"DefaultCosDiscountPercent\"", StringComparison.Ordinal) && html.Contains("data-val-range-max=\"100\"", StringComparison.Ordinal)
        && html.Contains("data-val-range-min=\"0\"", StringComparison.Ordinal), "Customer discount field or client validation is missing.");
}
Console.WriteLine("PASS: customer create/edit render the default COS discount and client validation.");
{
    var controller = ActivatorUtilities.CreateInstance<IBSWeb.Areas.MSAP.Controllers.CollectionController>(services);
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } };
    var values = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
    {
        ["Revision"] = "9", ["ApprovedRevision"] = "9", ["ApprovedByUserId"] = "forged-approver", ["ApprovedAt"] = "2030-01-01T07:00:00"
    });
    var provider = new Microsoft.AspNetCore.Mvc.ModelBinding.FormValueProvider(
        Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Form, values, System.Globalization.CultureInfo.InvariantCulture);
    var bill = new Billing();
    await controller.TryUpdateModelAsync(bill, "", provider);
    var schedule = new VesselSchedule();
    await controller.TryUpdateModelAsync(schedule, "", provider);
    Check(bill.Revision == 1 && bill.ApprovedRevision == null && bill.ApprovedByUserId == null && bill.ApprovedAt == null
        && schedule.Revision == 1 && schedule.ApprovedRevision == null && schedule.ApprovedByUserId == null && schedule.ApprovedAt == null,
        "Form binding accepted forged approval evidence.");
    var jsonBill = JsonSerializer.Deserialize<Billing>("{\"Revision\":9,\"ApprovedRevision\":9,\"ApprovedByUserId\":\"forged-approver\",\"ApprovedAt\":\"2030-01-01T07:00:00\"}")!;
    Check(jsonBill.Revision == 1 && jsonBill.ApprovedRevision == null && jsonBill.ApprovedByUserId == null && jsonBill.ApprovedAt == null,
        "JSON binding accepted forged approval evidence.");
}
Console.WriteLine("PASS: form/JSON inputs cannot forge revision or approval evidence.");
foreach (string emptyAmount in new[] { "", "0.00" })
{
    var controller = ActivatorUtilities.CreateInstance<IBSWeb.Areas.MSAP.Controllers.CollectionController>(services);
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } };
    var values = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
    {
        ["Date"] = "2026-10-12", ["CustomerId"] = "1", ["CashAmount"] = emptyAmount,
        ["CheckAmount"] = "100.00", ["WVAT"] = emptyAmount, ["Amount"] = "100.00", ["EWT"] = "0.00"
    });
    var provider = new Microsoft.AspNetCore.Mvc.ModelBinding.FormValueProvider(
        Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Form, values, System.Globalization.CultureInfo.InvariantCulture);
    bool valid = await controller.TryUpdateModelAsync(new IBS.Models.MSAP.ViewModels.CreateCollectionViewModel(), "", provider);
    Check(valid == (emptyAmount != ""), "Optional payment amounts must bind after blank inputs are submitted as zero.");
    if (emptyAmount == "")
    {
        Check(controller.ModelState.ErrorCount == 2
            && controller.ModelState["CashAmount"]!.Errors.Count == 1
            && controller.ModelState["WVAT"]!.Errors.Count == 1, "The two empty-value errors must be attributable to payment amounts.");
    }
}
Console.WriteLine("PASS: MVC binding reproduces two empty payment errors and accepts zero for unused amounts.");
var bankingView = engine.GetView(null, "/Areas/MSAP/Views/Collection/_Banking.cshtml", false);
Check(bankingView.Success, "Collection banking partial is missing.");
foreach (var status in new[] { "For Deposit", "Deposited", "Returned", "Redeposited", "Cleared" })
{
    var date = DateOnly.FromDateTime(DateTime.Today);
    var receipt = new Collection
    {
        MsapCollectionId = 1, Date = date, CheckAmount = 100,
        BankId = status == "Returned" ? 8 : null,
        DepositDate = status is "Deposited" or "Redeposited" or "Cleared" ? date : null,
        ReturnedDate = status is "Returned" or "Redeposited" ? date : null,
        ClearedDate = status == "Cleared" ? date : null
    };
    var http = new DefaultHttpContext { RequestServices = services };
    http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Collection banking check"));
    var actionContext = new ActionContext(http, new RouteData(), moduleActions.First(a => a.ControllerName == "Collection"));
    actionContext.RouteData.Values["area"] = "MSAP";
    actionContext.RouteData.Values["controller"] = "Collection";
    actionContext.RouteData.Values["action"] = "Preview";
    var viewData = new ViewDataDictionary<Collection>(services.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()) { Model = receipt };
    viewData["BankingAction"] = status is "For Deposit" or "Returned" ? IBS.Models.MSAP.Enums.CollectionBankingAction.Deposit
        : IBS.Models.MSAP.Enums.CollectionBankingAction.Clear;
    viewData["BankAccounts"] = new List<SelectListItem>
    {
        new() { Value = "7", Text = "MMSI Bank" },
        new() { Value = "8", Text = "Another MMSI Bank" }
    };
    using var output = new StringWriter();
    var viewContext = new ViewContext(actionContext, bankingView.View!, viewData,
        services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions());
    await bankingView.View!.RenderAsync(viewContext);
    var html = output.ToString();
    if (status is "For Deposit" or "Returned")
    {
        if (status == "Returned")
        {
            Check(Regex.IsMatch(html, "<option value=\"8\" selected(?:=\"selected\")?>"), "Retain the saved receiving bank on redeposit.");
        }
        else
        {
            Check(!Regex.IsMatch(html, "<option value=\"[78]\" selected(?:=\"selected\")?>"),
                "A new deposit with multiple accounts must require an explicit bank choice.");
        }
        Check(html.Contains("id=\"depositBank\"", StringComparison.Ordinal) && html.Contains("Another MMSI Bank", StringComparison.Ordinal), "The bank dropdown must retain all available accounts.");
        Check(!html.Contains("value=\"Clear\"", StringComparison.Ordinal), "An undeposited or returned receipt must not offer clearing.");
    }
    Check(html.Contains("id=\"collectionBankingForm\"", StringComparison.Ordinal), "ModernAlert banking form is missing.");
    Check(!html.Contains("Collection → Deposit → Clear", StringComparison.Ordinal), "Banking inputs must not render another progress section.");
}
Console.WriteLine("PASS: collection banking forms retain saved accounts and require an explicit choice when multiple receiving banks are available.");
var jobProgressView = engine.GetView(null, "/Areas/MSAP/Views/Shared/_JobProgress.cshtml", false);
Check(jobProgressView.Success, "Shared Job Progress partial is missing.");
foreach (bool allowed in new[] { false, true })
{
    for (int stage = 0; stage <= JobProgressCalculator.CompletedStage + 1; stage++)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "Job Progress check"));
        if (allowed)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "progress-allowed")], "check"));
        }
        var progressRoute = new RouteData();
        progressRoute.Values["area"] = "MSAP";
        progressRoute.Values["controller"] = "Billing";
        progressRoute.Values["action"] = "Review";
        progressRoute.Values["id"] = 123;
        http.Request.RouteValues = progressRoute.Values;
        var actionContext = new ActionContext(http, progressRoute, moduleActions.First());
        var model = new IBS.Models.MSAP.ViewModels.JobProgressViewModel
        {
            Stage = stage == JobProgressCalculator.CompletedStage + 1 ? 0 : stage, HasSchedule = stage < 2, Guidance = "Attention check",
            Stopped = stage == JobProgressCalculator.CompletedStage + 1, IsCancelled = stage == JobProgressCalculator.CompletedStage + 1,
            Permission = stage == JobProgressCalculator.CompletedStage ? null : IBS.Models.MSAP.Enums.ProcedureEnum.ApproveTariff,
            WaitingFor = "tariff approval", ActionController = "DispatchTicket", ActionName = "Preview",
            TargetId = 123, ActionLabel = "Review Charges", CustomerOrderSlipId = 77,
            OtherTugTicketCount = stage >= 2 ? 3 : 0, MissingOtherTugTicketCount = 1,
            OtherTugChargeReviewCount = 1, ConfirmedOtherTugTicketCount = 1,
            MissingOtherTugTicketId = 91, OtherTugChargeReviewId = 92
        };
        var viewData = new ViewDataDictionary<IBS.Models.MSAP.ViewModels.JobProgressViewModel>(services.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()) { Model = model };
        if (stage == 1)
        {
            model.ActionController = "VesselSchedule";
            model.ActionName = "Details";
            model.VesselScheduleId = 42;
        }
        if (stage == JobProgressCalculator.PostingStage)
        {
            model.ActionController = "Billing";
            model.ActionName = "Review";
            model.CustomerOrderSlipId = 77;
            model.RecordedTicketCount = 2;
            model.ExpectedTicketCount = 2;
            model.LinkedBillings = [new Billing { MsapBillingId = 123, MsapBillingNumber = "B-CHECK", Status = MsapConstants.BillingStatus.ForPosting }];
        }
        if (stage is JobProgressCalculator.DepositStage or JobProgressCalculator.ClearingStage)
        {
            model.BankingCollection = new Collection { MsapCollectionId = 55, CheckAmount = 100 };
            model.ActionController = "Collection";
            model.ActionName = "Banking";
        }
        if (stage == JobProgressCalculator.CollectionStage)
        {
            viewData["JobProgressActionStage"] = stage;
        }
        using var output = new StringWriter();
        var viewContext = new ViewContext(actionContext, jobProgressView.View!, viewData,
            services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions());
        await jobProgressView.View!.RenderAsync(viewContext);
        string html = output.ToString();
        bool showOtherTugs = stage >= 2 && stage <= JobProgressCalculator.CompletedStage;
        Check(html.Contains("AP Other Tugs follow-up", StringComparison.Ordinal) == showOtherTugs,
            "Provider-cost follow-up must remain visible through collection/clearing and be hidden for cancelled bookings.");
        if (showOtherTugs)
        {
            Check(html.Contains("2 / 3 recorded", StringComparison.Ordinal)
                && html.Contains("Does not block customer billing.", StringComparison.Ordinal)
                && html.Contains("receipt_long", StringComparison.Ordinal), "Provider follow-up must show recorded progress, an unnumbered icon and its billing independence.");
            Check(html.IndexOf("AP Other Tugs follow-up", StringComparison.Ordinal) < html.IndexOf("</ol>", StringComparison.Ordinal),
                "Provider follow-up must be part of Job Progress without adding a numbered billing stage.");
        }
        Check(html.Contains("Record costs", StringComparison.Ordinal) == (showOtherTugs && allowed)
            && html.Contains("Review charges", StringComparison.Ordinal) == (showOtherTugs && allowed),
            "Provider input/review links ignored their permissions or disappeared after customer collection.");
        if (showOtherTugs && allowed)
        {
            Check(html.Contains("/OtherTugCharge/Editor?", StringComparison.Ordinal) && html.Contains("ticketId=91", StringComparison.Ordinal)
                && html.Contains("/OtherTugCharge/Editor/92", StringComparison.Ordinal)
                && !html.Contains("/OtherTugCharge/Editor/123", StringComparison.Ordinal)
                && html.Contains("customerOrderSlipId=77", StringComparison.Ordinal), "Provider follow-up lost the selected COS/ticket/document.");
        }
        Check(Regex.Matches(html, "aria-current=\"step\"").Count == (stage == JobProgressCalculator.CompletedStage + 1 ? 0 : 1), "Job Progress current stage is wrong.");
        Check(html.Contains("Fully cleared", StringComparison.Ordinal) && html.Contains("Job Progress", StringComparison.Ordinal), "Job Progress does not show the entire journey.");
        Check(Regex.Matches(html, "class=\"modern-btn-primary\"").Count == (allowed && stage < JobProgressCalculator.CompletedStage ? 1 : 0), "Job Progress must show exactly one permitted next action.");
        if (!allowed && stage < JobProgressCalculator.CompletedStage)
        {
            Check(html.Contains("Waiting for an authorized user", StringComparison.Ordinal), "Job Progress is missing its role handoff.");
        }
        if (allowed && stage == JobProgressCalculator.TariffStage)
        {
            Check(html.Contains("disabled", StringComparison.Ordinal), "Tariff must use the ticket row action instead of a duplicate progress action.");
        }
        if (allowed && stage == 1)
        {
            Check(html.Contains("/MSAP/VesselSchedule/Details/42", StringComparison.Ordinal) && !html.Contains("data-schedule-confirm=\"true\"", StringComparison.Ordinal), "Planning must link to the canonical schedule review page.");
        }
        if (allowed && stage == JobProgressCalculator.PostingStage)
        {
            Check(html.Contains("customerOrderSlipId=77", StringComparison.Ordinal) && html.Contains("Review", StringComparison.Ordinal)
                && !html.Contains("data-schedule-confirm=\"true\"", StringComparison.Ordinal), "Final approval must open the selected bill review and bypass confirmation.");
            Check(html.Contains("Tickets recorded", StringComparison.Ordinal) && html.Contains("Billing prepared", StringComparison.Ordinal)
                && html.Contains("2 / 2 recorded", StringComparison.Ordinal) && html.Contains("#B-CHECK", StringComparison.Ordinal),
                "Completed labels, recorded-ticket counts and individual billing links must remain visible.");
        }
        if (allowed && stage is JobProgressCalculator.DepositStage or JobProgressCalculator.ClearingStage)
        {
            Check(html.Contains("data-collection-banking=", StringComparison.Ordinal) && html.Contains("bankingAction=", StringComparison.Ordinal), "Banking actions must open ModernAlert with the chosen operation.");
            Check(html.Contains("Return Check", StringComparison.Ordinal) == (stage == JobProgressCalculator.ClearingStage), "Only a deposited check can offer a return action.");
        }
        if (allowed && stage == JobProgressCalculator.CollectionStage)
        {
            Check(html.Contains("disabled", StringComparison.Ordinal), "Collection form must use its designated save action.");
        }
        if (allowed && stage < JobProgressCalculator.CompletedStage && stage != 1 && stage != JobProgressCalculator.TariffStage && stage != JobProgressCalculator.PostingStage && stage != JobProgressCalculator.CollectionStage && stage < JobProgressCalculator.DepositStage)
        {
            Check(html.Contains("/MSAP/DispatchTicket/Preview/123", StringComparison.Ordinal), "Job Progress next action route is wrong.");
        }
    }
}
Console.WriteLine("PASS: shared Job Progress renders all eleven stages, cancellation, role handoffs and exactly one permitted next action.");
foreach (var grants in new[] { (Manage: false, Confirm: false), (Manage: true, Confirm: false), (Manage: false, Confirm: true), (Manage: true, Confirm: true) })
{
    var dashboardView = engine.GetView(null, "/Areas/MSAP/Views/Home/Index.cshtml", false);
    Check(dashboardView.Success, "MSAP dashboard view is missing.");
    var http = new DefaultHttpContext { RequestServices = services };
    http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "Provider dashboard rendering check"));
    var action = new ActionContext(http, new RouteData(), moduleActions.First());
    var model = new IBS.Models.MSAP.ViewModels.DashboardCountViewModel
    {
        ShowDashboard = true, UpdatedAt = DateTime.Today,
        CanManageOtherTugCharges = grants.Manage, CanConfirmOtherTugCharges = grants.Confirm,
        MissingOtherTugTicketCount = 3, OtherTugChargeReviewCount = 2
    };
    var data = new ViewDataDictionary<IBS.Models.MSAP.ViewModels.DashboardCountViewModel>(services.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
    using var output = new StringWriter();
    await dashboardView.View!.RenderAsync(new ViewContext(action, dashboardView.View, data,
        services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
    string html = output.ToString();
    Check(html.Contains("AP Other Tugs · Missing inputs", StringComparison.Ordinal) == grants.Manage
        && html.Contains("href=\"#missing-provider-costs\"", StringComparison.Ordinal) == grants.Manage,
        "The dashboard must show missing-cost entry only to the permitted preparer.");
    Check(html.Contains("AP Other Tugs · For confirmation", StringComparison.Ordinal) == (grants.Manage || grants.Confirm)
        && html.Contains("id=\"provider-charge-reviews\"", StringComparison.Ordinal) == (grants.Manage || grants.Confirm),
        "Provider-cost review disappeared for an authorized role or was shown without access.");
}
Console.WriteLine("PASS: dashboard AP Other Tugs reminders and entry/review queues render for preparer, confirmer, combined and denied access.");

foreach (var area in new[] { "MSAP", "MSAPAdmin", "MSAPSuperAdmin" })
{
    foreach (var view in Directory.EnumerateFiles(Path.Combine(webRoot, "Areas", area), "*.cshtml", SearchOption.AllDirectories))
    {
        foreach (Match asset in Regex.Matches(File.ReadAllText(view), "(?:src|href)=\"([^\"]+)\""))
        {
            var url = asset.Groups[1].Value;
            if (url.StartsWith("~/msap/", StringComparison.Ordinal) || url.StartsWith("/msap/", StringComparison.Ordinal))
            {
                var path = url.TrimStart('~', '/').Split('?', '#')[0];
                Check(File.Exists(Path.Combine(webRoot, "wwwroot", path)), $"Missing MSAP asset in {view}: {url}");
            }
            if (url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("//", StringComparison.Ordinal))
            {
                Check(!url.Contains("/msap/", StringComparison.Ordinal), $"MSAP prefix inserted into CDN URL: {url}");
            }
        }
    }
}
Console.WriteLine("PASS: MSAP view assets exist and CDN URLs have no module prefix.");
foreach (var area in new[] { "MSAP", "MSAPAdmin", "MSAPSuperAdmin" })
{
    var descriptor = moduleActions.First(a => a.RouteValues["area"] == area);
    var http = new DefaultHttpContext { RequestServices = services };
    var route = new RouteData();
    route.Values["area"] = area;
    route.Values["controller"] = descriptor.ControllerName;
    var actionContext = new ActionContext(http, route, descriptor);
    Check(engine.FindView(actionContext, "_Layout", false).View?.Path == "/Areas/MSAP/Views/Shared/_Layout.cshtml", "MSAP picked the base layout.");
}
var moduleAction = moduleActions.First();
var result = new JsonResult(new { Amount = 1.235m });
var filterContext = new ResultExecutingContext(new ActionContext(new DefaultHttpContext(), new RouteData(), moduleAction), [], result, new object());
new MsapJsonResultFilter().OnResultExecuting(filterContext);
Check(JsonSerializer.Serialize(result.Value, (JsonSerializerOptions)result.SerializerSettings!).Contains("1.24", StringComparison.Ordinal), "Module decimal formatting changed.");
var baseAction = actions.First(a => a.RouteValues["area"] == "User");
var baseResult = new JsonResult(new { Amount = 1.235m });
new MsapJsonResultFilter().OnResultExecuting(new ResultExecutingContext(new ActionContext(new DefaultHttpContext(), new RouteData(), baseAction), [], baseResult, new object()));
Check(baseResult.SerializerSettings == null, "MSAP changed base JSON options.");
Console.WriteLine($"PASS: {moduleActions.Select(a => a.ControllerTypeInfo).Distinct().Count()} controllers, routes, DI, view isolation, schema/migrations, audit ownership and scoped JSON.");
var storage = services.GetRequiredService<ICloudStorageService>();
string[] unsafePaths =
[
    "../outside.txt", @"..\outside.txt", "/outside.txt", @"C:\outside.txt",
    "checks/../../outside.txt", "checks/../outside.txt", "checks//file.txt",
    "checks/file.txt:stream", "checks/line\r\nbreak.txt", ""
];
foreach (string unsafePath in unsafePaths)
{
    using var content = new MemoryStream("msap"u8.ToArray());
    var upload = new FormFile(content, 0, content.Length, "check", "check.txt");
    Func<Task>[] operations =
    [
        () => storage.UploadFileAsync(upload, unsafePath),
        () => storage.GetSignedUrlAsync(unsafePath),
        () => storage.DownloadFileAsync(unsafePath),
        () => storage.DeleteFileAsync(unsafePath)
    ];
    foreach (Func<Task> operation in operations)
    {
        try
        {
            await operation();
            throw new InvalidOperationException("MSAP storage accepted an unsafe path.");
        }
        catch (ArgumentException)
        {
        }
    }
}
var fileName = $"checks/{Guid.NewGuid():N}.txt";
try
{
    using var content = new MemoryStream("msap"u8.ToArray());
    await storage.UploadFileAsync(new FormFile(content, 0, content.Length, "check", "check.txt"), fileName);
    Check((await storage.GetSignedUrlAsync(fileName)).StartsWith("/msap-storage/", StringComparison.Ordinal), "MSAP attachments use the base URL.");
    await using var download = await storage.DownloadFileAsync(fileName);
    using var reader = new StreamReader(download);
    Check(await reader.ReadToEndAsync() == "msap", "MSAP attachment round-trip failed.");
}
finally
{
    await storage.DeleteFileAsync(fileName);
}
Console.WriteLine("PASS: unsafe storage paths rejected for upload, URL, download and deletion; nested attachment round-trip.");
if (args.Contains("--database", StringComparer.Ordinal) || args.Contains("--approval-database", StringComparer.Ordinal)
    || args.Contains("--customer-discount-database", StringComparer.Ordinal) || args.Contains("--ticket-linkage-database", StringComparer.Ordinal))
{
    await Checks.MSAP.DatabaseCheck.RunAsync(storage, services.GetRequiredService<ITempDataDictionaryFactory>(),
        args.Contains("--approval-database", StringComparer.Ordinal), args.Contains("--customer-discount-database", StringComparer.Ordinal), args.Contains("--ticket-linkage-database", StringComparer.Ordinal));
}
if (args.Contains("--scheduling-database", StringComparer.Ordinal) || args.Contains("--calendar-database", StringComparer.Ordinal)
    || args.Contains("--service-entries-database", StringComparer.Ordinal))
{
    await Checks.MSAP.SchedulingCheck.RunAsync(args.Contains("--calendar-database", StringComparer.Ordinal),
        args.Contains("--service-entries-database", StringComparer.Ordinal));
}
