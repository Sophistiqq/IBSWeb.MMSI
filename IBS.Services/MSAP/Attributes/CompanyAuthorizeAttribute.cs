using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IBS.Services.MSAP.Attributes
{
    public class CompanyAuthorizeAttribute(string company): AuthorizeAttribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var companyClaim = context.HttpContext.User.Claims.FirstOrDefault(c => c.Type == "Company")?.Value;

            // During consolidation, allow both Filpride and MMSI users to access the resources
            bool isAuthorized = string.Equals(companyClaim, company, StringComparison.OrdinalIgnoreCase);

            if (!isAuthorized)
            {
                if ((company == "MMSI" || company == "Filpride") &&
                    (companyClaim == "MMSI" || companyClaim == "Filpride"))
                {
                    isAuthorized = true;
                }
            }

            if (!isAuthorized)
            {
                context.Result = new ForbidResult();
            }
        }
    }
}
