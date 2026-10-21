using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IBillingRepository : IRepository<Billing>
    {
        Task<Billing?> GetForUpdateAsync(int id, CancellationToken ct = default);

        Task SaveAsync(CancellationToken cancellationToken);

        Task AddSalesBookAsync(IBS.Models.MSAP.Books.SalesBook entry, CancellationToken ct);

        Task<bool> HasSalesBookAsync(int billingId, CancellationToken ct);

        Task VoidSalesBookAsync(int billingId, string actor, string? reason, DateTime voidedAt, CancellationToken ct);

        Task<List<string>?> GetToBillDispatchTicketListAsync(int billingId, CancellationToken cancellationToken = default);

        Task<List<string>?> GetUniqueTugboatsListAsync(int billingId, CancellationToken cancellationToken = default);

        Task<List<DispatchTicket>?> GetPaidDispatchTicketsAsync(int billingId, CancellationToken cancellationToken = default);

        Task<List<SelectListItem>?> GetMsapCustomersWithBillablesSelectList(int? currentCustomerId, string type, CancellationToken cancellationToken = default);

        Task<List<SelectListItem>?> GetMsapUnbilledTicketsByCustomer(int? customerId, CancellationToken cancellationToken);

        Task<List<SelectListItem>> GetMsapBilledTicketsById(int id, CancellationToken cancellationToken = default);

        Task<string> GenerateBillingNumber(int year, CancellationToken cancellationToken = default);

        Billing ProcessAddress(Billing model, CancellationToken cancellationToken = default);

        Task<(IEnumerable<Billing> Data, int RecordsFiltered, int TotalRecords)> GetPagedBillingsAsync(DataTablesParameters parameters, CancellationToken cancellationToken, int? customerOrderSlipId = null);

        Task<List<Billing>> GetBillingsByCollectionIdAsync(int collectionId, CancellationToken cancellationToken);
    }
}
