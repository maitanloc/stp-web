namespace Stp.Core.Excel;

/// <summary>Yêu cầu gộp nhiều workbook cùng cấu trúc ML.</summary>
public sealed class MergeRequest
{
    public IReadOnlyList<string> InputPaths { get; init; } = Array.Empty<string>();

    /// <summary>File kết quả .xlsx (chưa có macro); engine có thể chuyển tiếp sang .xlsm.</summary>
    public string OutputPath { get; init; } = string.Empty;

    public bool Overwrite { get; init; } = true;

    /// <summary>Sinh sheet in năm từ ML đã gộp.</summary>
    public bool GeneratePrintSheets { get; init; } = true;
}

public sealed class MergeOutcome
{
    public bool Success { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public int FileCount { get; init; }

    public int YearCount { get; init; }

    public int DossierCount { get; init; }

    /// <summary>Số hồ sơ trùng (cùng năm + nhãn số) giữa các file; vẫn giữ tất cả.</summary>
    public int DuplicateCount { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string? ErrorMessage { get; init; }
}

/// <summary>Gộp nhiều file ML thành một workbook (ML + sheet in năm + bảng nguồn).</summary>
public interface IMergedWorkbookService
{
    MergeOutcome Merge(MergeRequest request, CancellationToken cancellationToken = default);
}
