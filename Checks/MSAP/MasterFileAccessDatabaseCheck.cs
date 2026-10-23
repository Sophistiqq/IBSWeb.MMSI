using System.Security.Claims;
using System.Text.RegularExpressions;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.Models;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Checks.MSAP
{
    internal static class MasterFileAccessDatabaseCheck
    {
        public static async Task RunAsync(IServiceProvider services)
        {
            // TestConnection rejects the configured source and databases outside the isolated prefix.
            _ = TugboatOwnerCheck.TestConnection();
            var shared = services.GetRequiredService<ApplicationDbContext>();
            var module = services.GetRequiredService<MsapDbContext>();
            Check(!(await shared.Database.GetAppliedMigrationsAsync()).Any()
                && !(await module.Database.GetAppliedMigrationsAsync()).Any(), "Use a fresh isolated database for the access check.");
            await shared.Database.MigrateAsync("20261020094302_AddTugboatWorkspaceOwnership");
            await module.Database.MigrateAsync();
            await shared.Database.MigrateAsync();
            Check(!shared.Database.HasPendingModelChanges() && !module.Database.HasPendingModelChanges(),
                "Fresh migration snapshots differ from the models.");

            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = new ApplicationUser { UserName = "master-admin-check", Name = "Master admin check", Department = "MIS" };
            var user = new ApplicationUser { UserName = "master-user-check", Name = "Master user check", Department = "MIS" };
            foreach ((ApplicationUser account, string role) in new[] { (admin, MsapRoles.Admin), (user, MsapRoles.User) })
            {
                Check((await users.CreateAsync(account)).Succeeded, "Could not create the isolated identity fixture.");
                Check((await users.AddClaimAsync(account, new Claim(MsapRoles.ClaimType, role))).Succeeded,
                    "Could not grant the isolated MSAP role.");
            }
            var grants = new UserAccess { UserId = user.Id };
            module.MsapUserAccesses.Add(grants);
            await module.SaveChangesAsync();
            string[] controllers = ["Customer", "MaritimeService", "Port", "Terminal", "Vessel", "Tugboat", "TugboatOwner", "TugMaster", "Principal", "BankAccount"];
            string[] flags = ["Customer", "Service", "Port", "Terminal", "Vessel", "Tugboat", "TugboatOwner", "TugMaster", "Principal", "BankAccount"];
            Check(flags.All(flag => !(bool)typeof(UserAccess).GetProperty($"CanManage{flag}MasterFile")!.GetValue(grants)!),
                "New access grants did not default to false.");

            typeof(UserAccess).GetProperty("CanManagePortMasterFile")!.SetValue(grants, true);
            await module.SaveChangesAsync();
            IMigrator migrator = module.GetService<IMigrator>();
            await migrator.MigrateAsync("20261022084743_RemoveEmployeeMasterFile");
            await module.Database.MigrateAsync();
            module.ChangeTracker.Clear();
            grants = await module.MsapUserAccesses.SingleAsync(row => row.UserId == user.Id);
            Check(flags.All(flag => !(bool)typeof(UserAccess).GetProperty($"CanManage{flag}MasterFile")!.GetValue(grants)!),
                "Permission rollback/reapply must default new granular grants to false.");
            Check(!module.Database.HasPendingModelChanges(), "Cleanup/permission rollback differs from the model.");

            await AssertNavbarAsync(admin, controllers);
            await AssertNavbarAsync(user, []);
            for (int index = 0; index < controllers.Length; index++)
            {
                var property = typeof(UserAccess).GetProperty($"CanManage{flags[index]}MasterFile")!;
                property.SetValue(grants, true);
                await module.SaveChangesAsync();
                await AssertNavbarAsync(user, [controllers[index]]);
                property.SetValue(grants, false);
                await module.SaveChangesAsync();
            }
            await AssertNavbarAsync(user, []);
            Console.WriteLine("PASS: fresh shared/MSAP migrations and cleanup/permission rollback/reapply; persisted admin/all, user/none, each single master-file navbar grant and revocation.");

            async Task AssertNavbarAsync(ApplicationUser account, string[] expected)
            {
                // Use a fresh request scope so access is read from PostgreSQL rather than a tracked fixture.
                using IServiceScope request = services.CreateScope();
                IServiceProvider requestServices = request.ServiceProvider;
                var access = requestServices.GetRequiredService<IAccessControlService>();
                Check(access is AccessControlService, "The database navbar check must use the real access service.");
                var principalFactory = requestServices.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
                var http = new DefaultHttpContext { RequestServices = requestServices, User = await principalFactory.CreateAsync(account) };
                http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Master-file access check"));
                var route = new RouteData();
                route.Values["area"] = "MSAP";
                route.Values["controller"] = "Home";
                route.Values["action"] = "Index";
                http.Request.RouteValues = route.Values;
                var context = new ActionContext(http, route, new ActionDescriptor());
                var engine = requestServices.GetRequiredService<IRazorViewEngine>();
                var view = engine.GetView(null, "/Areas/MSAP/Views/Shared/_Navbar.cshtml", false);
                var data = new ViewDataDictionary(requestServices.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary());
                using var output = new StringWriter();
                await view.View!.RenderAsync(new ViewContext(context, view.View, data,
                    requestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), output, new HtmlHelperOptions()));
                string html = output.ToString();
                foreach (string controller in controllers)
                {
                    bool visible = Regex.IsMatch(html, $"href=\"/MSAP/{controller}(?:/Index)?\"");
                    Check(visible == expected.Contains(controller), $"Unexpected navbar visibility for {account.UserName}: {controller}.");
                }
                Check(!html.Contains("/MSAP/Supplier") && !html.Contains("/MSAP/Employee"), "Retired master-file links remain visible.");
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
