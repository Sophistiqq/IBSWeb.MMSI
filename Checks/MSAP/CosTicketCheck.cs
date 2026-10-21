using System.Text.Json;
using IBS.DataAccess.MSAP.Data;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Models.MSAP.ViewModels;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class CosTicketCheck
    {
        public static async Task RunAsync(MsapDbContext db, IUnitOfWork work, VesselScheduleService schedules)
        {
            db.ChangeTracker.Clear();
            var customer = await db.Customers.FirstAsync();
            var vessel = await db.MsapVessels.FirstAsync();
            var terminal = await db.MsapTerminals.FirstAsync();
            var service = await db.MsapServices.FirstAsync();
            var companyTug = await db.MsapTugboats.FirstAsync();
            var subcontractor = new Tugboat
            {
                TugboatNumber = "C01", TugboatName = "COS subcontractor", PortId = terminal.PortId,
                IsCompanyOwned = false
            };
            var master = new TugMaster { TugMasterNumber = "COS01", TugMasterName = "COS check master" };
            db.AddRange(subcontractor, master);
            await db.SaveChangesAsync();
            var schedule = new VesselSchedule
            {
                CustomerId = customer.CustomerId, VesselId = vessel.VesselId,
                PortId = terminal.PortId, TerminalId = terminal.TerminalId,
                PlannedStart = new DateTime(2025, 6, 1, 8, 0, 0), PlannedEnd = new DateTime(2025, 6, 1, 10, 0, 0),
                AssignedTugboatIds = JsonSerializer.Serialize(new[] { companyTug.TugboatId, subcontractor.TugboatId }),
                ServiceEntries = new[] { companyTug.TugboatId, subcontractor.TugboatId }.Select(tugId => new VesselScheduleServiceEntry
                {
                    TugboatId = tugId, ServiceId = service.ServiceId,
                    TariffBasis = TariffChargeBasis.PerMove, BafMethod = BafCalculationMethod.PerHour,
                    ManualTariffRate = 0m, ManualBafRate = 35000m
                }).ToList()
            };
            Check((await schedules.CreateAsync(schedule, "check")).IsSuccess, "Could not prepare COS check.");
            await db.Entry(schedule).ReloadAsync();
            var approved = await schedules.ConfirmAsync(schedule.VesselScheduleId, schedule.CreatedDate, "check");
            Check(approved.IsSuccess, approved.Message!);
            var job = (await work.CustomerOrderSlip.GetAsync(j => j.CustomerOrderSlipId == approved.Data))!;
            Check(job.CustomerOrderSlipNumber.StartsWith("COS-", StringComparison.Ordinal), "New COS uses the old number prefix.");
            Check(!(await work.DispatchTicket.GetAllAsync(t => t.CustomerOrderSlipId == job.CustomerOrderSlipId)).Any(),
                "Approval created tickets before actual details were entered.");
            Check((await schedules.ConfirmAsync(schedule.VesselScheduleId, schedule.EditedDate!.Value, "check")).IsSuccess,
                "Repeated approval failed.");
            Check((await JobProgressCalculator.LoadAsync(work, job, schedule, default)).Stage == 2,
                "Unrecorded services counted as completed.");
            var dispatch = new DispatchTicketService(work, null!, NullLogger<DispatchTicketService>.Instance);
            var entry = schedule.ServiceEntries[0];
            var actual = await dispatch.PopulateDispatchTicketViewModelAsync(new DispatchTicketViewModel
            {
                VesselScheduleServiceEntryId = entry.VesselScheduleServiceEntryId
            }, job.CustomerOrderSlipId, default);
            Check(actual.TugBoatId == entry.TugboatId && actual.ServiceId == entry.ServiceId,
                "Actual-entry form did not load the selected service.");
            Check(!(await dispatch.CreateDispatchTicketAsync(actual, null, null, "check", default)).IsSuccess,
                "An empty actual ticket was saved.");
            actual.TugMasterId = master.TugMasterId;
            actual.DispatchNumber = "ACTUAL-COS-01";
            actual.DateLeft = new DateOnly(2025, 6, 1); actual.DateArrived = actual.DateLeft;
            actual.TimeLeft = new TimeOnly(8, 0); actual.TimeArrived = new TimeOnly(8, 30);
            actual.ServiceId = int.MaxValue;
            Check(!(await dispatch.CreateDispatchTicketAsync(actual, null, null, "check", default)).IsSuccess,
                "Actual entry could replace its approved service.");
            actual.ServiceId = entry.ServiceId;
            var saved = await dispatch.CreateDispatchTicketAsync(actual, null, null, "check", default);
            Check(saved.IsSuccess, saved.Message!);
            var ticket = (await work.DispatchTicket.GetAsync(t => t.DispatchTicketId == saved.Data))!;
            Check(ticket.VesselScheduleServiceEntryId == entry.VesselScheduleServiceEntryId
                && ticket.RawDurationMinutes == 30 && ticket.TotalHours == 1m,
                "Actual ticket lost its service link, raw duration or minimum hours.");
            Check(!(await dispatch.CreateDispatchTicketAsync(actual, null, null, "check", default)).IsSuccess
                && (await work.DispatchTicket.GetAllAsync(t => t.CustomerOrderSlipId == job.CustomerOrderSlipId)).Count() == 1,
                "Duplicate actual entry created a second ticket.");
            Check(!(await dispatch.DeleteTicketAsync(ticket.DispatchTicketId, "check", default)).IsSuccess,
                "A required ticket could be deleted to bypass completion.");
            Check((await JobProgressCalculator.LoadAsync(work, job, schedule, default)).Stage == 2
                && !(await new CustomerOrderSlipService(work, NullLogger<CustomerOrderSlipService>.Instance)
                    .CompleteBookingAsync(job.CustomerOrderSlipId, "check", default)).IsSuccess,
                "One recorded service bypassed the missing subcontractor ticket.");
            Check(await JobProgressCalculator.GetBlockerAsync(work, job.CustomerOrderSlipId, JobProgressCalculator.BillingStage, default) != null,
                "Billing bypassed an unrecorded expected service.");
            var remaining = schedule.ServiceEntries[1];
            var second = await dispatch.PopulateDispatchTicketViewModelAsync(new DispatchTicketViewModel
            {
                VesselScheduleServiceEntryId = remaining.VesselScheduleServiceEntryId
            }, job.CustomerOrderSlipId, default);
            second.DispatchNumber = "ACTUAL-COS-02";
            second.DateLeft = actual.DateLeft; second.DateArrived = actual.DateArrived;
            second.TimeLeft = actual.TimeLeft; second.TimeArrived = actual.TimeArrived; second.TugMasterId = master.TugMasterId;
            var secondSaved = await dispatch.CreateDispatchTicketAsync(second, null, null, "check", default);
            Check(secondSaved.IsSuccess, secondSaved.Message!);
            Check((await JobProgressCalculator.LoadAsync(work, job, schedule, default)).Stage == JobProgressCalculator.TariffStage,
                "Recording all required services did not advance service progress.");
            var changed = new DispatchTicketViewModel();
            changed.FromEntity(ticket);
            changed.ServiceId = int.MaxValue;
            Check(!(await dispatch.UpdateDispatchTicketAsync(changed, null, null, "check", default)).IsSuccess,
                "Editing actual details replaced the approved service link.");
            Check((await new CustomerOrderSlipService(work, NullLogger<CustomerOrderSlipService>.Instance)
                .CompleteBookingAsync(job.CustomerOrderSlipId, "check", default)).IsSuccess, "Complete services could not close operational booking.");
            changed.FromEntity(ticket);
            Check((await dispatch.UpdateDispatchTicketAsync(changed, null, null, "check", default)).IsSuccess,
                "Operational completion prevented correction of an unbilled actual ticket.");
            Console.WriteLine("PASS: COS approval creates no tickets; actual entry links one ticket per service, rejects empty/duplicate/changed services and blocks completion/billing while requirements remain.");
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
