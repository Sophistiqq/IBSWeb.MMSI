using IBS.Services;
using IBS.Utility;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;

namespace IBSWeb.Workspaces
{
    public class TugboatWorkspaceMiddleware
    {
        private readonly RequestDelegate _next;

        public TugboatWorkspaceMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext http, TugboatWorkspace workspace, TugboatWorkspaceService service)
        {
            var isMsapPath = http.Request.Path.StartsWithSegments("/MSAP")
                || http.Request.Path.StartsWithSegments("/MSAPAdmin");
            var isMsapOnlyUser = http.User.IsInRole(MsapRoles.IdentityUserRole)
                && !http.User.IsInRole(MsapRoles.Admin);

            if (http.User.Identity?.IsAuthenticated == true
                && !isMsapPath
                && !isMsapOnlyUser
                && http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() == null
                && http.Request.Cookies.TryGetValue(TugboatWorkspace.CookieName, out string? token))
            {
                if (!service.TryRead(http.User, token, out int? id))
                {
                    http.Response.Cookies.Delete(TugboatWorkspace.CookieName);
                    http.Response.StatusCode = StatusCodes.Status409Conflict;
                    await http.Response.WriteAsync("Workspace selection expired. Reload the page and select your workspace.");
                    return;
                }

                string? name = await service.GetNameAsync(id, http.RequestAborted);
                if (name == null)
                {
                    http.Response.Cookies.Delete(TugboatWorkspace.CookieName);
                    http.Response.StatusCode = StatusCodes.Status409Conflict;
                    await http.Response.WriteAsync("The selected company-owned tugboat is unavailable. Reload the page.");
                    return;
                }

                workspace.Select(id, name);
            }

            await _next(http);
        }
    }
}
