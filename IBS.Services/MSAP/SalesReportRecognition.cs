using IBS.Models.MSAP;

namespace IBS.Services.MSAP
{
    public static class SalesReportRecognition
    {
        public static string GetDeferredApLabel(DispatchTicket ticket)
        {
            return ticket.OtherTugCostPending ? "Awaiting AP Confirmation" : "Deferred to confirmation month";
        }

        public static bool IncludesService(DispatchTicket ticket, DateOnly from, DateOnly to)
        {
            var date = ticket.Tugboat.IsCompanyOwned ? ticket.Billing?.Date ?? ticket.Date : ticket.Date;
            return date >= from && date <= to;
        }

        public static bool IncludesAmounts(DispatchTicket ticket, DateOnly from, DateOnly to)
        {
            if (ticket.OtherTugCostPending)
            {
                return false;
            }

            var date = ticket.Billing?.Date ?? ticket.Date;
            if (!ticket.Tugboat.IsCompanyOwned)
            {
                // shortcut: legacy AP has no confirmation date; retain its ticket period until settlement dates are supplied.
                date = ticket.OtherTugChargeLine?.Charge.ConfirmedAt is { } confirmedAt
                    ? DateOnly.FromDateTime(confirmedAt) : ticket.Date;
                if (date < ticket.Date)
                {
                    date = ticket.Date;
                }
            }

            return date >= from && date <= to;
        }
    }
}
