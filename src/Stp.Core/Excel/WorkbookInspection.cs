using Stp.Core.Domain;

namespace Stp.Core.Excel;

/// <summary>
/// Thông tin nhận diện một workbook mà không cần mở Excel.
/// </summary>
public sealed class WorkbookInspection
{
    public string FilePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public WorkbookKind Kind { get; init; } = WorkbookKind.Unknown;

    public bool HasMlSheet { get; init; }

    public string? MlSheetName { get; init; }

    public bool HasPrintSheets { get; init; }

    /// <summary>Có <c>xl/vbaProject.bin</c> trong file hay không.</summary>
    public bool HasMacro { get; init; }

    public int SheetCount { get; init; }

    public int YearCount { get; init; }

    public int DossierCount { get; init; }

    public int PrintReadyCount { get; init; }

    public int TotalPages { get; init; }

    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();

    public IReadOnlyList<YearInspection> Years { get; init; } = Array.Empty<YearInspection>();

    public IReadOnlyList<SheetInspection> Sheets { get; init; } = Array.Empty<SheetInspection>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class YearInspection
{
    public int Year { get; init; }

    public string SheetName { get; init; } = string.Empty;

    public int DossierCount { get; init; }

    public int MinNumber { get; init; }

    public int MaxNumber { get; init; }

    public bool HasPrintSheet { get; init; }

    public IReadOnlyList<string> SourceSheets { get; init; } = Array.Empty<string>();
}

public sealed class SheetInspection
{
    public string Name { get; init; } = string.Empty;

    public bool IsMl { get; init; }

    public bool IsPrint { get; init; }

    public int? Year { get; init; }

    public int LastRow { get; init; }

    public int LastColumn { get; init; }

    public int BlockCount { get; init; }
}
