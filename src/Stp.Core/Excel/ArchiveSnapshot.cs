using Stp.Core.Domain;

namespace Stp.Core.Excel;

/// <summary>
/// Dữ liệu đọc được từ một workbook: mỗi năm + danh sách hồ sơ theo đúng cấu trúc ML.
/// Dùng chung cho import, đồng bộ và kiểm tra.
/// </summary>
public sealed class ArchiveSnapshot
{
    public string SourceFileName { get; init; } = string.Empty;

    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;

    public WorkbookKind Kind { get; init; } = WorkbookKind.Unknown;

    public List<ArchiveYearData> Years { get; init; } = new();

    public int TotalDossiers => Years.Sum(y => y.Dossiers.Count);

    public int TotalPrintReady => Years.Sum(y => y.Dossiers.Count(d => d.HasPrintLayout));

    public int TotalPages => Years.Sum(y => y.Dossiers.Sum(d => d.TotalPages));

    public int TotalTypes => Years
        .SelectMany(y => y.Dossiers)
        .Select(d => d.Type)
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
}

public sealed class ArchiveYearData
{
    public int Year { get; init; }

    public string SheetName { get; init; } = string.Empty;

    public List<string> SourceSheets { get; init; } = new();

    public List<DossierRecord> Dossiers { get; init; } = new();
}

public sealed class DossierRecord
{
    public int MlRow { get; init; }

    public int Number { get; init; }

    public string NumberLabel { get; init; } = string.Empty;

    public string Title { get; init; } = "MỤC LỤC VĂN BẢN";

    public string Group { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string BoxNumber { get; init; } = string.Empty;

    public string SubNumber { get; init; } = string.Empty;

    public string OriginalSummary { get; init; } = string.Empty;

    public string NumberNote { get; init; } = string.Empty;

    public string SourceVolume { get; init; } = string.Empty;

    public bool HasPrintLayout { get; init; }

    public bool IsAggregate { get; init; }

    public int TotalPages { get; init; }

    public List<DocumentLine> Documents { get; init; } = new();
}
