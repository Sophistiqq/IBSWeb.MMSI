using System.Text.Json;
using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Checks.MSAP
{
    internal static class ScheduleServiceEntryCheck
    {
        public static async Task RunAsync(MsapDbContext db)
        {
            Tugboat tug = await db.MsapTugboats.SingleAsync();
            Service service = await db.MsapServices.SingleAsync();
            var schedule = new VesselSchedule
            {
                CustomerId = (await db.Customers.SingleAsync()).CustomerId,
                VesselId = (await db.MsapVessels.SingleAsync()).VesselId,
                PortId = tug.PortId,
                TerminalId = (await db.MsapTerminals.SingleAsync()).TerminalId,
                PlannedStart = new DateTime(2030, 1, 1, 8, 0, 0),
                PlannedEnd = new DateTime(2030, 1, 1, 10, 0, 0),
                AssignedTugboatIds = JsonSerializer.Serialize(new[] { tug.TugboatId })
            };
            db.MsapVesselSchedules.Add(schedule);
            await db.SaveChangesAsync();

            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261016025615_AddWorkflowApprovalPermissions");
            await migrator.MigrateAsync();
            await db.Entry(schedule).ReloadAsync();
            Check(schedule.AssignedTugboatIds == JsonSerializer.Serialize(new[] { tug.TugboatId })
                && !await db.MsapVesselScheduleServiceEntries.AnyAsync(),
                "Service-entry migration changed existing tug assignments or invented service requirements.");

            var secondTug = new Tugboat { TugboatNumber = "002", TugboatName = "Second Tug", PortId = schedule.PortId, IsCompanyOwned = false };
            var secondService = new Service { ServiceNumber = "002", ServiceName = "Second Service" };
            db.AddRange(secondTug, secondService);
            foreach (Tugboat plannedTug in new[] { tug, secondTug })
            {
                foreach (Service plannedService in new[] { service, secondService })
                {
                    schedule.ServiceEntries.Add(new VesselScheduleServiceEntry { Tugboat = plannedTug, Service = plannedService });
                }
            }
            await db.SaveChangesAsync();
            int[] entryIds = schedule.ServiceEntries.Select(entry => entry.VesselScheduleServiceEntryId).Order().ToArray();
            db.ChangeTracker.Clear();
            VesselSchedule reloaded = await db.MsapVesselSchedules.Include(row => row.ServiceEntries).SingleAsync();
            Check(reloaded.ServiceEntries.Count == 4 && reloaded.ServiceEntries.GroupBy(entry => entry.TugboatId)
                .All(group => group.Count() == 2 && group.Select(entry => entry.ServiceId).Distinct().Count() == 2)
                && reloaded.ServiceEntries.Select(entry => entry.VesselScheduleServiceEntryId).Order().SequenceEqual(entryIds),
                "Planned tug/service rows or their stable IDs did not survive reload.");
            reloaded.ServiceEntries.Add(new VesselScheduleServiceEntry { TugboatId = tug.TugboatId, ServiceId = service.ServiceId });
            await db.SaveChangesAsync();
            Check(await db.MsapVesselScheduleServiceEntries.CountAsync() == 5 && !await db.MsapDispatchTickets.AnyAsync()
                && !await db.MsapJobOrders.AnyAsync(), "Repeated services were lost or planning created operational tickets/COS.");

            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM msap.mmsi_vessel_schedules WHERE vessel_schedule_id = {schedule.VesselScheduleId}");
                throw new InvalidOperationException("Deleting a schedule discarded its expected services.");
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.RestrictViolation)
            {
                await transaction.RollbackAsync();
            }
            Check(await db.MsapVesselScheduleServiceEntries.CountAsync() == 5, "Delete protection lost planned service rows.");
            Console.WriteLine("PASS: service-entry migration round-trip; two tugs/two services, repeated services and stable IDs persist without synthetic tickets; schedule deletion is restricted.");
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
