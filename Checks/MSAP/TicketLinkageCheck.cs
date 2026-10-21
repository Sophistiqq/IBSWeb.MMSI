using IBS.DataAccess.MSAP.Data;
using IBS.Models.MSAP;
using IBS.Models.MSAP.MasterFile;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Checks.MSAP
{
    internal static class TicketLinkageCheck
    {
        public static async Task RunAsync(MsapDbContext db)
        {
            var customer = new Customer
            {
                CustomerCode = "TLK0001", CustomerName = "Ticket linkage check", CustomerAddress = "Check address",
                CustomerTin = "000-000-000-00000", CustomerTerms = "COD", CustomerType = "Industrial", VatType = "Vatable", ZipCode = "1000"
            };
            var vessel = new Vessel { VesselNumber = "TL01", VesselName = "Linkage Vessel", VesselType = "LOCAL" };
            var port = new Port { PortNumber = "TL1", PortName = "Linkage Port" };
            var terminal = new Terminal { TerminalNumber = "TL1", TerminalName = "Linkage Terminal", Port = port };
            var tug = new Tugboat { TugboatNumber = "TLT1", TugboatName = "Linkage Tug", Port = port };
            var service = new Service { ServiceNumber = "999", ServiceName = "Linkage Service" };
            var schedule = new VesselSchedule
            {
                Customer = customer, Vessel = vessel, Port = port, Terminal = terminal,
                PlannedStart = new DateTime(2030, 1, 1, 8, 0, 0), PlannedEnd = new DateTime(2030, 1, 1, 10, 0, 0)
            };
            var entry = new VesselScheduleServiceEntry
            {
                VesselSchedule = schedule, Tugboat = tug, Service = service
            };
            var ticket = new DispatchTicket
            {
                DispatchNumber = "TLKT-001", Customer = customer, Vessel = vessel, Port = port, Terminal = terminal,
                Tugboat = tug, Service = service, Date = new DateOnly(2030, 1, 1), Status = MsapConstants.DispatchTicketStatus.ForTariff,
                CreatedBy = "check-user", TotalHours = 1m
            };

            db.AddRange(schedule, entry, ticket);
            await db.SaveChangesAsync();

            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261016042536_AddRevisionBoundApprovalEvidence");
            await migrator.MigrateAsync();
            await db.Entry(ticket).ReloadAsync();

            Check(ticket.VesselScheduleServiceEntryId == null && ticket.RawDurationHours == null && ticket.RawDurationMinutes == null
                && !ticket.IsManualTariffRate && !ticket.IsManualBafRate,
                "Ticket linkage migration fabricated linkage or duration values.");

            ticket.VesselScheduleServiceEntryId = entry.VesselScheduleServiceEntryId;
            ticket.RawDurationHours = 0.5m;
            ticket.RawDurationMinutes = 30;
            ticket.IsManualTariffRate = true;
            ticket.IsManualBafRate = false;
            await db.SaveChangesAsync();

            await db.Entry(ticket).ReloadAsync();
            Check(ticket.VesselScheduleServiceEntryId == entry.VesselScheduleServiceEntryId && ticket.RawDurationHours == 0.5m
                && ticket.RawDurationMinutes == 30 && ticket.IsManualTariffRate && !ticket.IsManualBafRate,
                "Ticket linkage and raw duration values did not persist.");

            foreach ((FormattableString command, string expectedCode) in new (FormattableString, string)[]
            {
                ($"UPDATE msap.mmsi_dispatch_tickets SET raw_duration_hours = -1 WHERE \"RECID\" = {ticket.DispatchTicketId}", PostgresErrorCodes.CheckViolation),
                ($"UPDATE msap.mmsi_dispatch_tickets SET raw_duration_minutes = -5 WHERE \"RECID\" = {ticket.DispatchTicketId}", PostgresErrorCodes.CheckViolation),
                ($"UPDATE msap.mmsi_dispatch_tickets SET vessel_schedule_service_entry_id = 999999 WHERE \"RECID\" = {ticket.DispatchTicketId}", PostgresErrorCodes.ForeignKeyViolation),
                ($"DELETE FROM msap.mmsi_vessel_schedule_service_entries WHERE vessel_schedule_service_entry_id = {entry.VesselScheduleServiceEntryId}", PostgresErrorCodes.ForeignKeyViolation)
            })
            {
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(command);
                    throw new InvalidOperationException("Database allowed invalid duration or foreign key operation.");
                }
                catch (PostgresException exception) when (exception.SqlState == expectedCode)
                {
                    await transaction.RollbackAsync();
                }
            }

            Check(!db.Database.HasPendingModelChanges(), "Ticket linkage migration does not match the model.");
            Console.WriteLine("PASS: dispatch ticket linkage and raw duration migration defaults correctly; constraints reject negative durations and orphan references; entry deletion is restricted.");
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
