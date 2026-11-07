namespace Stp.Core.Domain;

/// <summary>
/// Một file workbook nguồn đã import hoặc đã đồng bộ.
/// </summary>
public sealed class ArchiveWorkbook
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tên hiển thị, ví dụ "DK_KET_HON".</summary>
    public string Name { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>Đường dẫn tuyệt đối trên máy chủ; không trả ra API công khai.</summary>
    public string? SourcePath { get; set; }

    public string FileHash { get; set; } = string.Empty;

    public WorkbookKind Kind { get; set; } = WorkbookKind.Unknown;

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Phiên bản dữ liệu, tăng mỗi lần đồng bộ thành công.</summary>
    public int Version { get; set; } = 1;

    public bool IsMaster { get; set; } = true;

    public string? Notes { get; set; }

    public List<ArchiveYear> Years { get; set; } = new();
}
