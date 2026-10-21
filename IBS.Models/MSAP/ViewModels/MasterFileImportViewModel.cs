using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace IBS.Models.MSAP.ViewModels
{
    public class MasterFileImportViewModel
    {
        public List<IFormFile> Files { get; set; } = [];
        public bool UpdateExisting { get; set; }
        public string? NewTugboatPortCode { get; set; }
        public string? NonVatableTaxType { get; set; }
        public string? ReviewedFingerprint { get; set; }
        public string? EditedFilesJson { get; set; }
        public List<MasterFileImportDocument> Documents { get; set; } = [];
        public List<SelectListItem> Ports { get; set; } = [];
        public List<MasterFileImportItem> Items { get; set; } = [];
        public List<string> IgnoredFiles { get; set; } = [];
        public List<string> Errors { get; set; } = [];
        public bool Imported { get; set; }
        public List<Guid> RateDraftIds { get; set; } = [];
        public List<MasterFileImportAvailability> Availability { get; set; } = [];
    }

    public class MasterFileImportDocument
    {
        public string Kind { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public List<MasterFileImportColumn> Columns { get; set; } = [];
        public List<MasterFileImportRow> Rows { get; set; } = [];
    }

    public class MasterFileImportColumn
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string[] Choices { get; set; } = [];
        public bool Required { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public class MasterFileImportAvailability
    {
        public string File { get; set; } = string.Empty;
        public int SuppliedRows { get; set; }
        public int? StoredRows { get; set; }
    }

    public class MasterFileImportRow
    {
        public int Row { get; set; }
        public Dictionary<string, string> Fields { get; set; } = [];
    }

    public class MasterFileImportItem
    {
        public string FileName { get; set; } = string.Empty;
        public int Row { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Changes { get; set; } = string.Empty;
        public string? Warning { get; set; }
    }
}
