using System.Security.Claims;
using IBS.Models.MSAP.Enums;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace IBS.Services.MSAP.AccessControl
{
    public class MsapRoleHandler : AuthorizationHandler<ClaimsAuthorizationRequirement>
    {
        private readonly IRoleService _roleService;
        private readonly IAccessControlService _accessControl;

        public MsapRoleHandler(IRoleService roleService, IAccessControlService accessControl)
        {
            _roleService = roleService;
            _accessControl = accessControl;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, ClaimsAuthorizationRequirement requirement)
        {
            if (requirement.ClaimType is not (MsapRoles.ClaimType or MsapRoles.EntryClaimType))
            {
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = context.User.Identity?.IsAuthenticated == true && userId is not null
                ? await _roleService.GetUserRoleAsync(userId)
                : null;
            var hasAccess = role is not null
                && requirement.AllowedValues!.Contains(role)
                && userId is not null
                && (requirement.ClaimType == MsapRoles.EntryClaimType
                    || await _accessControl.HasAccessAsync(userId, ProcedureEnum.AccessMsap));
            if (hasAccess)
            {
                context.Succeed(requirement);
            }
            else
            {
                // Reject stale claims, inactive accounts and users without assigned MSAP access.
                context.Fail();
            }
        }
    }
}
