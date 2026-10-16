using IBS.DataAccess.MSAP.Repository.IRepository;
using IBS.Models.MSAP;
using IBS.Models.MSAP.ViewModels;

namespace IBS.DataAccess.MSAP.Repository.Msap.IRepository
{
    public interface IDispatchTicketRepository : IRepository<DispatchTicket>
    {
        Task SaveAsync(CancellationToken cancellationToken);

        Task<DispatchTicket?> GetDispatchTicketWithDetailsAsync(int id, CancellationToken cancellationToken = default);

        Task<IEnumerable<DispatchTicket>> GetAllDispatchTicketsWithDetailsAsync(CancellationToken cancellationToken = default);

        Task<IEnumerable<DispatchTicket>> GetDispatchTicketsWithDetailsAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default);

        Task<bool> IsCustomerOrderSlipEditableAsync(int? customerOrderSlipId, CancellationToken cancellationToken = default);

        Task<DispatchTicketViewModel> GetDispatchTicketSelectLists(DispatchTicketViewModel model, CancellationToken cancellationToken = default);

        Task<(IEnumerable<DispatchTicket> Data, int RecordsFiltered, int TotalRecords)> GetPagedDispatchTicketsAsync(DataTablesParameters parameters, string filterType, CancellationToken cancellationToken = default);       }
}
