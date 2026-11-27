using IBS.Models.MSAP.Enums;

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

    public class AccessControlService(IUserAccessService userAccessService)
        : IAccessControlService
    {
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
}
