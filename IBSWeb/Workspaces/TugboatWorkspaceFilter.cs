using IBS.Services;
using IBS.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IBSWeb.Workspaces
{
    public class TugboatWorkspaceFilter : IAsyncActionFilter, IResultFilter
    {
        private readonly TugboatWorkspace _workspace;
        private readonly TugboatWorkspaceService _service;

        public TugboatWorkspaceFilter(TugboatWorkspace workspace, TugboatWorkspaceService service)
        {
            _workspace = workspace;
            _service = service;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            HttpRequest request = context.HttpContext.Request;
            if (context.RouteData.Values["area"] as string == "Filpride"
                && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                string token = request.Headers["X-IBS-Workspace"].ToString();
                if (request.HasFormContentType)
                {
                    IFormCollection form = await request.ReadFormAsync(context.HttpContext.RequestAborted);
                    if (form.TryGetValue(TugboatWorkspace.FormFieldName, out var value))
                    {
                        token = value.ToString();
                    }
                }

                if (!_service.TryRead(context.HttpContext.User, token, out int? id) || id != _workspace.TugboatId)
                {
                    context.Result = new ConflictObjectResult("The workspace changed or this page is outdated. Reload it before submitting.");
                    return;
                }
            }

            await next();
        }

        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.RouteData.Values["area"] as string == "Filpride"
                && context.Result is FileResult file && _workspace.TugboatId.HasValue
                && !string.IsNullOrEmpty(file.FileDownloadName))
            {
                file.FileDownloadName = $"{Path.GetFileNameWithoutExtension(file.FileDownloadName)}-Tugboat-{_workspace.TugboatId}{Path.GetExtension(file.FileDownloadName)}";
            }
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }
}
