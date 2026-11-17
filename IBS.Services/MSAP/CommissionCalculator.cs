using IBS.Models.MSAP;
using IBS.Utility.MSAP.Constants;
using static IBS.Utility.MSAP.Constants.TaxConstants;

namespace IBS.Services.MSAP
{
    public static class CommissionCalculator
    {
        public const string CategoryAgencyIncentive = MsapConstants.RepresentativeCategory.AgencyIncentive;
        public const string CategoryAgentCommission = MsapConstants.RepresentativeCategory.AgentCommission;

        public const string ModePercentage = "Percentage";
        public const string ModeFlat = "Flat";

        public const string BaseNetOfDiscount = "NetOfDiscount";
        public const string BaseNetOfVat = "NetOfVat";
        public const string BaseGross = "Gross";
        public const string BaseBasicOnly = "BasicOnly";
        public const string BaseBafOnly = "BafOnly";

        public static decimal ComputeAmount(
            string? mode,
            string? basis,
            decimal rate,
            decimal basicGross,
            decimal bafGross,
            decimal netOfDiscount,
            bool isVatable)
        {
            if (rate <= 0m)
            {
                return 0m;
            }

            if (string.Equals(mode, ModeFlat, StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(rate, 2, MidpointRounding.AwayFromZero);
            }

            var baseAmount = basis switch
            {
                BaseGross => basicGross + bafGross,
                BaseNetOfVat => isVatable
                    ? Math.Round(netOfDiscount / VatMultiplier, 2, MidpointRounding.AwayFromZero)
                    : netOfDiscount,
                BaseBasicOnly => basicGross,
                BaseBafOnly => bafGross,
                _ => netOfDiscount
            };

            if (baseAmount <= 0m)
            {
                return 0m;
            }

            return Math.Round(baseAmount * (rate / 100m), 2, MidpointRounding.AwayFromZero);
        }

        public static void AllocateAcrossTickets(
            IReadOnlyList<DispatchTicket> tickets,
            decimal totalAgencyIncentive,
            decimal totalAgentCommission)
        {
            var incentives = AllocateSingle(tickets, totalAgencyIncentive);
            var commissions = AllocateSingle(tickets, totalAgentCommission);
            for (var i = 0; i < tickets.Count; i++)
            {
                tickets[i].AgencyIncentiveAmount = incentives[i];
                tickets[i].AgentCommissionAmount = commissions[i];
            }
        }

        private static decimal[] AllocateSingle(IReadOnlyList<DispatchTicket> tickets, decimal total)
        {
            var result = new decimal[tickets.Count];
            if (tickets.Count == 0 || total <= 0m)
            {
                return result;
            }

            var weights = tickets.Select(t => Math.Max(0m, t.DispatchNetRevenue + t.BAFNetRevenue)).ToArray();
            var weightSum = weights.Sum();
            if (weightSum <= 0m)
            {
                weights = Enumerable.Repeat(1m, tickets.Count).ToArray();
                weightSum = tickets.Count;
            }

            var allocated = 0m;
            for (var i = 0; i < tickets.Count; i++)
            {
                if (i == tickets.Count - 1)
                {
                    result[i] = Math.Round(total - allocated, 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    var share = Math.Round(total * (weights[i] / weightSum), 2, MidpointRounding.AwayFromZero);
                    result[i] = share;
                    allocated += share;
                }
            }

            return result;
        }
    }
}
