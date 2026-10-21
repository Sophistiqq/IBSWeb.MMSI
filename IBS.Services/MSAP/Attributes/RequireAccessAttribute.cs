using IBS.Models.MSAP.Enums;
using IBS.Services.MSAP.AccessControl;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace IBS.Services.MSAP.Attributes
{
    /// <summary>
    /// Shared authorization plumbing for the RequireAccess attributes: resolves the user claim,
    /// runs the per-attribute permission check, and denies in a transport-appropriate way —
    /// JSON envelope ({ success = false, message }) for AJAX/JSON requests so ModernAlert can
    /// render it, TempData["error"] + redirect for full-page navigations.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public abstract class RequireAccessBaseAttribute(string errorMessage) : Attribute, IAsyncAuthorizationFilter
    {
        protected abstract Task<bool> HasAccessAsync(IAccessControlService accessControl, string userId);

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || string.IsNullOrWhiteSpace(userIdClaim.Value))
            {
                Deny(context, "You must be logged in to access this resource.");
                return;
            }

            var accessControl = context.HttpContext.RequestServices.GetRequiredService<IAccessControlService>();
            if (!await HasAccessAsync(accessControl, userIdClaim.Value))
            {
                Deny(context, errorMessage);
            }
        }

        private void Deny(AuthorizationFilterContext context, string message)
        {
            var httpContext = context.HttpContext;
            var isAjax = httpContext.Request.Headers["X-Requested-With"].ToString()
                .Contains("XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
            var isJsonRequest = httpContext.Request.Headers["Content-Type"].ToString()
                .Contains("application/json", StringComparison.OrdinalIgnoreCase);

            if (isAjax || isJsonRequest)
            {
                context.Result = new JsonResult(new { success = false, message });
                return;
            }

            var tempDataFactory = httpContext.RequestServices.GetRequiredService<ITempDataDictionaryFactory>();
            var tempData = tempDataFactory.GetTempData(httpContext);
            tempData["error"] = message;
            tempData.Save();

            // Full-page navigation: bounce the user back to the page they came from so
            // the workflow isn't lost; fall back to Home when there's no local referer.
            var referer = httpContext.Request.Headers["Referer"].ToString();
            if (!string.IsNullOrWhiteSpace(referer)
                && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri)
                && string.Equals(refererUri.Host, httpContext.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new RedirectResult(refererUri.PathAndQuery);
                return;
            }

            context.Result = new RedirectToActionResult("Index", "Home", new { area = "MSAP" });
        }
    }

    /// <summary>
    /// Declarative access control attribute that checks user permissions before action execution.
    /// Implements IAsyncAuthorizationFilter to run in the authorization filter phase.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class RequireAccessAttribute(
        ProcedureEnum procedure,
        string errorMessage = "Access denied. You don't have permission to perform this action.")
        : RequireAccessBaseAttribute(errorMessage)
    {
        protected override Task<bool> HasAccessAsync(IAccessControlService accessControl, string userId)
            => accessControl.HasAccessAsync(userId, procedure);
    }

    /// <summary>
    /// Declarative access control attribute that checks if user has access to ANY of the specified procedures.
    /// Use this when an action should be accessible if the user has at least one of the required permissions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class RequireAnyAccessAttribute : RequireAccessBaseAttribute
    {
        private readonly ProcedureEnum[] _procedures;

        public RequireAnyAccessAttribute(
            params ProcedureEnum[] procedures)
            : base("Access denied. You don't have permission to perform this action.")
        {
            _procedures = procedures ?? Array.Empty<ProcedureEnum>();
        }

        public RequireAnyAccessAttribute(
            string errorMessage,
            params ProcedureEnum[] procedures)
            : base(errorMessage)
        {
            _procedures = procedures ?? Array.Empty<ProcedureEnum>();
        }

        protected override async Task<bool> HasAccessAsync(IAccessControlService accessControl, string userId)
        {
            foreach (var procedure in _procedures)
            {
                if (await accessControl.HasAccessAsync(userId, procedure))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
