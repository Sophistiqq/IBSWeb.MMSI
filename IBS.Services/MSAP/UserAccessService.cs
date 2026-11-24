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
            if (!Enum.IsDefined(procedure) || procedure is ProcedureEnum.EditBilling or ProcedureEnum.SetTariff or ProcedureEnum.ApproveTariff
                or ProcedureEnum.DeleteDispatchTicket or ProcedureEnum.DeleteCustomerOrderSlip
                or ProcedureEnum.CloseCustomerOrderSlip
                or ProcedureEnum.CreateDisbursement or ProcedureEnum.ViewInventoryReport)
            {
                return false;
            }

            string? role = await roleService.GetUserRoleAsync(id);
            if (role == MsapRoles.Admin)
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

            bool canViewBillings = userAccess.CanCreateBilling || userAccess.CanDeleteBilling
                || userAccess.CanReviewBilling || userAccess.CanApproveFinalBilling || userAccess.CanCreateCollection || userAccess.CanAccessTreasury
                || userAccess.CanViewMaritimeReport;
            bool canViewCollections = userAccess.CanCreateCollection || userAccess.CanAccessTreasury || userAccess.CanRevertCollectionBanking || userAccess.CanViewMaritimeReport;
            bool canViewOperations = userAccess.CanCreateCustomerOrderSlip || userAccess.CanEditCustomerOrderSlip
                || userAccess.CanApproveSchedule || userAccess.CanCreateDispatchTicket || userAccess.CanEditDispatchTicket
                || userAccess.CanManageOtherTugCharges || userAccess.CanConfirmOtherTugCharges || canViewBillings || canViewCollections;
            bool canManageMasterFile = userAccess.CanManageCustomerMasterFile || userAccess.CanManageServiceMasterFile
                || userAccess.CanManagePortMasterFile || userAccess.CanManageTerminalMasterFile || userAccess.CanManageVesselMasterFile
                || userAccess.CanManageTugboatMasterFile || userAccess.CanManageTugboatOwnerMasterFile || userAccess.CanManageTugMasterMasterFile
                || userAccess.CanManagePrincipalMasterFile || userAccess.CanManageBankAccountMasterFile;

            return procedure switch
            {
                ProcedureEnum.AccessMsap => canViewOperations || userAccess.CanViewRates || userAccess.CanManageRates
                    || userAccess.CanApproveRateUpload || canManageMasterFile || userAccess.CanManageMsapImport,
                ProcedureEnum.ViewOperations => canViewOperations,
                ProcedureEnum.ViewBillings => canViewBillings,
                ProcedureEnum.ViewCollections => canViewCollections,
                ProcedureEnum.CreateDispatchTicket => userAccess.CanCreateDispatchTicket,
                ProcedureEnum.EditDispatchTicket => userAccess.CanEditDispatchTicket,
                ProcedureEnum.CreateBilling => userAccess.CanCreateBilling,
                ProcedureEnum.DeleteBilling => userAccess.CanDeleteBilling,
                ProcedureEnum.ReverseBilling => userAccess.CanReverseBilling,
                ProcedureEnum.ManagePostedPeriod => false,
                ProcedureEnum.CreateCollection => userAccess.CanCreateCollection,
                ProcedureEnum.CreateCustomerOrderSlip => userAccess.CanCreateCustomerOrderSlip,
                ProcedureEnum.EditCustomerOrderSlip => userAccess.CanEditCustomerOrderSlip,
                ProcedureEnum.ApproveSchedule => userAccess.CanApproveSchedule,
                ProcedureEnum.ViewRates => userAccess.CanViewRates || userAccess.CanManageRates || userAccess.CanApproveRateUpload,
                ProcedureEnum.ManageRates => userAccess.CanManageRates,
                ProcedureEnum.ManageOtherTugCharges => userAccess.CanManageOtherTugCharges,
                ProcedureEnum.ConfirmOtherTugCharges => userAccess.CanConfirmOtherTugCharges,
                ProcedureEnum.ApproveRateUpload => userAccess.CanApproveRateUpload,
                ProcedureEnum.ReviewBilling => userAccess.CanReviewBilling,
                ProcedureEnum.ApproveFinalBilling => userAccess.CanApproveFinalBilling,
                ProcedureEnum.AccessTreasury => userAccess.CanAccessTreasury,
                ProcedureEnum.RevertCollectionBanking => userAccess.CanRevertCollectionBanking,
                ProcedureEnum.ManageCustomerMasterFile => userAccess.CanManageCustomerMasterFile,
                ProcedureEnum.ManageServiceMasterFile => userAccess.CanManageServiceMasterFile,
                ProcedureEnum.ManagePortMasterFile => userAccess.CanManagePortMasterFile,
                ProcedureEnum.ManageTerminalMasterFile => userAccess.CanManageTerminalMasterFile,
                ProcedureEnum.ManageVesselMasterFile => userAccess.CanManageVesselMasterFile,
                ProcedureEnum.ManageTugboatMasterFile => userAccess.CanManageTugboatMasterFile,
                ProcedureEnum.ManageTugboatOwnerMasterFile => userAccess.CanManageTugboatOwnerMasterFile,
                ProcedureEnum.ManageTugMasterMasterFile => userAccess.CanManageTugMasterMasterFile,
                ProcedureEnum.ManagePrincipalMasterFile => userAccess.CanManagePrincipalMasterFile,
                ProcedureEnum.ManageBankAccountMasterFile => userAccess.CanManageBankAccountMasterFile,
                ProcedureEnum.ManageMsapImport => userAccess.CanManageMsapImport,
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

                    AuditTrail auditTrail = await CreateAuditAsync(username, $"Created User Access for {model.DisplayName}. Schedule: prepare={model.CanCreateCustomerOrderSlip}, edit={model.CanEditCustomerOrderSlip}, approve={model.CanApproveSchedule}. Tickets: record={model.CanCreateDispatchTicket}, edit/recalculate={model.CanEditDispatchTicket}. Billing: prepare={model.CanCreateBilling}, discard draft={model.CanDeleteBilling}, reopen={model.CanReverseBilling}, review={model.CanReviewBilling}, approve={model.CanApproveFinalBilling}. Collections: prepare={model.CanCreateCollection}, banking={model.CanAccessTreasury}, revert banking={model.CanRevertCollectionBanking}. Rates: view={model.CanViewRates}, prepare={model.CanManageRates}, approve={model.CanApproveRateUpload}. Other Tug Charges: prepare={model.CanManageOtherTugCharges}, confirm={model.CanConfirmOtherTugCharges}. Master files: customer={model.CanManageCustomerMasterFile}, service={model.CanManageServiceMasterFile}, port={model.CanManagePortMasterFile}, terminal={model.CanManageTerminalMasterFile}, vessel={model.CanManageVesselMasterFile}, tugboat={model.CanManageTugboatMasterFile}, tugboat owner={model.CanManageTugboatOwnerMasterFile}, tug master={model.CanManageTugMasterMasterFile}, principal={model.CanManagePrincipalMasterFile}, bank account={model.CanManageBankAccountMasterFile}. Import={model.CanManageMsapImport}, reports={model.CanViewMaritimeReport}.", model.Id, model.UserId);
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
                if (existing.CanCreateBilling != model.CanCreateBilling) changes.Add($"Create Billing: {existing.CanCreateBilling} → {model.CanCreateBilling}");
                if (existing.CanDeleteBilling != model.CanDeleteBilling) changes.Add($"Discard draft billing: {existing.CanDeleteBilling} → {model.CanDeleteBilling}");
                if (existing.CanReverseBilling != model.CanReverseBilling) changes.Add($"Reopen approved billing: {existing.CanReverseBilling} → {model.CanReverseBilling}");
                if (existing.CanCreateCollection != model.CanCreateCollection) changes.Add($"Create Collection: {existing.CanCreateCollection} → {model.CanCreateCollection}");
                if (existing.CanCreateCustomerOrderSlip != model.CanCreateCustomerOrderSlip) changes.Add($"Create schedule: {existing.CanCreateCustomerOrderSlip} → {model.CanCreateCustomerOrderSlip}");
                if (existing.CanEditCustomerOrderSlip != model.CanEditCustomerOrderSlip) changes.Add($"Edit booking: {existing.CanEditCustomerOrderSlip} → {model.CanEditCustomerOrderSlip}");
                if (existing.CanApproveSchedule != model.CanApproveSchedule)
                {
                    changes.Add($"Approve Schedule: {existing.CanApproveSchedule} → {model.CanApproveSchedule}");
                }
                if (existing.CanReviewBilling != model.CanReviewBilling)
                {
                    changes.Add($"Review Billing: {existing.CanReviewBilling} → {model.CanReviewBilling}");
                }
                if (existing.CanApproveFinalBilling != model.CanApproveFinalBilling)
                {
                    changes.Add($"Approve Final Billing: {existing.CanApproveFinalBilling} → {model.CanApproveFinalBilling}");
                }
                if (existing.CanAccessTreasury != model.CanAccessTreasury) changes.Add($"Collection banking: {existing.CanAccessTreasury} → {model.CanAccessTreasury}");
                if (existing.CanRevertCollectionBanking != model.CanRevertCollectionBanking) changes.Add($"Revert collection banking: {existing.CanRevertCollectionBanking} → {model.CanRevertCollectionBanking}");
                if (existing.CanManageMsapImport != model.CanManageMsapImport) changes.Add($"Manage Import: {existing.CanManageMsapImport} → {model.CanManageMsapImport}");
                if (existing.CanManageCustomerMasterFile != model.CanManageCustomerMasterFile) changes.Add($"Customer MF: {existing.CanManageCustomerMasterFile} → {model.CanManageCustomerMasterFile}");
                if (existing.CanManageServiceMasterFile != model.CanManageServiceMasterFile) changes.Add($"Service MF: {existing.CanManageServiceMasterFile} → {model.CanManageServiceMasterFile}");
                if (existing.CanManagePortMasterFile != model.CanManagePortMasterFile) changes.Add($"Port MF: {existing.CanManagePortMasterFile} → {model.CanManagePortMasterFile}");
                if (existing.CanManageTerminalMasterFile != model.CanManageTerminalMasterFile) changes.Add($"Terminal MF: {existing.CanManageTerminalMasterFile} → {model.CanManageTerminalMasterFile}");
                if (existing.CanManageVesselMasterFile != model.CanManageVesselMasterFile) changes.Add($"Vessel MF: {existing.CanManageVesselMasterFile} → {model.CanManageVesselMasterFile}");
                if (existing.CanManageTugboatMasterFile != model.CanManageTugboatMasterFile) changes.Add($"Tugboat MF: {existing.CanManageTugboatMasterFile} → {model.CanManageTugboatMasterFile}");
                if (existing.CanManageTugboatOwnerMasterFile != model.CanManageTugboatOwnerMasterFile) changes.Add($"Tugboat Owner MF: {existing.CanManageTugboatOwnerMasterFile} → {model.CanManageTugboatOwnerMasterFile}");
                if (existing.CanManageTugMasterMasterFile != model.CanManageTugMasterMasterFile) changes.Add($"Tug Master MF: {existing.CanManageTugMasterMasterFile} → {model.CanManageTugMasterMasterFile}");
                if (existing.CanManagePrincipalMasterFile != model.CanManagePrincipalMasterFile) changes.Add($"Principal MF: {existing.CanManagePrincipalMasterFile} → {model.CanManagePrincipalMasterFile}");
                if (existing.CanManageBankAccountMasterFile != model.CanManageBankAccountMasterFile) changes.Add($"Bank Account MF: {existing.CanManageBankAccountMasterFile} → {model.CanManageBankAccountMasterFile}");
                if (existing.CanViewMaritimeReport != model.CanViewMaritimeReport) changes.Add($"View Maritime: {existing.CanViewMaritimeReport} → {model.CanViewMaritimeReport}");

                await unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    if (changes.Any())
                    {
                        AuditTrail auditTrail = await CreateAuditAsync(username, $"Edited User Access for {existing.DisplayName}: {string.Join("; ", changes)}", existing.Id, existing.UserId);
                        await unitOfWork.AuditTrail.AddAsync(auditTrail, cancellationToken);
                    }

                    ClearRetiredPermissions(existing);
                    existing.CanCreateDispatchTicket = model.CanCreateDispatchTicket;
                    existing.CanEditDispatchTicket = model.CanEditDispatchTicket;
                    existing.CanCreateBilling = model.CanCreateBilling;
                    existing.CanDeleteBilling = model.CanDeleteBilling;
                    existing.CanReverseBilling = model.CanReverseBilling;
                    existing.CanCreateCollection = model.CanCreateCollection;
                    existing.CanCreateCustomerOrderSlip = model.CanCreateCustomerOrderSlip;
                    existing.CanEditCustomerOrderSlip = model.CanEditCustomerOrderSlip;
                    existing.CanApproveSchedule = model.CanApproveSchedule;
                    existing.CanViewRates = model.CanViewRates;
                    existing.CanManageRates = model.CanManageRates;
                    existing.CanManageOtherTugCharges = model.CanManageOtherTugCharges;
                    existing.CanConfirmOtherTugCharges = model.CanConfirmOtherTugCharges;
                    existing.CanApproveRateUpload = model.CanApproveRateUpload;
                    existing.CanReviewBilling = model.CanReviewBilling;
                    existing.CanApproveFinalBilling = model.CanApproveFinalBilling;
                    existing.CanAccessTreasury = model.CanAccessTreasury;
                    existing.CanRevertCollectionBanking = model.CanRevertCollectionBanking;
                    existing.CanManageMsapImport = model.CanManageMsapImport;
                    existing.CanManageCustomerMasterFile = model.CanManageCustomerMasterFile;
                    existing.CanManageServiceMasterFile = model.CanManageServiceMasterFile;
                    existing.CanManagePortMasterFile = model.CanManagePortMasterFile;
                    existing.CanManageTerminalMasterFile = model.CanManageTerminalMasterFile;
                    existing.CanManageVesselMasterFile = model.CanManageVesselMasterFile;
                    existing.CanManageTugboatMasterFile = model.CanManageTugboatMasterFile;
                    existing.CanManageTugboatOwnerMasterFile = model.CanManageTugboatOwnerMasterFile;
                    existing.CanManageTugMasterMasterFile = model.CanManageTugMasterMasterFile;
                    existing.CanManagePrincipalMasterFile = model.CanManagePrincipalMasterFile;
                    existing.CanManageBankAccountMasterFile = model.CanManageBankAccountMasterFile;
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
            model.CanEditBilling = false;
            model.CanSetTariff = false;
            model.CanApproveTariff = false;
            model.CanDeleteDispatchTicket = false;
            model.CanDeleteCustomerOrderSlip = false;
            model.CanCloseCustomerOrderSlip = false;
            model.CanCreateDisbursement = false;
            model.CanViewInventoryReport = false;
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
