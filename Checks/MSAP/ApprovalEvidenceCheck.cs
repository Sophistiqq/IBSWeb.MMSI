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
    internal static class ApprovalEvidenceCheck
    {
        public static async Task RunAsync(MsapDbContext db)
        {
            var customer = new Customer
            {
                CustomerCode = "APV0001", CustomerName = "Approval evidence check", CustomerAddress = "Check address",
                CustomerTin = "000-000-000-00000", CustomerTerms = "COD", CustomerType = "Industrial", VatType = "Vatable", ZipCode = "1000"
            };
            var vessel = new Vessel { VesselNumber = "AP01", VesselName = "Approval Vessel", VesselType = "LOCAL" };
            var port = new Port { PortNumber = "AP1", PortName = "Approval Port" };
            var terminal = new Terminal { TerminalNumber = "AP1", TerminalName = "Approval Terminal", Port = port };
            var schedule = new VesselSchedule
            {
                Customer = customer, Vessel = vessel, Port = port, Terminal = terminal,
                PlannedStart = new DateTime(2030, 1, 1, 8, 0, 0), PlannedEnd = new DateTime(2030, 1, 1, 10, 0, 0)
            };
            var bill = new Billing
            {
                MsapBillingNumber = "APPROVAL01", BilledTo = customer.CustomerCode, Customer = customer, Vessel = vessel,
                Port = port, Terminal = terminal, Date = new DateOnly(2030, 1, 1), DueDate = new DateOnly(2030, 1, 1),
                Year = 2030, Company = "MMSI", Amount = 100m, Status = MsapConstants.BillingStatus.ForPosting
            };
            db.AddRange(schedule, bill);
            await db.SaveChangesAsync();
            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261016042156_AddScheduleCommercialSettings");
            await migrator.MigrateAsync();
            await db.Entry(schedule).ReloadAsync();
            await db.Entry(bill).ReloadAsync();
            Check(schedule.Revision == 1 && schedule.ApprovedRevision == null && schedule.ApprovedAt == null && schedule.ApprovedByUserId == null
                && bill.Revision == 1 && bill.ApprovedRevision == null && bill.ApprovedAt == null && bill.ApprovedByUserId == null,
                "Approval migration fabricated approval evidence.");

            schedule.ApprovedRevision = bill.ApprovedRevision = 1;
            schedule.ApprovedByUserId = bill.ApprovedByUserId = "shared-check";
            schedule.ApprovedAt = bill.ApprovedAt = new DateTime(2030, 1, 1, 7, 0, 0);
            await db.SaveChangesAsync();
            schedule.Revision = bill.Revision = 2;
            bill.Amount = 125m;
            await db.SaveChangesAsync();
            await db.Entry(schedule).ReloadAsync();
            await db.Entry(bill).ReloadAsync();
            Check(schedule.ApprovedRevision == 1 && schedule.Revision == 2 && bill.ApprovedRevision == 1 && bill.Revision == 2
                && bill.Amount == 125m && bill.MsapBillingNumber == "APPROVAL01", "Revisions lost their previous approval evidence or record identity.");

            foreach ((FormattableString command, string expectedCode) in new (FormattableString, string)[]
            {
                ($"UPDATE msap.mmsi_vessel_schedules SET approved_revision = NULL WHERE vessel_schedule_id = {schedule.VesselScheduleId}", PostgresErrorCodes.CheckViolation),
                ($"UPDATE msap.mmsi_vessel_schedules SET approved_revision = 3 WHERE vessel_schedule_id = {schedule.VesselScheduleId}", PostgresErrorCodes.CheckViolation),
                ($"UPDATE msap.mmsi_billings SET approved_at = NULL WHERE \"RECID\" = {bill.MsapBillingId}", PostgresErrorCodes.CheckViolation),
                ($"UPDATE msap.mmsi_billings SET revision = 0 WHERE \"RECID\" = {bill.MsapBillingId}", PostgresErrorCodes.CheckViolation),
                ($"UPDATE msap.mmsi_vessel_schedules SET approved_by_user_id = 'missing-approver' WHERE vessel_schedule_id = {schedule.VesselScheduleId}", PostgresErrorCodes.ForeignKeyViolation),
                ($"UPDATE msap.mmsi_billings SET approved_by_user_id = 'missing-approver' WHERE \"RECID\" = {bill.MsapBillingId}", PostgresErrorCodes.ForeignKeyViolation)
            })
            {
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(command);
                    throw new InvalidOperationException("Database allowed incomplete or invalid approval evidence.");
                }
                catch (PostgresException exception) when (exception.SqlState == expectedCode)
                {
                    await transaction.RollbackAsync();
                }
            }
            Check(!db.Database.HasPendingModelChanges(), "Approval migration does not match the model.");
            Console.WriteLine("PASS: approval-evidence migration defaults remain pending; schedule/bill revisions and identities persist; incomplete/future approvals and unknown approvers are rejected.");
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
