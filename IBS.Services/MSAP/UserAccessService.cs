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
            if (!Enum.IsDefined(procedure) || procedure is ProcedureEnum.SetTariff or ProcedureEnum.ApproveTariff
                or ProcedureEnum.DeleteDispatchTicket or ProcedureEnum.DeleteCustomerOrderSlip
                or ProcedureEnum.CloseCustomerOrderSlip or ProcedureEnum.ReverseBilling
                or ProcedureEnum.CreateDisbursement or ProcedureEnum.ManagePostedPeriod or ProcedureEnum.ViewInventoryReport
                or ProcedureEnum.ManageMsapImport)
            {
                return false;
            }

            string? role = await roleService.GetUserRoleAsync(id);
            if (role is MsapRoles.Admin or MsapRoles.SuperAdmin)
            {
                return true;
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

            bool canViewBillings = userAccess.CanCreateBilling || userAccess.CanEditBilling || userAccess.CanDeleteBilling
                || userAccess.CanApproveFinalBilling || userAccess.CanCreateCollection || userAccess.CanAccessTreasury
                || userAccess.CanViewMaritimeReport;
            bool canViewCollections = userAccess.CanCreateCollection || userAccess.CanAccessTreasury || userAccess.CanViewMaritimeReport;
            bool canViewOperations = userAccess.CanCreateCustomerOrderSlip || userAccess.CanEditCustomerOrderSlip
                || userAccess.CanApproveSchedule || userAccess.CanCreateDispatchTicket || userAccess.CanEditDispatchTicket
                || userAccess.CanManageOtherTugCharges || userAccess.CanConfirmOtherTugCharges || canViewBillings || canViewCollections;

            return procedure switch
            {
                ProcedureEnum.AccessMsap => canViewOperations || userAccess.CanViewRates || userAccess.CanManageRates
                    || userAccess.CanApproveRateUpload || userAccess.CanManageMaritimeMasterFile,
                ProcedureEnum.ViewOperations => canViewOperations,
                ProcedureEnum.ViewBillings => canViewBillings,
                ProcedureEnum.ViewCollections => canViewCollections,
                ProcedureEnum.CreateDispatchTicket => userAccess.CanCreateDispatchTicket,
                ProcedureEnum.EditDispatchTicket => userAccess.CanEditDispatchTicket,
                ProcedureEnum.CreateBilling => userAccess.CanCreateBilling,
                ProcedureEnum.EditBilling => userAccess.CanEditBilling,
                ProcedureEnum.DeleteBilling => userAccess.CanDeleteBilling,
                ProcedureEnum.CreateCollection => userAccess.CanCreateCollection,
                ProcedureEnum.CreateCustomerOrderSlip => userAccess.CanCreateCustomerOrderSlip,
                ProcedureEnum.EditCustomerOrderSlip => userAccess.CanEditCustomerOrderSlip,
                ProcedureEnum.ApproveSchedule => userAccess.CanApproveSchedule,
                ProcedureEnum.ViewRates => userAccess.CanViewRates || userAccess.CanManageRates || userAccess.CanApproveRateUpload,
                ProcedureEnum.ManageRates => userAccess.CanManageRates,
                ProcedureEnum.ManageOtherTugCharges => userAccess.CanManageOtherTugCharges,
                ProcedureEnum.ConfirmOtherTugCharges => userAccess.CanConfirmOtherTugCharges,
                ProcedureEnum.ApproveRateUpload => userAccess.CanApproveRateUpload,
                ProcedureEnum.ApproveFinalBilling => userAccess.CanApproveFinalBilling,
                ProcedureEnum.AccessTreasury => userAccess.CanAccessTreasury,
                ProcedureEnum.ManageMaritimeMasterFile => userAccess.CanManageMaritimeMasterFile,
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
            UserAccess? access = await unitOfWork.UserAccess.GetAsync(ua => ua.Id == id, cancellationToken);
            if (access != null)
            {
                ApplicationUser? user = await userManager.FindByIdAsync(access.UserId);
                access.DisplayName = string.IsNullOrWhiteSpace(user?.Name) ? "Name unavailable" : user.Name.Trim();
            }
            return access;
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
                model.Id = 0;
                ClearRetiredPermissions(model);
                if (await roleService.GetUserRoleAsync(model.UserId) != MsapRoles.User)
                {
                    return ServiceResult.Failure("Procedure permissions can only be assigned to an active MSAP User.");
                }

                var existing = await unitOfWork.UserAccess.GetAsync(ua => ua.UserId == model.UserId, cancellationToken);
                if (existing != null)
                {
                    return ServiceResult.Failure("User access already exists for this account.");
                }

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var selectedUser = await userManager.FindByIdAsync(model.UserId);
                    model.UserName = selectedUser?.UserName;
                    model.DisplayName = string.IsNullOrWhiteSpace(selectedUser?.Name) ? "Name unavailable" : selectedUser.Name.Trim();

                    await unitOfWork.UserAccess.AddAsync(model, cancellationToken);
                    await unitOfWork.SaveAsync(cancellationToken);

                    AuditTrail auditTrail = await CreateAuditAsync(username, $"Created User Access for {model.DisplayName}. Schedule: prepare={model.CanCreateCustomerOrderSlip}, edit={model.CanEditCustomerOrderSlip}, approve={model.CanApproveSchedule}. Tickets: record={model.CanCreateDispatchTicket}, edit/recalculate={model.CanEditDispatchTicket}. Billing: prepare={model.CanCreateBilling}, edit={model.CanEditBilling}, discard draft={model.CanDeleteBilling}, approve={model.CanApproveFinalBilling}. Collections: prepare={model.CanCreateCollection}, banking={model.CanAccessTreasury}. Rates: view={model.CanViewRates}, prepare={model.CanManageRates}, approve={model.CanApproveRateUpload}. Other Tug Charges: prepare={model.CanManageOtherTugCharges}, confirm={model.CanConfirmOtherTugCharges}. References={model.CanManageMaritimeMasterFile}, import={model.CanManageMsapImport}, reports={model.CanViewMaritimeReport}.", model.Id, model.UserId);
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

                if (existing.UserId != model.UserId || await roleService.GetUserRoleAsync(existing.UserId) != MsapRoles.User)
                {
                    return ServiceResult.Failure("Permissions can only be changed for the selected active MSAP User.");
                }
                ClearRetiredPermissions(model);
                model.DisplayName = existing.DisplayName;

                var changes = new List<string>();
                if (existing.CanManageOtherTugCharges != model.CanManageOtherTugCharges)
                {
                    changes.Add($"Prepare Other Tug Charges: {existing.CanManageOtherTugCharges} → {model.CanManageOtherTugCharges}");
                }
                if (existing.CanConfirmOtherTugCharges != model.CanConfirmOtherTugCharges)
                {
                    changes.Add($"Confirm Other Tug Charges: {existing.CanConfirmOtherTugCharges} → {model.CanConfirmOtherTugCharges}");
                }
                if (existing.CanViewRates != model.CanViewRates)
                {
                    changes.Add($"View Rates: {existing.CanViewRates} → {model.CanViewRates}");
                }
                if (existing.CanManageRates != model.CanManageRates)
                {
                    changes.Add($"Prepare Rates: {existing.CanManageRates} → {model.CanManageRates}");
                }
                if (existing.CanApproveRateUpload != model.CanApproveRateUpload)
                {
                    changes.Add($"Approve Rate Updates: {existing.CanApproveRateUpload} → {model.CanApproveRateUpload}");
                }
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
                if (existing.CanCreateCustomerOrderSlip != model.CanCreateCustomerOrderSlip) changes.Add($"Create schedule: {existing.CanCreateCustomerOrderSlip} → {model.CanCreateCustomerOrderSlip}");
                if (existing.CanEditCustomerOrderSlip != model.CanEditCustomerOrderSlip) changes.Add($"Edit booking: {existing.CanEditCustomerOrderSlip} → {model.CanEditCustomerOrderSlip}");
                if (existing.CanDeleteCustomerOrderSlip != model.CanDeleteCustomerOrderSlip) changes.Add($"Retired delete COS: {existing.CanDeleteCustomerOrderSlip} → {model.CanDeleteCustomerOrderSlip}");
                if (existing.CanCloseCustomerOrderSlip != model.CanCloseCustomerOrderSlip) changes.Add($"Retired close COS: {existing.CanCloseCustomerOrderSlip} → {model.CanCloseCustomerOrderSlip}");
                if (existing.CanApproveSchedule != model.CanApproveSchedule)
                {
                    changes.Add($"Approve Schedule: {existing.CanApproveSchedule} → {model.CanApproveSchedule}");
                }
                if (existing.CanApproveFinalBilling != model.CanApproveFinalBilling)
                {
                    changes.Add($"Approve Final Billing: {existing.CanApproveFinalBilling} → {model.CanApproveFinalBilling}");
                }
                if (existing.CanAccessTreasury != model.CanAccessTreasury) changes.Add($"Collection banking: {existing.CanAccessTreasury} → {model.CanAccessTreasury}");
                if (existing.CanCreateDisbursement != model.CanCreateDisbursement) changes.Add($"Create Disbursement: {existing.CanCreateDisbursement} → {model.CanCreateDisbursement}");
                if (existing.CanManageMsapImport != model.CanManageMsapImport) changes.Add($"Manage Import: {existing.CanManageMsapImport} → {model.CanManageMsapImport}");
                if (existing.CanManageMaritimeMasterFile != model.CanManageMaritimeMasterFile) changes.Add($"Manage Maritime MF: {existing.CanManageMaritimeMasterFile} → {model.CanManageMaritimeMasterFile}");
                if (existing.CanViewInventoryReport != model.CanViewInventoryReport) changes.Add($"View Inventory: {existing.CanViewInventoryReport} → {model.CanViewInventoryReport}");
                if (existing.CanViewMaritimeReport != model.CanViewMaritimeReport) changes.Add($"View Maritime: {existing.CanViewMaritimeReport} → {model.CanViewMaritimeReport}");

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    if (changes.Any())
                    {
                        AuditTrail auditTrail = await CreateAuditAsync(username, $"Edited User Access for {existing.DisplayName}: {string.Join("; ", changes)}", existing.Id, existing.UserId);
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
                    existing.CanViewRates = model.CanViewRates;
                    existing.CanManageRates = model.CanManageRates;
                    existing.CanManageOtherTugCharges = model.CanManageOtherTugCharges;
                    existing.CanConfirmOtherTugCharges = model.CanConfirmOtherTugCharges;
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

        private static void ClearRetiredPermissions(UserAccess model)
        {
            model.CanSetTariff = false;
            model.CanApproveTariff = false;
            model.CanDeleteDispatchTicket = false;
            model.CanDeleteCustomerOrderSlip = false;
            model.CanCloseCustomerOrderSlip = false;
            model.CanReverseBilling = false;
            model.CanCreateDisbursement = false;
            model.CanViewInventoryReport = false;
            model.CanManageMsapImport = false;
        }

        private async Task<AuditTrail> CreateAuditAsync(string username, string activity, int recordId, string targetUserId)
        {
            ApplicationUser? actor = await userManager.FindByNameAsync(username);
            return new AuditTrail(username, activity, "User Access", recordId, targetUserId)
            {
                UserId = actor?.Id,
                ActorName = string.IsNullOrWhiteSpace(actor?.Name) ? null : actor.Name.Trim()
            };
        }

    }
}
