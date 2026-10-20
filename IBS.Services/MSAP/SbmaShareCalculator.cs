using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;

namespace IBS.Services.MSAP
{
    public static class SbmaShareCalculator
    {
        public static Dictionary<int, decimal> Allocate(IEnumerable<DispatchTicket> tickets, bool? enabled = null)
        {
            var amounts = new Dictionary<int, decimal>();
            foreach (var group in tickets.GroupBy(t => (t.CustomerOrderSlipId, t.TugBoatId)))
            {
                var ordered = group.OrderBy(t => t.DispatchTicketId).ToList();
                decimal total = Math.Round(ordered.Sum(t => ActualShare(t, enabled ?? t.ExpectedServiceEntry?.VesselSchedule?.SbmaEnabled == true)), 2);
                decimal allocated = 0m;
                for (int index = 0; index < ordered.Count; index++)
                {
                    var ticket = ordered[index];
                    decimal amount = index == ordered.Count - 1 ? total - allocated
                        : Math.Min(total - allocated, Calculate(ticket, enabled ?? ticket.ExpectedServiceEntry?.VesselSchedule?.SbmaEnabled == true));
                    amounts[ticket.DispatchTicketId] = amount;
                    allocated += amount;
                }
            }
            return amounts;
        }

        public static void Apply(IEnumerable<DispatchTicket> tickets, bool enabled)
        {
            List<DispatchTicket> all = tickets.ToList();
            Dictionary<int, decimal> amounts = Allocate(all.Where(x => x.Status != MsapConstants.DispatchTicketStatus.Deleted), enabled);
            foreach (DispatchTicket ticket in all)
            {
                ticket.SbmaShareAmount = amounts.GetValueOrDefault(ticket.DispatchTicketId);
            }
        }

        public static decimal Calculate(DispatchTicket ticket, bool enabled)
        {
            return Math.Round(ActualShare(ticket, enabled), 2);
        }

        private static decimal ActualShare(DispatchTicket ticket, bool enabled)
        {
            if (!enabled || !ticket.DateLeft.HasValue || !ticket.DateArrived.HasValue
                || !ticket.TimeLeft.HasValue || !ticket.TimeArrived.HasValue)
            {
                return 0m;
            }
            long ticks = (ticket.DateArrived.Value.ToDateTime(ticket.TimeArrived.Value)
                - ticket.DateLeft.Value.ToDateTime(ticket.TimeLeft.Value)).Ticks;
            return ticks <= 0 ? 0m : ticks / (decimal)TimeSpan.TicksPerMinute * 87.5m;
        }
    }
}
