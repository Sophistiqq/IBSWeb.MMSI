namespace IBS.Models.MSAP.ViewModels
{
    public class RateReferenceViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public int? PortId { get; set; }
    }
}
