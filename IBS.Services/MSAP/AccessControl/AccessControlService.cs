using ApplicationUser = IBS.Models.ApplicationUser;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using Microsoft.AspNetCore.Identity;

namespace IBS.Services.MSAP.AccessControl
{
    /// <summary>
    /// Centralized user access control service for all modules
    /// </summary>
    public interface IAccessControlService
    {
        Task<bool> HasAccessAsync(string userId, params ProcedureEnum[] procedures);
        Task<bool> HasAnyAccessAsync(string userId, params ProcedureEnum[] procedures);
    }

    public class AccessControlService(UserManager<ApplicationUser> userManager, IUserAccessService userAccessService)
        : IAccessControlService
    {
        private readonly UserManager<ApplicationUser> _userManager = userManager;

        /// <summary>
        /// Check if user has access to ANY of the specified procedures
        /// </summary>
        public async Task<bool> HasAnyAccessAsync(string userId, params ProcedureEnum[] procedures)
        {
            if (procedures == null || procedures.Length == 0)
            {
                return false;
            }

            foreach (var procedure in procedures)
            {
                if (await userAccessService.CheckAccess(userId, procedure, default))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check access - alias for HasAnyAccessAsync
        /// </summary>
        public async Task<bool> HasAccessAsync(string userId, params ProcedureEnum[] procedures)
        {
            return await HasAnyAccessAsync(userId, procedures);
        }
    }

    /// <summary>
    /// Extension methods for easy access control in controllers
    /// </summary>
    public static class AccessControlExtensions
    {

        #region -- MSAP Import --

        public static async Task<bool> HasMsapImportAccessAsync(this IAccessControlService accessControl, string userId)
        {
            return await accessControl.HasAnyAccessAsync(userId,
                ProcedureEnum.ManageMsapImport);
        }

        #endregion -- MSAP Import --

        #region -- Reports --

        public static async Task<bool> HasMaritimeReportAccessAsync(this IAccessControlService accessControl, string userId)
        {
            return await accessControl.HasAnyAccessAsync(userId,
                ProcedureEnum.ViewMaritimeReport);
        }

        #endregion -- Reports --
    }
}
