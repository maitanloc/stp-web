namespace Stp.Core.Domain;

/// <summary>
/// Một năm lưu trữ trong workbook. Các sheet bị chia nhỏ như "1999 (1)", "1999 (2)"
/// được gom chung một năm; <see cref="SheetNames"/> giữ tên các sổ in gốc.
/// </summary>
public sealed class ArchiveYear
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkbookId { get; set; }

    public ArchiveWorkbook Workbook { get; set; } = null!;

    public int Year { get; set; }

    /// <summary>Tên sheet đại diện, ví dụ "1990".</summary>
    public string SheetName { get; set; } = string.Empty;

    /// <summary>Danh sách sheet in gốc của năm, phân tách bằng dấu phẩy.</summary>
    public string? SourceSheets { get; set; }

    public int SortOrder { get; set; }

    public List<Dossier> Dossiers { get; set; } = new();
}
