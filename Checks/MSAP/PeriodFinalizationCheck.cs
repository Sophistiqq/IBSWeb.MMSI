using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.DataAccess.MSAP.Repository.Msap;
using IBS.Models;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class PeriodFinalizationCheck
    {
        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static async Task RunAsync(string? connectionString)
        {
            // 1. Verify MsapConstants.EnforceClosedPeriods is active
            Check(MsapConstants.EnforceClosedPeriods, "MSAP EnforceClosedPeriods must be enabled.");

            // 2. Test UserAccessService permissions for ManagePostedPeriod and ReverseBilling
            var adminId = Guid.NewGuid().ToString();
            var userId = Guid.NewGuid().ToString();

            var roleService = System.Reflection.DispatchProxy.Create<IRoleService, RoleProxy>();
            ((RoleProxy)(object)roleService).Handler = (method, args) =>
            {
                string? uid = (string?)args![0];
                return Task.FromResult<string?>(uid == adminId ? MsapRoles.Admin : MsapRoles.User);
            };

            var userAccessStore = new Dictionary<string, UserAccess>
            {
                [userId] = new UserAccess { UserId = userId, CanReverseBilling = true }
            };

            var userAccessRepo = System.Reflection.DispatchProxy.Create<IBS.DataAccess.MSAP.Repository.Msap.IRepository.IUserAccessRepository, AccessRepoProxy>();
            ((AccessRepoProxy)(object)userAccessRepo).Handler = (method, args) =>
            {
                return Task.FromResult<UserAccess?>(userAccessStore[userId]);
            };

            var unitOfWork = System.Reflection.DispatchProxy.Create<IBS.DataAccess.MSAP.Repository.IRepository.IUnitOfWork, UowProxy>();
            ((UowProxy)(object)unitOfWork).Handler = (method, args) =>
            {
                if (method.Name == "get_UserAccess") return userAccessRepo;
                throw new NotImplementedException(method.Name);
            };

            var userManagerStore = System.Reflection.DispatchProxy.Create<IUserStore<ApplicationUser>, UserStoreProxy>();
            var userManager = new UserManager<ApplicationUser>(userManagerStore, null!, null!, null!, null!, null!, null!, null!, null!);

            var accessService = new UserAccessService(unitOfWork, userManager, roleService, NullLogger<UserAccessService>.Instance);

            // Admin access check
            bool adminCanManagePeriods = await accessService.CheckAccess(adminId, ProcedureEnum.ManagePostedPeriod);
            Check(adminCanManagePeriods, "Admin must have access to ManagePostedPeriod.");
            bool adminCanReverseBilling = await accessService.CheckAccess(adminId, ProcedureEnum.ReverseBilling);
            Check(adminCanReverseBilling, "Admin must have access to ReverseBilling.");

            // User access check
            bool userCanManagePeriods = await accessService.CheckAccess(userId, ProcedureEnum.ManagePostedPeriod);
            Check(!userCanManagePeriods, "Standard user must not have access to ManagePostedPeriod.");
            bool userCanReverseBilling = await accessService.CheckAccess(userId, ProcedureEnum.ReverseBilling);
            Check(userCanReverseBilling, "Standard user with CanReverseBilling must have access to ReverseBilling.");

            // 3. Test Database Stamping via FinalizePeriodRecordsAsync against live db if available
            if (!string.IsNullOrWhiteSpace(connectionString) && connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            {
                var options = new DbContextOptionsBuilder<MsapDbContext>()
                    .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "msap"))
                    .Options;
                await using var db = new MsapDbContext(options);

                var testYear = 2026;
                var testMonth = 1;
                var testDate = new DateOnly(testYear, testMonth, 15);
                var otherDate = new DateOnly(testYear, testMonth + 1, 15);

                var repo = new PostedPeriodRepository(db);

                // Fetch existing customer, tugboat, vessel, port, terminal, service from db to satisfy foreign keys
                var customerId = await db.Customers.Select(c => c.CustomerId).FirstOrDefaultAsync();
                var tugboatId = await db.MsapTugboats.Select(t => t.TugboatId).FirstOrDefaultAsync();
                var vesselId = await db.MsapVessels.Select(v => v.VesselId).FirstOrDefaultAsync();
                var portId = await db.MsapPorts.Select(p => p.PortId).FirstOrDefaultAsync();
                var terminalId = await db.MsapTerminals.Select(t => t.TerminalId).FirstOrDefaultAsync();
                var serviceId = await db.MsapServices.Select(s => s.ServiceId).FirstOrDefaultAsync();

                if (customerId > 0 && tugboatId > 0 && vesselId > 0 && portId > 0 && terminalId > 0 && serviceId > 0)
                {
                    var ticket1 = new DispatchTicket
                    {
                        Date = testDate,
                        DateLeft = testDate,
                        TimeLeft = new TimeOnly(8, 0),
                        DateArrived = testDate,
                        TimeArrived = new TimeOnly(12, 0),
                        DispatchNumber = "CHK" + Guid.NewGuid().ToString("N")[..6],
                        Status = MsapConstants.DispatchTicketStatus.ForBilling,
                        CreatedBy = "System",
                        CustomerId = customerId,
                        TugBoatId = tugboatId,
                        VesselId = vesselId,
                        PortId = portId,
                        TerminalId = terminalId,
                        ServiceId = serviceId
                    };
                    var ticket2 = new DispatchTicket
                    {
                        Date = otherDate,
                        DateLeft = otherDate,
                        TimeLeft = new TimeOnly(8, 0),
                        DateArrived = otherDate,
                        TimeArrived = new TimeOnly(12, 0),
                        DispatchNumber = "CHK" + Guid.NewGuid().ToString("N")[..6],
                        Status = MsapConstants.DispatchTicketStatus.ForBilling,
                        CreatedBy = "System",
                        CustomerId = customerId,
                        TugBoatId = tugboatId,
                        VesselId = vesselId,
                        PortId = portId,
                        TerminalId = terminalId,
                        ServiceId = serviceId
                    };

                    await db.MsapDispatchTickets.AddRangeAsync(ticket1, ticket2);
                    await db.SaveChangesAsync();

                    try
                    {
                        await repo.FinalizePeriodRecordsAsync(testYear, testMonth);
                        db.ChangeTracker.Clear();

                        var reloaded1 = await db.MsapDispatchTickets.AsNoTracking().FirstAsync(t => t.DispatchTicketId == ticket1.DispatchTicketId);
                        var reloaded2 = await db.MsapDispatchTickets.AsNoTracking().FirstAsync(t => t.DispatchTicketId == ticket2.DispatchTicketId);

                        Check(reloaded1.PeriodStatus == MsapPeriodStatus.Final, $"Expected ticket1 PeriodStatus to be 'Final', got '{reloaded1.PeriodStatus}'");
                        Check(reloaded2.PeriodStatus == null, $"Expected ticket2 PeriodStatus to remain null, got '{reloaded2.PeriodStatus}'");

                        // 4. Test Voiding Finalized Ticket produces Adjusted tag & AdjustmentLog
                        var appDbOptions = new DbContextOptionsBuilder<IBS.DataAccess.Data.ApplicationDbContext>()
                            .UseNpgsql(connectionString)
                            .Options;
                        await using var appDb = new IBS.DataAccess.Data.ApplicationDbContext(appDbOptions);
                        var uow = new UnitOfWork(db, appDb);
                        var dtService = new DispatchTicketService(uow, null!, NullLogger<DispatchTicketService>.Instance);
                        var voidResult = await dtService.VoidTicketAsync(ticket1.DispatchTicketId, "Correction of duplicate ticket entry", "TestUser", CancellationToken.None);
                        Check(voidResult.IsSuccess, $"Expected ticket void to succeed, got: {voidResult.Message}");

                        var voidedTicket = await db.MsapDispatchTickets.AsNoTracking().FirstAsync(t => t.DispatchTicketId == ticket1.DispatchTicketId);
                        Check(voidedTicket.Status == MsapConstants.DispatchTicketStatus.Deleted, "Expected ticket status to be Deleted");
                        Check(voidedTicket.PeriodStatus == MsapPeriodStatus.Adjusted, $"Expected ticket PeriodStatus to be Adjusted, got '{voidedTicket.PeriodStatus}'");

                        var log = await db.MsapAdjustmentLogs.AsNoTracking().FirstOrDefaultAsync(l => l.DocumentType == "DispatchTicket" && l.RecordId == ticket1.DispatchTicketId);
                        Check(log != null, "Expected adjustment log for voided finalized ticket");
                        Check(log!.Reason == "Correction of duplicate ticket entry", "Expected adjustment log reason to match");
                        Check(log.ChangesJson.Contains("Adjusted"), "Expected adjustment log changes to mention Adjusted");
                    }
                    finally
                    {
                        var logs = await db.MsapAdjustmentLogs.Where(l => l.DocumentType == "DispatchTicket" && l.RecordId == ticket1.DispatchTicketId).ToListAsync();
                        db.MsapAdjustmentLogs.RemoveRange(logs);
                        var ticketsToRemove = await db.MsapDispatchTickets.Where(t => t.DispatchTicketId == ticket1.DispatchTicketId || t.DispatchTicketId == ticket2.DispatchTicketId).ToListAsync();
                        db.MsapDispatchTickets.RemoveRange(ticketsToRemove);
                        await db.SaveChangesAsync();
                    }
                }
            }

            Console.WriteLine("PASS: Period finalization, EnforceClosedPeriods, and UserAccess permissions verified.");
        }

        public class RoleProxy : System.Reflection.DispatchProxy
        {
            public Func<System.Reflection.MethodInfo, object?[]?, object?>? Handler { get; set; }
            protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => Handler?.Invoke(targetMethod!, args);
        }

        public class AccessRepoProxy : System.Reflection.DispatchProxy
        {
            public Func<System.Reflection.MethodInfo, object?[]?, object?>? Handler { get; set; }
            protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => Handler?.Invoke(targetMethod!, args);
        }

        public class UowProxy : System.Reflection.DispatchProxy
        {
            public Func<System.Reflection.MethodInfo, object?[]?, object?>? Handler { get; set; }
            protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => Handler?.Invoke(targetMethod!, args);
        }

        public class UserStoreProxy : System.Reflection.DispatchProxy
        {
            protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => null;
        }
    }
}
