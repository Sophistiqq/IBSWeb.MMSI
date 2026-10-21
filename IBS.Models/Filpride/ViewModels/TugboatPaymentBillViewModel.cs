namespace IBS.Models.Filpride.ViewModels
{
    public class TugboatPaymentBillViewModel
    {
        public int ChargeId { get; set; }
        public string ProviderReference { get; set; } = "";
        public bool IncludesVat { get; set; }
        public decimal Gross { get; set; }
        public decimal Withholding { get; set; }
        public decimal Net => Gross - Withholding;
        public List<TugboatPaymentLine> Lines { get; set; } = [];
    }
}
