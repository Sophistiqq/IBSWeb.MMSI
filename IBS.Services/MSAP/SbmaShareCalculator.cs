using IBS.Models.MSAP;

namespace IBS.Services.MSAP
{
    public static class SbmaShareCalculator
    {
        public static Dictionary<int, decimal> Allocate(IEnumerable<DispatchTicket> tickets)
        {
            var amounts = new Dictionary<int, decimal>();
            foreach (var group in tickets.GroupBy(t => (t.CustomerOrderSlipId, t.TugBoatId)))
            {
                var ordered = group.OrderBy(t => t.DispatchTicketId).ToList();
                decimal total = Math.Round(ordered.Sum(t => ActualShare(t, t.ExpectedServiceEntry?.VesselSchedule?.SbmaEnabled == true)), 2);
                decimal allocated = 0m;
                for (int index = 0; index < ordered.Count; index++)
                {
                    var ticket = ordered[index];
                    decimal amount = index == ordered.Count - 1 ? total - allocated
                        : Calculate(ticket, ticket.ExpectedServiceEntry?.VesselSchedule?.SbmaEnabled == true);
                    amounts[ticket.DispatchTicketId] = amount;
                    allocated += amount;
                }
            }
            return amounts;
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
