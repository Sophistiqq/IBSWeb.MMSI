using IBS.DataAccess.MSAP.Repository.MasterFile.IRepository;
using IBS.DataAccess.MSAP.Repository.Msap.IRepository;
using Microsoft.AspNetCore.Mvc.Rendering;
using ITermsRepository = IBS.DataAccess.Repository.Filpride.IRepository.ITermsRepository;

namespace IBS.DataAccess.MSAP.Repository.IRepository
{
    public interface IUnitOfWork : IDisposable
    {
        ICompanyRepository Company { get; }

        Task SaveAsync(CancellationToken cancellationToken = default);

        Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);

        #region--Master Files

        ISupplierRepository Supplier { get; }
        ICustomerRepository Customer { get; }
        IAuditTrailRepository AuditTrail { get; }
        IEmployeeRepository Employee { get; }
        ITermsRepository Terms { get; }

        Task<List<SelectListItem>> GetCustomerListAsyncById(CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetSupplierListAsyncById(string company, CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetTradeSupplierListAsyncById(string company, CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetNonTradeSupplierListAsyncById(string company, CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetHaulerListAsyncById(string company, CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetBankAccountListById(CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetEmployeeListById(CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetCompanyListAsyncByName(CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetCompanyListAsyncById(CancellationToken cancellationToken = default);

        Task<List<SelectListItem>> GetCashierListAsyncByUsernameAsync(CancellationToken cancellationToken = default);


        #endregion

        #region --Master File

        IBankAccountRepository BankAccount { get; }

        #endregion

        #region --MSAP

        IMsapRepository Msap { get; }
        ICustomerOrderSlipRepository CustomerOrderSlip { get; }
        IDispatchTicketRepository DispatchTicket { get; }
        IBillingRepository Billing { get; }
        ICollectionRepository Collection { get; }
        IReportRepository Report { get; }
        IServiceRepository Service { get; }
        ITariffTableRepository TariffTable { get; }
        IBafTableRepository BafTable { get; }
        IPortRepository Port { get; }
        IPrincipalRepository Principal { get; }
        ITerminalRepository Terminal { get; }
        ITugboatRepository Tugboat { get; }
        ITugMasterRepository TugMaster { get; }
        ITugboatOwnerRepository TugboatOwner { get; }
        IUserAccessRepository UserAccess { get; }
        IVesselRepository Vessel { get; }
        IVesselScheduleRepository VesselSchedule { get; }

        #endregion

        #region -- Posting Period --

        IPostedPeriodRepository PostedPeriod { get; }

        #endregion

    }
}
