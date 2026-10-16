using System.Text.RegularExpressions;
using IBS.Services.MSAP;
using IBS.Services.MSAP.AccessControl;
using IBS.Utility.MSAP.Constants;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ApplicationUser = IBS.Models.ApplicationUser;

namespace IBSWeb.Areas.MSAP.Controllers
{
    [Area("MSAP")]
    [Route("MSAP/Docs")]
    [Authorize]
    public class DocsController(
        IWebHostEnvironment env,
        UserManager<ApplicationUser> userManager,
        IAccessControlService accessControl,
        IRoleService roleService) : Controller
    {
        private static readonly string[] ManualFiles =
        [
            "00-user-login",
            "01-customer-order-slip",
            "02-dispatch-ticket",
            "03-billing",
            "04-collection",
            "06-master-files",
            "07-admin",
            "08-import-export",
            "09-reports",
        ];

        private readonly string _docsRoot = Directory.Exists(Path.Combine(env.ContentRootPath, "MSAP", "Manual"))
            ? Path.Combine(env.ContentRootPath, "MSAP", "Manual")
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "Docs", "MSAP", "manual"));
        private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var html = RenderMarkdownFile("README");
            ViewBag.Sidebar = await GetSidebarItems();
            ViewBag.ActiveSlug = "index";
            ViewBag.Title = "User Manual";
            return View("Index", html);
        }

        [HttpGet("{slug}")]
        public async Task<IActionResult> Show(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return RedirectToAction(nameof(Index));
            }

            if (slug == "index" || slug == "README")
            {
                return RedirectToAction("Index");
            }

            var fileSlug = ManualFiles.FirstOrDefault(f =>
                f.EndsWith(slug, StringComparison.OrdinalIgnoreCase) ||
                f.Replace("0", "").Replace("-", "") == slug.Replace("-", ""));

            if (fileSlug == null)
            {
                return NotFound();
            }

            if (!await UserCanAccess(fileSlug))
            {
                return NotFound();
            }

            var html = RenderMarkdownFile(fileSlug);
            if (html == null)
            {
                return NotFound();
            }

            ViewBag.Sidebar = await GetSidebarItems();
            ViewBag.ActiveSlug = slug;
            ViewBag.Title = GetTitleFromFile(fileSlug);
            return View("Index", html);
        }

        private string? RenderMarkdownFile(string slug)
        {
            var filePath = slug == "README"
                ? Path.Combine(_docsRoot, "README.md")
                : Path.Combine(_docsRoot, $"{slug}.md");

            if (!System.IO.File.Exists(filePath))
            {
                return null;
            }

            var markdown = System.IO.File.ReadAllText(filePath);
            return Markdown.ToHtml(markdown, _pipeline);
        }

        private static string GetTitleFromFile(string slug)
        {
            var match = Regex.Match(slug, @"^\d+-(.+)$");
            if (!match.Success)
            {
                return "User Manual";
            }

            var name = match.Groups[1].Value.Replace("-", " ");
            return char.ToUpper(name[0]) + name[1..];
        }

        private async Task<List<(string Slug, string Title)>> GetSidebarItems()
        {
            var userId = userManager.GetUserId(User)!;
            var allItems = new List<(string Slug, string Title, Func<string, Task<bool>>? Guard)>
            {
                ("index", "Overview", null),
                ("user-login", "User Login", null),
                ("customer-order-slip", "COS", null),
                ("dispatch-ticket", "Dispatch Ticket", null),
                ("billing", "Billing", null),
                ("collection", "Collection", null),
                ("master-files", "Master Files", null),
                ("admin", "Administration", async uid => await roleService.GetUserRoleAsync(uid) is MsapRoles.Admin or MsapRoles.SuperAdmin),
                ("import-export", "Import & Export", async uid => await accessControl.HasMsapImportAccessAsync(uid)),
                ("reports", "Reports", async uid => await accessControl.HasMaritimeReportAccessAsync(uid))
            };

            var result = new List<(string Slug, string Title)>();
            foreach (var (slug, title, guard) in allItems)
            {
                if (guard == null || await guard(userId))
                {
                    result.Add((slug, title));
                }
            }
            return result;
        }

        private async Task<bool> UserCanAccess(string fileSlug)
        {
            var userId = userManager.GetUserId(User)!;
            return fileSlug switch
            {
                "07-admin" => await roleService.GetUserRoleAsync(userId) is MsapRoles.Admin or MsapRoles.SuperAdmin,
                "08-import-export" => await accessControl.HasMsapImportAccessAsync(userId),
                "09-reports" => await accessControl.HasMaritimeReportAccessAsync(userId),
                _ => true
            };
        }
    }
}
