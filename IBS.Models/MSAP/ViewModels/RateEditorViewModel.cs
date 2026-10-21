using IBS.Models.MSAP.Enums;

namespace IBS.Models.MSAP.ViewModels
{
    public class RateEditorViewModel
    {
        public Guid? Id { get; set; }
        public RateTableKind Kind { get; set; } = RateTableKind.Tariff;
        public DateOnly AsOfDate { get; set; }
        public List<RateUploadRowViewModel> Rows { get; set; } = [];
        public List<RateReferenceViewModel> Customers { get; set; } = [];
        public List<RateReferenceViewModel> Ports { get; set; } = [];
        public List<RateReferenceViewModel> Terminals { get; set; } = [];
        public List<RateReferenceViewModel> Services { get; set; } = [];
    }
}
