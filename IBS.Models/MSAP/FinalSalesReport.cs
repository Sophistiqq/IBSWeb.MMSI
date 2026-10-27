namespace IBS.Models.MSAP
{
    public class FinalSalesReport
    {
        public DateOnly DateFrom { get; set; }
        public DateOnly DateTo { get; set; }
        public DateTime GeneratedAt { get; set; }
        public byte[] Content { get; set; } = [];
    }
}
