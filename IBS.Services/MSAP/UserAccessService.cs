using ApplicationUser = IBS.Models.ApplicationUser;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Constants;
using IBS.Utility.MSAP.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class UserAccessService(
        IUnitOfWork unitOfWork,
        UserManager<ApplicationUser> userManager,
        IRoleService roleService,
        ILogger<UserAccessService> logger)
        : IUserAccessService
    {
        public async Task<bool> CheckAccess(string id, ProcedureEnum procedure, CancellationToken cancellationToken = default)
        {
            string? role = await roleService.GetUserRoleAsync(id);
            if (role is MsapRoles.Admin or MsapRoles.SuperAdmin)
            {
                return procedure != ProcedureEnum.ApproveRateUpload;
            }

            if (role != MsapRoles.User)
            {
                return false;
            }

            var userAccess = await unitOfWork.UserAccess
                .GetAsync(a => a.UserId == id, cancellationToken);

            if (userAccess == null)
            {
                return false;
            }

            return procedure switch
            {
                ProcedureEnum.CreateDispatchTicket => userAccess.CanCreateDispatchTicket,
                ProcedureEnum.EditDispatchTicket => userAccess.CanEditDispatchTicket,
                ProcedureEnum.DeleteDispatchTicket => userAccess.CanDeleteDispatchTicket,
                ProcedureEnum.SetTariff => userAccess.CanSetTariff,
                ProcedureEnum.ApproveTariff => userAccess.CanApproveTariff,
                ProcedureEnum.CreateBilling => userAccess.CanCreateBilling,
                ProcedureEnum.EditBilling => userAccess.CanEditBilling,
                ProcedureEnum.DeleteBilling => userAccess.CanDeleteBilling,
                ProcedureEnum.ReverseBilling => userAccess.CanReverseBilling,
                ProcedureEnum.CreateCollection => userAccess.CanCreateCollection,
                ProcedureEnum.CreateCustomerOrderSlip => userAccess.CanCreateCustomerOrderSlip,
                ProcedureEnum.EditCustomerOrderSlip => userAccess.CanEditCustomerOrderSlip,
                ProcedureEnum.DeleteCustomerOrderSlip => userAccess.CanDeleteCustomerOrderSlip,
                ProcedureEnum.CloseCustomerOrderSlip => userAccess.CanCloseCustomerOrderSlip,
                ProcedureEnum.ApproveSchedule => userAccess.CanApproveSchedule,
                ProcedureEnum.ApproveRateUpload => userAccess.CanApproveRateUpload,
                ProcedureEnum.ApproveFinalBilling => userAccess.CanApproveFinalBilling,
                ProcedureEnum.AccessTreasury => userAccess.CanAccessTreasury,
                ProcedureEnum.CreateDisbursement => userAccess.CanCreateDisbursement,
                ProcedureEnum.ManageMsapImport => userAccess.CanManageMsapImport,
                ProcedureEnum.ManageMaritimeMasterFile => userAccess.CanManageMaritimeMasterFile,
                ProcedureEnum.ViewInventoryReport => userAccess.CanViewInventoryReport,
                ProcedureEnum.ViewMaritimeReport => userAccess.CanViewMaritimeReport,
                _ => false
            };
        }

        public async Task<IEnumerable<UserAccess>> GetAllAsync(CancellationToken cancellationToken)
        {
            return await unitOfWork.UserAccess.GetAllAsync(null, cancellationToken);
        }

        public async Task<UserAccess?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await unitOfWork.UserAccess.GetAsync(ua => ua.Id == id, cancellationToken);
        }

        public async Task<UserAccess> PopulateUsersAsync(UserAccess? model, CancellationToken cancellationToken)
        {
            model ??= new UserAccess();
            model.Users = [];
            foreach (var item in await unitOfWork.Msap.GetMsapUsersSelectListById(cancellationToken))
            {
                if (await roleService.GetUserRoleAsync(item.Value) == MsapRoles.User)
                {
                    model.Users.Add(item);
                }
            }
            return model;
        }

        public async Task<ServiceResult> CreateAsync(UserAccess model, string username, CancellationToken cancellationToken)
        {
            try
            {
                if (await roleService.GetUserRoleAsync(model.UserId) != MsapRoles.User)
                {
                    return ServiceResult.Failure("Procedure permissions can only be assigned to an active MSAP User.");
                }

                var existing = await unitOfWork.UserAccess.GetAsync(ua => ua.UserId == model.UserId, cancellationToken);
                if (existing != null)
                {
                    return ServiceResult.Failure($"Access for {existing.UserName} already exists.");
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var selectedUser = await userManager.FindByIdAsync(model.UserId);
                    model.UserName = selectedUser?.UserName;

                    await unitOfWork.UserAccess.AddAsync(model, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);

                    AuditTrail auditTrail = new(username, $"Created User Access for {model.UserName}", "User Access", model.Id);
                    await unitOfWork.AuditTrail.AddAsync(auditTrail, cancellationToken);

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success("User access created successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create user access");
                return ServiceResult.Failure($"Failed to create user access: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }

        public async Task<ServiceResult> UpdateAsync(UserAccess model, string username, CancellationToken cancellationToken)
        {
            try
            {
                var existing = await GetByIdAsync(model.Id, cancellationToken);
                if (existing == null)
                {
                    return ServiceResult.Failure("User access not found.", ServiceResultStatus.NotFound);
                }

                var changes = new List<string>();
                if (existing.CanCreateDispatchTicket != model.CanCreateDispatchTicket) changes.Add($"Create DT: {existing.CanCreateDispatchTicket} → {model.CanCreateDispatchTicket}");
                if (existing.CanEditDispatchTicket != model.CanEditDispatchTicket) changes.Add($"Edit DT: {existing.CanEditDispatchTicket} → {model.CanEditDispatchTicket}");
                if (existing.CanDeleteDispatchTicket != model.CanDeleteDispatchTicket) changes.Add($"Delete DT: {existing.CanDeleteDispatchTicket} → {model.CanDeleteDispatchTicket}");
                if (existing.CanSetTariff != model.CanSetTariff) changes.Add($"Set Tariff: {existing.CanSetTariff} → {model.CanSetTariff}");
                if (existing.CanApproveTariff != model.CanApproveTariff) changes.Add($"Approve Tariff: {existing.CanApproveTariff} → {model.CanApproveTariff}");
                if (existing.CanCreateBilling != model.CanCreateBilling) changes.Add($"Create Billing: {existing.CanCreateBilling} → {model.CanCreateBilling}");
                if (existing.CanEditBilling != model.CanEditBilling) changes.Add($"Edit Billing: {existing.CanEditBilling} → {model.CanEditBilling}");
                if (existing.CanDeleteBilling != model.CanDeleteBilling) changes.Add($"Delete Billing: {existing.CanDeleteBilling} → {model.CanDeleteBilling}");
                if (existing.CanReverseBilling != model.CanReverseBilling) changes.Add($"Reverse Billing: {existing.CanReverseBilling} → {model.CanReverseBilling}");
                if (existing.CanCreateCollection != model.CanCreateCollection) changes.Add($"Create Collection: {existing.CanCreateCollection} → {model.CanCreateCollection}");
                if (existing.CanCreateCustomerOrderSlip != model.CanCreateCustomerOrderSlip) changes.Add($"Create JO: {existing.CanCreateCustomerOrderSlip} → {model.CanCreateCustomerOrderSlip}");
                if (existing.CanEditCustomerOrderSlip != model.CanEditCustomerOrderSlip) changes.Add($"Edit JO: {existing.CanEditCustomerOrderSlip} → {model.CanEditCustomerOrderSlip}");
                if (existing.CanDeleteCustomerOrderSlip != model.CanDeleteCustomerOrderSlip) changes.Add($"Delete JO: {existing.CanDeleteCustomerOrderSlip} → {model.CanDeleteCustomerOrderSlip}");
                if (existing.CanCloseCustomerOrderSlip != model.CanCloseCustomerOrderSlip) changes.Add($"Close JO: {existing.CanCloseCustomerOrderSlip} → {model.CanCloseCustomerOrderSlip}");
                if (existing.CanApproveSchedule != model.CanApproveSchedule)
                {
                    changes.Add($"Approve Schedule: {existing.CanApproveSchedule} → {model.CanApproveSchedule}");
                }
                if (existing.CanApproveRateUpload != model.CanApproveRateUpload)
                {
                    changes.Add($"Approve Rate Upload: {existing.CanApproveRateUpload} → {model.CanApproveRateUpload}");
                }
                if (existing.CanApproveFinalBilling != model.CanApproveFinalBilling)
                {
                    changes.Add($"Approve Final Billing: {existing.CanApproveFinalBilling} → {model.CanApproveFinalBilling}");
                }
                if (existing.CanAccessTreasury != model.CanAccessTreasury) changes.Add($"Access Treasury: {existing.CanAccessTreasury} → {model.CanAccessTreasury}");
                if (existing.CanCreateDisbursement != model.CanCreateDisbursement) changes.Add($"Create Disbursement: {existing.CanCreateDisbursement} → {model.CanCreateDisbursement}");
                if (existing.CanManageMsapImport != model.CanManageMsapImport) changes.Add($"Manage Import: {existing.CanManageMsapImport} → {model.CanManageMsapImport}");
                if (existing.CanManageMaritimeMasterFile != model.CanManageMaritimeMasterFile) changes.Add($"Manage Maritime MF: {existing.CanManageMaritimeMasterFile} → {model.CanManageMaritimeMasterFile}");
                if (existing.CanViewInventoryReport != model.CanViewInventoryReport) changes.Add($"View Inventory: {existing.CanViewInventoryReport} → {model.CanViewInventoryReport}");
                if (existing.CanViewMaritimeReport != model.CanViewMaritimeReport) changes.Add($"View Maritime: {existing.CanViewMaritimeReport} → {model.CanViewMaritimeReport}");

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    if (changes.Any())
                    {
                        AuditTrail auditTrail = new(username, $"Edited User Access for {existing.UserName}: {string.Join("; ", changes)}", "User Access", existing.Id);
                        await unitOfWork.AuditTrail.AddAsync(auditTrail, cancellationToken);
                    }

                    existing.CanCreateDispatchTicket = model.CanCreateDispatchTicket;
                    existing.CanEditDispatchTicket = model.CanEditDispatchTicket;
                    existing.CanDeleteDispatchTicket = model.CanDeleteDispatchTicket;
                    existing.CanSetTariff = model.CanSetTariff;
                    existing.CanApproveTariff = model.CanApproveTariff;
                    existing.CanCreateBilling = model.CanCreateBilling;
                    existing.CanEditBilling = model.CanEditBilling;
                    existing.CanDeleteBilling = model.CanDeleteBilling;
                    existing.CanReverseBilling = model.CanReverseBilling;
                    existing.CanCreateCollection = model.CanCreateCollection;
                    existing.CanCreateCustomerOrderSlip = model.CanCreateCustomerOrderSlip;
                    existing.CanEditCustomerOrderSlip = model.CanEditCustomerOrderSlip;
                    existing.CanDeleteCustomerOrderSlip = model.CanDeleteCustomerOrderSlip;
                    existing.CanCloseCustomerOrderSlip = model.CanCloseCustomerOrderSlip;
                    existing.CanApproveSchedule = model.CanApproveSchedule;
                    existing.CanApproveRateUpload = model.CanApproveRateUpload;
                    existing.CanApproveFinalBilling = model.CanApproveFinalBilling;
                    existing.CanAccessTreasury = model.CanAccessTreasury;
                    existing.CanCreateDisbursement = model.CanCreateDisbursement;
                    existing.CanManageMsapImport = model.CanManageMsapImport;
                    existing.CanManageMaritimeMasterFile = model.CanManageMaritimeMasterFile;
                    existing.CanViewInventoryReport = model.CanViewInventoryReport;
                    existing.CanViewMaritimeReport = model.CanViewMaritimeReport;

                    await unitOfWork.SaveAsync(cancellationToken);
                }, cancellationToken);

                return ServiceResult.Success("User access edited successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to edit user access");
                return ServiceResult.Failure($"Failed to edit user access: {ExceptionHelper.GetErrorMessage(ex)}");
            }
        }
    }
}
