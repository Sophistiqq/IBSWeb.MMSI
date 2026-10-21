using System.Text.Json.Serialization;

namespace IBS.Models.Filpride.ViewModels
{
    public class TugboatPaymentLine
    {
        public int AllocationId { get; set; }
        public int TicketId { get; set; }
        public int TugboatId { get; set; }
        public int? CosId { get; set; }
        public string? CosNumber { get; set; }
        public string TicketNumber { get; set; } = "";
        public string TugboatName { get; set; } = "";
        public decimal Amount { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? Gross { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? Withholding { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? Net => Gross - Withholding;
    }
}
