using System.Security.Claims;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.Models;
using IBS.Services;
using IBS.Utility;
using IBSWeb.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Checks.MSAP
{
    internal static class TugboatWorkspaceCheck
    {
        public static async Task RunAsync()
        {
            var workspace = new TugboatWorkspace();
            await using var services = new ServiceCollection().AddLogging().AddSingleton(workspace)
                .AddDbContext<ApplicationDbContext>(options => options.UseNpgsql("Host=localhost;Database=unused;Username=unused"))
                .AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>()
                .Services.BuildServiceProvider();
            await using var db = services.GetRequiredService<ApplicationDbContext>();
            await using var msap = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
            var service = new TugboatWorkspaceService(msap, new EphemeralDataProtectionProvider());
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "workspace-check")], "check"));
            string token = service.Protect(user, 17);
            Check(service.TryRead(user, token, out int? id) && id == 17, "Selection token lost its identity.");
            Check(!service.TryRead(new ClaimsPrincipal(), token, out _), "Another user accepted the token.");
            Check(!service.TryRead(user, token + "tampered", out _) && !service.TryRead(user, "", out _), "Invalid selection token was accepted.");

            workspace.Select(17, "First tugboat");
            Check(db.FilprideCheckVoucherHeaders.ToQueryString().Contains("17"), "Source query is not scoped.");
            Check(db.FilprideCheckVoucherDetails.ToQueryString().Contains("workspace_tugboat_id"), "Detail query is not scoped.");
            Check(db.FilprideGeneralLedgerBooks.IgnoreQueryFilters(["MasterVisibility", "BalanceValidity"])
                .ToQueryString().Contains("17"), "Report visibility override lost workspace scoping.");
            Check(!db.FilprideCheckVoucherHeaders.IgnoreQueryFilters([TugboatWorkspace.FilterName])
                .ToQueryString().Contains("WHERE"), "MMSI document numbering is still scoped.");
            workspace.Select(23, "Second tugboat");
            Check(db.FilprideCheckVoucherHeaders.ToQueryString().Contains("23"), "The EF model cached another selection.");
            workspace.Select(null, "MMSI");
            Check(db.FilprideGlPeriodBalances.ToQueryString().Contains("workspace_tugboat_id IS NULL"), "Consolidated snapshots would count tugboat snapshots twice.");

            await CheckRequestAsync(workspace, service, user, token, 17, true);
            await CheckRequestAsync(workspace, service, user, token, 23, false);
            await CheckRequestAsync(workspace, service, user, "", 17, false);

            workspace.Select(null, "MMSI");
            var jobHttp = new DefaultHttpContext { User = user };
            jobHttp.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AllowAnonymousAttribute()), "job"));
            jobHttp.Request.Headers.Cookie = $"{TugboatWorkspace.CookieName}={token}";
            await new TugboatWorkspaceMiddleware(_ => Task.CompletedTask).InvokeAsync(jobHttp, workspace, service);
            Check(workspace.TugboatId == null, "An anonymous job inherited a browser workspace.");

            Check(!db.Database.HasPendingModelChanges(), "Workspace migration does not match the model.");
            IMigrator migrator = db.GetService<IMigrator>();
            string latest = db.Database.GetMigrations().Last();
            string previous = db.Database.GetMigrations().Reverse().Skip(1).First();
            string upgrade = migrator.GenerateScript(previous, latest);
            string rollback = migrator.GenerateScript(latest, previous);
            Check(upgrade.Contains("workspace_tugboat_id") && !upgrade.Contains("DROP TABLE")
                && !upgrade.Contains("UPDATE ") && !upgrade.Contains("CREATE TABLE"), "Workspace upgrade contains unrelated schema or backfills.");
            Check(rollback.Contains("DROP COLUMN workspace_tugboat_id"), "Workspace rollback is missing ownership columns.");
            Console.WriteLine("PASS: scoped source/detail/GL queries, consolidated snapshots, user-bound tokens, stale requests, anonymous jobs and focused upgrade/rollback SQL (no database).");
        }

        private static async Task CheckRequestAsync(TugboatWorkspace workspace, TugboatWorkspaceService service,
            ClaimsPrincipal user, string token, int selected, bool allowed)
        {
            workspace.Select(selected, "Selected tugboat");
            var http = new DefaultHttpContext { User = user };
            http.Request.Method = "POST";
            http.Request.Headers["X-IBS-Workspace"] = token;
            var route = new RouteData();
            route.Values["area"] = "Filpride";
            var action = new ActionContext(http, route, new ActionDescriptor());
            var context = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
            bool executed = false;
            await new TugboatWorkspaceFilter(workspace, service).OnActionExecutionAsync(context, () =>
            {
                executed = true;
                return Task.FromResult(new ActionExecutedContext(action, [], new object()));
            });
            Check(executed == allowed && (allowed || context.Result is ConflictObjectResult), "Stale request gate returned an unexpected result.");
        }

        internal static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
