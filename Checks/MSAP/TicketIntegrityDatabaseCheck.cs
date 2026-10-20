using System.Text.Json;
using IBS.DataAccess.Data;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Checks.MSAP
{
    internal static class TicketIntegrityDatabaseCheck
    {
        public static async Task RunAsync()
        {
            string connection = TugboatOwnerCheck.TestConnection();
            await using var db = new MsapDbContext(new DbContextOptionsBuilder<MsapDbContext>()
                .UseNpgsql(connection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "msap")).Options);
            await db.Database.MigrateAsync();
            Check(!db.Database.HasPendingModelChanges(), "Ticket migration differs from the model.");
            string[] migrations = db.Database.GetMigrations().ToArray();
            await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            await db.Database.MigrateAsync();
            await using var shared = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
            var work = new UnitOfWork(db, shared);
            var files = new FixtureStorage();
            var dispatch = new DispatchTicketService(work, files, NullLogger<DispatchTicketService>.Instance);
            var customer = await db.Customers.FirstAsync(x => x.Company == "MMSI");
            var terminal = await db.MsapTerminals.FirstAsync();
            var vessel = await db.MsapVessels.FirstAsync();
            var tug = await db.MsapTugboats.FirstAsync(x => x.IsCompanyOwned);
            var anotherTug = await db.MsapTugboats.FirstAsync(x => x.IsCompanyOwned && x.TugboatId != tug.TugboatId);
            var captain = await db.MsapTugMasters.FirstAsync(x => x.IsActive);
            var service = await db.MsapServices.FirstAsync();
            var anotherService = await db.MsapServices.FirstAsync(x => x.ServiceId != service.ServiceId);
            DateOnly date = new(1901, 1, 15);
            while (await db.MsapPostedPeriods.AnyAsync(x => x.Year == date.Year && x.Month == date.Month && x.IsClosed))
            {
                date = date.AddMonths(1);
            }
            var roundingService = new Service { ServiceNumber = "ZX9", ServiceName = "Rounding guard fixture" };
            db.Add(roundingService);
            await db.SaveChangesAsync();
            string reference = "CHK-" + Guid.NewGuid().ToString("N")[..10];
            var parent = new CustomerOrderSlip
            {
                CustomerOrderSlipNumber = reference, COSNumber = "Legacy reference", Date = date, Status = "Open",
                CustomerId = customer.CustomerId, VesselId = vessel.VesselId, PortId = terminal.PortId, TerminalId = terminal.TerminalId
            };
            var entry = new VesselScheduleServiceEntry
            {
                TugboatId = tug.TugboatId, ServiceId = service.ServiceId,
                TariffBasis = TariffChargeBasis.PerHour, BafMethod = BafCalculationMethod.PerHour
            };
            var schedule = new VesselSchedule
            {
                CustomerOrderSlip = parent, CustomerId = customer.CustomerId, VesselId = vessel.VesselId,
                PortId = terminal.PortId, TerminalId = terminal.TerminalId, Revision = 1,
                AssignedTugboatIds = JsonSerializer.Serialize(new[] { tug.TugboatId, anotherTug.TugboatId }),
                PlannedStart = date.ToDateTime(new TimeOnly(8, 0)), PlannedEnd = date.ToDateTime(new TimeOnly(10, 0)),
                Status = MsapConstants.VesselScheduleStatus.Tentative, SbmaEnabled = true, ServiceEntries = [entry]
            };
            var secondEntry = new VesselScheduleServiceEntry
            {
                TugboatId = tug.TugboatId, ServiceId = anotherService.ServiceId,
                TariffBasis = TariffChargeBasis.PerHour, BafMethod = BafCalculationMethod.PerHour,
                ManualTariffRate = 0m, ManualBafRate = 0m
            };
            var uploadEntry = new VesselScheduleServiceEntry
            {
                TugboatId = anotherTug.TugboatId, ServiceId = service.ServiceId,
                TariffBasis = TariffChargeBasis.PerHour, BafMethod = BafCalculationMethod.PerHour,
                ManualTariffRate = 0m, ManualBafRate = 0m
            };
            schedule.ServiceEntries.AddRange([secondEntry, uploadEntry]);
            var roundingEntry = new VesselScheduleServiceEntry
            {
                TugboatId = tug.TugboatId, Service = roundingService,
                TariffBasis = TariffChargeBasis.PerHour, BafMethod = BafCalculationMethod.PerHour,
                ManualTariffRate = 0m, ManualBafRate = 0m
            };
            schedule.ServiceEntries.Add(roundingEntry);
            db.Add(schedule);
            await db.SaveChangesAsync();
            DispatchTicketViewModel Input(string suffix, int seconds = 2) => new()
            {
                CustomerOrderSlipId = parent.CustomerOrderSlipId,
                VesselScheduleServiceEntryId = suffix == "2" ? secondEntry.VesselScheduleServiceEntryId : suffix == "F" ? uploadEntry.VesselScheduleServiceEntryId : entry.VesselScheduleServiceEntryId,
                DispatchNumber = reference + suffix, TugBoatId = suffix == "F" ? anotherTug.TugboatId : tug.TugboatId,
                ServiceId = suffix == "2" ? anotherService.ServiceId : service.ServiceId, TugMasterId = captain.TugMasterId,
                Date = date, DateLeft = date, DateArrived = date, TimeLeft = new TimeOnly(8, 0), TimeArrived = new TimeOnly(8, 0, seconds)
            };
            await RejectAsync(Input("U"), "Unapproved COS was accepted.");
            schedule.ApprovedRevision = 1;
            schedule.Status = MsapConstants.VesselScheduleStatus.Confirmed;
            schedule.ApprovedAt = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
            schedule.ApprovedByUserId = await db.Users.Select(x => x.Id).FirstAsync();
            await db.SaveChangesAsync();
            await RejectAsync(Input("R"), "Missing approved tariff/BAF was accepted.");
            entry.ManualTariffRate = 0m;
            entry.ManualBafRate = 0m;
            await db.SaveChangesAsync();
            var partial = Input("P");
            partial.TimeArrived = null;
            await RejectAsync(partial, "Partial actual times were accepted.");
            var backwards = Input("B");
            backwards.TimeArrived = new TimeOnly(7, 59);
            await RejectAsync(backwards, "Reversed actual times were accepted.");
            var wrong = Input("X");
            wrong.VesselScheduleServiceEntryId = int.MaxValue;
            await RejectAsync(wrong, "Wrong COS/service linkage was accepted.");
            Check(files.Uploads == 0, "Invalid entries touched attachment storage.");

            var first = await dispatch.CreateDispatchTicketAsync(Input("1"), null, null, "Check", default);
            var second = await dispatch.CreateDispatchTicketAsync(Input("2"), null, null, "Check", default);
            Check(first.IsSuccess && second.IsSuccess, first.Message + " " + second.Message);
            await RejectAsync(Input("D"), "A duplicate expected-service ticket was accepted.");
            db.ChangeTracker.Clear();
            var stored = await db.MsapDispatchTickets.Where(x => x.CustomerOrderSlipId == parent.CustomerOrderSlipId).OrderBy(x => x.DispatchTicketId).ToListAsync();
            Check(stored.Count == 2 && stored.All(x => x.Status == MsapConstants.DispatchTicketStatus.ForBilling && x.TotalBilling == 0m),
                "Approved zero rates were confused with missing rates.");
            Check(stored.All(x => x.COSNumber == reference) && stored.Sum(x => x.SbmaShareAmount) == 5.83m
                && stored[1].SbmaShareAmount == 2.91m, "COS identity or persisted SBMA rounding is wrong.");
            Check(await db.MsapDispatchTickets.Where(x => x.DispatchTicketId == stored[1].DispatchTicketId)
                .Select(x => x.SbmaShareAmount).SingleAsync() == 2.91m, "A report subset would recalculate the saved rounding allocation.");
            var edit = new DispatchTicketViewModel();
            edit.FromEntity(stored[0]);
            edit.COSNumber = "Forged reference";
            edit.TimeArrived = new TimeOnly(8, 0, 3);
            Check((await dispatch.UpdateDispatchTicketAsync(edit, null, null, "Check", default)).IsSuccess, "Valid actual-ticket edit failed.");
            db.ChangeTracker.Clear();
            var edited = await db.MsapDispatchTickets.SingleAsync(x => x.DispatchTicketId == first.Data);
            Check(edited.COSNumber == reference && edited.SbmaShareAmount == 4.38m, "Edit trusted a supplied COS or lost stored SBMA.");
            int count = await db.MsapDispatchTickets.CountAsync(x => x.CustomerOrderSlipId == parent.CustomerOrderSlipId);
            files.FailUpload = true;
            Check(!(await dispatch.CreateDispatchTicketAsync(Input("F"), Image(), null, "Check", default)).IsSuccess,
                "A failed attachment upload reported success.");
            Check(files.Uploads == 1 && files.Names.Count == 0 && await db.MsapDispatchTickets.CountAsync(x => x.CustomerOrderSlipId == parent.CustomerOrderSlipId) == count,
                "Failed upload left a file or partial ticket.");
            files.FailUpload = false;

            var closed = await db.MsapPostedPeriods.SingleOrDefaultAsync(x => x.Year == date.Year && x.Month == date.Month);
            if (closed == null)
            {
                closed = new MsapPostedPeriod { Year = date.Year, Month = date.Month };
                db.Add(closed);
            }
            closed.IsClosed = true;
            closed.ClosedBy = "Rounding check";
            await db.SaveChangesAsync();
            var late = Input("L");
            late.VesselScheduleServiceEntryId = roundingEntry.VesselScheduleServiceEntryId;
            late.ServiceId = roundingService.ServiceId;
            late.Date = DateOnly.FromDateTime(DateTime.Today).AddDays(-4);
            late.DateLeft = late.DateArrived = late.Date;
            Check(!(await dispatch.CreateDispatchTicketAsync(late, Image(), null, "Check", default)).IsSuccess,
                "New SBMA allocation altered closed-period sibling rounding.");
            Check(files.Uploads == 2 && files.Names.Count == 0
                && await db.MsapDispatchTickets.CountAsync(x => x.CustomerOrderSlipId == parent.CustomerOrderSlipId) == count,
                "A failure after initial ticket persistence left a file or partial database record.");
            var reopened = await db.MsapPostedPeriods.SingleAsync(x => x.Id == closed.Id);
            reopened.IsClosed = false;
            await db.SaveChangesAsync();

            foreach (string assignment in new[] { "time_arrived = NULL", "sbma_share_amount = -1", "customer_order_slip_id = 2147483647" })
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    // The assignment strings are fixed test cases, never external input.
                    string sql = "UPDATE msap.mmsi_dispatch_tickets SET " + assignment + " WHERE \"RECID\" = {0}";
                    await db.Database.ExecuteSqlRawAsync(sql, first.Data);
                    throw new InvalidOperationException("Database accepted an invalid ticket invariant.");
                }
                catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
                {
                    await transaction.RollbackAsync();
                }
            }
            Console.WriteLine("PASS: migration rollback/reapply, missing approval/rates, partial/backwards times, wrong linkage, approved zero rates, COS identity, persisted SBMA/rounding/report subsets, edit and upload rollback, and SQL constraints (isolated database).");

            async Task RejectAsync(DispatchTicketViewModel input, string message)
            {
                int before = await db.MsapDispatchTickets.CountAsync(x => x.CustomerOrderSlipId == parent.CustomerOrderSlipId);
                var result = await dispatch.CreateDispatchTicketAsync(input, Image(), null, "Check", default);
                Check(!result.IsSuccess && await db.MsapDispatchTickets.CountAsync(x => x.CustomerOrderSlipId == parent.CustomerOrderSlipId) == before, message);
            }
        }

        private static IFormFile Image()
        {
            return new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "imageFile", "check.png");
        }

        private static void Check(bool passed, string message)
        {
            if (!passed)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class FixtureStorage : ICloudStorageService
        {
            public HashSet<string> Names { get; } = [];
            public int Uploads { get; private set; }
            public bool FailUpload { get; set; }

            public Task<string> UploadFileAsync(IFormFile fileToUpload, string fileNameToSave)
            {
                Uploads++;
                Names.Add(fileNameToSave);
                if (FailUpload)
                {
                    throw new IOException("Fixture upload failure.");
                }
                return Task.FromResult("https://fixture.invalid/" + fileNameToSave);
            }

            public Task DeleteFileAsync(string fileNameToDelete)
            {
                Names.Remove(fileNameToDelete);
                return Task.CompletedTask;
            }

            public Task<string> GetSignedUrlAsync(string fileNameToRead, int timeOutInMinutes = 30)
            {
                return Task.FromResult("https://fixture.invalid/" + fileNameToRead);
            }

            public Task<Stream> DownloadFileAsync(string fileNameToDownload)
            {
                return Task.FromResult<Stream>(new MemoryStream());
            }

            public Task<IFormFile?> GetFileAsFormFile(string fileName)
            {
                return Task.FromResult<IFormFile?>(null);
            }
        }
    }
}
