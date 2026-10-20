using IBS.Services;
using IBS.Utility;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace IBSWeb.Workspaces
{
    [HtmlTargetElement("form")]
    public class TugboatWorkspaceFormTagHelper : TagHelper
    {
        private readonly TugboatWorkspace _workspace;
        private readonly TugboatWorkspaceService _service;

        public TugboatWorkspaceFormTagHelper(TugboatWorkspace workspace, TugboatWorkspaceService service)
        {
            _workspace = workspace;
            _service = service;
        }

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = null!;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (ViewContext.HttpContext.User.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var input = new TagBuilder("input");
            input.Attributes["type"] = "hidden";
            input.Attributes["name"] = TugboatWorkspace.FormFieldName;
            input.Attributes["value"] = _service.Protect(ViewContext.HttpContext.User, _workspace.TugboatId);
            output.PostContent.AppendHtml(input);
        }
    }
}
