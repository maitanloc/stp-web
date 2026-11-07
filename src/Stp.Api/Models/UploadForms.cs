using Microsoft.AspNetCore.Http;

namespace Stp.Api.Models;

/// <summary>Form upload dùng chung cho job chuyển đổi/gộp.</summary>
public sealed class JobUploadForm
{
    public IFormFileCollection Files { get; set; } = new FormFileCollection();

    public int? OriginalFileMode { get; set; }

    public bool? CreateZip { get; set; }

    public bool? MergeOutput { get; set; }

    public bool? GeneratePrintSheets { get; set; }

    public bool? InjectMacro { get; set; }

    public string? HeaderText { get; set; }

    public string? FooterText { get; set; }
}

public sealed class SyncUploadForm
{
    public IFormFile? File { get; set; }

    public Guid? WorkbookId { get; set; }

    public int? BaseVersion { get; set; }
}
