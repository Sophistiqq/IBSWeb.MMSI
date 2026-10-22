using System.Reflection;
using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using IBS.Models.MSAP;
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
            Console.WriteLine("PASS: required tariff, missing BAF defaults to zero, table rates preserved and explicit zero BAF retained (no database).");
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
