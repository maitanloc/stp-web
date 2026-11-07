namespace Stp.Core.Domain;

/// <summary>
/// Một hồ sơ = một trang in "MỤC LỤC VĂN BẢN".
/// </summary>
public sealed class Dossier
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkbookId { get; set; }

    public ArchiveWorkbook Workbook { get; set; } = null!;

    public Guid YearId { get; set; }

    public ArchiveYear Year { get; set; } = null!;

    /// <summary>Dòng tương ứng trong sheet ML.</summary>
    public int MlRow { get; set; }

    /// <summary>Số hồ sơ dạng số, dùng để lọc giống macro ("346/1" -> 346).</summary>
    public int Number { get; set; }

    /// <summary>Nhãn số hồ sơ đúng như file Excel, ví dụ "346/1".</summary>
    public string NumberLabel { get; set; } = string.Empty;

    public string Title { get; set; } = "MỤC LỤC VĂN BẢN";

    public string Group { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string BoxNumber { get; set; } = string.Empty;

    public string SubNumber { get; set; } = string.Empty;

    public string OriginalSummary { get; set; } = string.Empty;

    public string NumberNote { get; set; } = string.Empty;

    public string SourceVolume { get; set; } = string.Empty;

    public bool HasPrintLayout { get; set; }

    public bool IsAggregate { get; set; }

    public int TotalPages { get; set; }

    /// <summary>Toàn văn tài liệu, nhập trực tiếp trong Excel (sheet VAN_BAN).</summary>
    public string? FullText { get; set; }

    /// <summary>Ghi chú/trích yếu bổ sung ngoài dữ liệu gốc.</summary>
    public string? Note { get; set; }

    /// <summary>SQL Server rowversion để phát hiện xung đột đồng bộ.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<DocumentLine> Documents { get; set; } = new();
}
