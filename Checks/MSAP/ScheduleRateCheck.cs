using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.Enums;
using IBS.Models.MSAP.MasterFile;
using IBS.Services.MSAP;
using IBS.Utility.MSAP.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Checks.MSAP
{
    internal static class ScheduleRateCheck
    {
        public static async Task RunAsync()
        {
            TariffRate? tariff = null;
            BafRate? baf = null;
            var customer = Proxy<ICustomerRepository>((_, _) => Task.FromResult<Customer?>(new Customer()));
            var vessel = Proxy<IVesselRepository>((_, _) => Task.FromResult<Vessel?>(new Vessel { VesselType = "LOCAL" }));
            var terminal = Proxy<ITerminalRepository>((_, _) => Task.FromResult<Terminal?>(new Terminal()));
            var tugs = Proxy<ITugboatRepository>((_, _) => Task.FromResult<IEnumerable<Tugboat>>([new Tugboat { TugboatId = 1 }]));
            var services = Proxy<IServiceRepository>((_, _) => Task.FromResult<IEnumerable<Service>>([new Service { ServiceId = 1 }]));
            var tariffs = Proxy<ITariffTableRepository>((_, _) => Task.FromResult(tariff));
            var bafs = Proxy<IBafTableRepository>((_, _) => Task.FromResult(baf));
            var work = Proxy<IUnitOfWork>((method, _) => method.Name switch
            {
                "get_Customer" => customer, "get_Vessel" => vessel, "get_Terminal" => terminal,
                "get_Tugboat" => tugs, "get_Service" => services, "get_TariffTable" => tariffs,
                "get_BafTable" => bafs, _ => throw new InvalidOperationException(method.Name)
            });
            var service = new VesselScheduleService(work, null!, NullLogger<VesselScheduleService>.Instance);
            var validate = typeof(VesselScheduleService).GetMethod("ValidateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var entry = new VesselScheduleServiceEntry { TugboatId = 1, ServiceId = 1 };
            var model = new VesselSchedule
            {
                VoyageNumber = "RATE-CHECK", CustomerId = 1, VesselId = 1, TerminalId = 1, PortId = 1,
                PlannedStart = new DateTime(2026, 10, 22, 8, 0, 0), PlannedEnd = new DateTime(2026, 10, 22, 9, 0, 0),
                Status = MsapConstants.VesselScheduleStatus.Cancelled, AssignedTugboatIds = "[1]", ServiceEntries = [entry]
            };
            Task<string?> Validate() => (Task<string?>)validate.Invoke(service, [model, true, CancellationToken.None, false])!;
            Check(await Validate() != null, "A missing tariff without a table rate was accepted.");
            entry.ManualTariffRate = 0;
            Check(await Validate() != null, "A zero manual tariff was accepted.");
            entry.ManualBafRate = null;
            entry.ManualTariffRate = 100;
            Check(await Validate() == null && entry.ManualBafRate == 0, "Blank BAF without a table rate must become zero.");
            entry.ManualTariffRate = null;
            entry.ManualBafRate = null;
            tariff = new TariffRate { Dispatch = 100 };
            baf = new BafRate { Amount = 20 };
            Check(await Validate() == null && entry.ManualTariffRate == null && entry.ManualBafRate == null,
                "Existing table rates must remain table-backed rather than becoming manual values.");
            tariff.Dispatch = 0;
            Check(await Validate() != null, "A zero table tariff was accepted.");
            entry.ManualTariffRate = 0.0001m;
            Check(await Validate() == null, "The smallest supported positive manual tariff was rejected.");
            tariff.Dispatch = 100;
            entry.ManualTariffRate = null;
            entry.ManualBafRate = 0;
            Check(await Validate() == null && entry.ManualBafRate == 0, "An explicit zero BAF was overwritten.");
            MethodInfo setOverrides = typeof(VesselScheduleService).GetMethod("SetTariffOverridesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Task SetOverrides(VesselSchedule? previous = null)
            {
                return (Task)setOverrides.Invoke(service, [model, previous, CancellationToken.None])!;
            }
            entry.ManualTariffRate = 100m;
            tariff = null;
            await SetOverrides();
            Check(entry.IsManualTariffOverride == false, "Missing-match manual tariff was classified as a fixed override.");
            tariff = new TariffRate { Dispatch = 100m };
            await SetOverrides();
            Check(entry.IsManualTariffOverride == false, "A manual tariff equal to the table was classified as an override.");
            tariff.Dispatch = 150m;
            await SetOverrides();
            Check(entry.IsManualTariffOverride == true, "An explicit manual exception lost its protection.");
            var previousSchedule = new VesselSchedule
            {
                CustomerId = model.CustomerId, PortId = model.PortId, TerminalId = model.TerminalId,
                ServiceEntries = [new VesselScheduleServiceEntry
                {
                    VesselScheduleServiceEntryId = entry.VesselScheduleServiceEntryId, ServiceId = entry.ServiceId,
                    ManualTariffRate = entry.ManualTariffRate, IsManualTariffOverride = false
                }]
            };
            await SetOverrides(previousSchedule);
            Check(entry.IsManualTariffOverride == false, "Editing an unchanged schedule converted fallback to a fixed exception.");
            entry.ManualTariffRate = 90m;
            await SetOverrides(previousSchedule);
            Check(entry.IsManualTariffOverride == true, "Editing a manual price against an existing table failed to record an exception.");
            entry.IsManualTariffOverride = null;
            entry.ManualTariffRate = null;
            tariff.Dispatch = 100m;
            model.Status = MsapConstants.VesselScheduleStatus.Tentative;
            var scheduleRepository = Proxy<IVesselScheduleRepository>((_, _) => Task.FromResult<VesselSchedule?>(model));
            var captureWork = Proxy<IUnitOfWork>((method, _) => method.Name == "get_VesselSchedule"
                ? scheduleRepository : throw new InvalidOperationException(method.Name));
            var rates = new RateUploadService(null!, captureWork);
            for (int day = 1; day <= 6; day++)
            {
                var preview = await rates.PreviewScheduleCaptureAsync(1, entry.VesselScheduleServiceEntryId,
                    RateTableKind.BAF, new DateOnly(2028, 2, 29).AddDays(day), CancellationToken.None);
                Check(!preview.IsSuccess && preview.Message == "BAF effective date must be a Tuesday.",
                    "A non-Tuesday BAF capture date was accepted.");
            }
            model.Status = MsapConstants.VesselScheduleStatus.Confirmed;
            model.Revision = 1;
            model.ApprovedRevision = 1;
            model.PlannedStart = new DateTime(2026, 10, 20, 8, 0, 0);
            entry.TariffBasis = TariffChargeBasis.PerHour;
            entry.BafMethod = BafCalculationMethod.PerHour;
            entry.ManualTariffRate = 100m;
            entry.ManualBafRate = 20m;
            var ticket = new DispatchTicket
            {
                TugBoatId = entry.TugboatId, ServiceId = entry.ServiceId,
                VesselScheduleServiceEntryId = entry.VesselScheduleServiceEntryId,
                DateLeft = new DateOnly(2026, 10, 20), DateArrived = new DateOnly(2026, 10, 20),
                TimeLeft = new TimeOnly(8, 0), TimeArrived = new TimeOnly(9, 0)
            };
            Check(ScheduledTicketCharges.Apply(ticket, model, entry, tariff, null) == null
                && ticket.IsManualBafRate && ticket.BAFRate == 20m, "Manual BAF fallback failed without table coverage.");
            baf = new BafRate { Amount = 30m, AsOfDate = new DateOnly(2026, 10, 20), ApprovedAt = new DateTime(2026, 10, 20) };
            var comparison = await ScheduledTicketCharges.PreviewAsync(work, ticket, model, CancellationToken.None);
            Check(comparison.Ticket.BAFRate == 30m && !comparison.Ticket.IsManualBafRate
                && ScheduledTicketCharges.HasChanges(ticket, comparison.Ticket), "Approved table BAF did not invalidate the saved manual BAF.");
            Check(ticket.BAFRate == 20m && ticket.IsManualBafRate && entry.ManualBafRate == 20m,
                "A review preview mutated approved manual amounts.");
            Check(ScheduledTicketCharges.Apply(ticket, model, entry, tariff, baf) == null
                && ticket.BAFRate == 30m && !ticket.IsManualBafRate && ticket.DispatchRate == 100m && ticket.IsManualTariffRate,
                "Recalculation failed to adopt table BAF while retaining manual tariff.");
            baf.Amount = 0m;
            Check(ScheduledTicketCharges.Apply(ticket, model, entry, tariff, baf) == null && ticket.BAFRate == 0m,
                "Explicit table BAF zero did not supersede manual BAF.");
            baf.AsOfDate = new DateOnly(2026, 10, 13);
            Check(ScheduledTicketCharges.Apply(ticket, model, entry, tariff, baf) == null && ticket.IsManualBafRate && ticket.BAFRate == 20m,
                "Expired BAF table coverage superseded valid manual fallback.");
            entry.IsManualTariffOverride = false;
            tariff = null;
            Check(ScheduledTicketCharges.Apply(ticket, model, entry, tariff, baf) == null
                && ticket.DispatchRate == 100m && ticket.IsManualTariffRate, "Missing table tariff did not retain manual fallback.");
            tariff = new TariffRate { Dispatch = 100m, AsOfDate = ticket.DateLeft!.Value, ApprovedAt = model.PlannedStart };
            comparison = await ScheduledTicketCharges.PreviewAsync(work, ticket, model, CancellationToken.None);
            Check(comparison.Ticket.DispatchRate == 100m && !comparison.Ticket.IsManualTariffRate
                && !ScheduledTicketCharges.HasChanges(ticket, comparison.Ticket), "An equal table tariff unnecessarily required recalculation.");
            tariff.Dispatch = 1120000m;
            comparison = await ScheduledTicketCharges.PreviewAsync(work, ticket, model, CancellationToken.None);
            Check(comparison.Ticket.DispatchRate == 1120000m && ScheduledTicketCharges.HasChanges(ticket, comparison.Ticket),
                "A same-effective-date tariff correction did not invalidate saved manual fallback charges.");
            Check(ticket.DispatchRate == 100m && entry.ManualTariffRate == 100m, "Tariff review mutated saved amounts.");
            tariff.AsOfDate = ticket.DateLeft.Value.AddDays(1);
            comparison = await ScheduledTicketCharges.PreviewAsync(work, ticket, model, CancellationToken.None);
            Check(comparison.Ticket.DispatchRate == 100m && !ScheduledTicketCharges.HasChanges(ticket, comparison.Ticket),
                "A future tariff repriced an earlier service.");
            ticket.DateLeft = ticket.DateLeft.Value.AddDays(1);
            ticket.DateArrived = ticket.DateLeft;
            comparison = await ScheduledTicketCharges.PreviewAsync(work, ticket, model, CancellationToken.None);
            Check(comparison.Ticket.DispatchRate == 1120000m && ScheduledTicketCharges.HasChanges(ticket, comparison.Ticket),
                "An approved schedule ignored a new tariff effective on its actual service date.");
            tariff.ApprovedAt = null;
            comparison = await ScheduledTicketCharges.PreviewAsync(work, ticket, model, CancellationToken.None);
            Check(comparison.Ticket.DispatchRate == 100m, "An unapproved tariff replaced manual fallback.");
            tariff.ApprovedAt = model.PlannedStart;
            entry.IsManualTariffOverride = true;
            comparison = await ScheduledTicketCharges.PreviewAsync(work, ticket, model, CancellationToken.None);
            Check(comparison.Ticket.DispatchRate == 100m && comparison.Ticket.IsManualTariffRate,
                "A table correction replaced an explicit manual tariff exception.");
            Console.WriteLine("PASS: manual tariff source classification, equal-rate adoption, dated tariff corrections/new prices, fixed exceptions, Tuesday BAF coverage and strict BAF recalculation (no database).");
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, CollectionCheck.CheckProxy>();
            ((CollectionCheck.CheckProxy)(object)proxy).Handler = handler;
            return proxy;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
