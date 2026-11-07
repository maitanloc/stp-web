namespace Stp.Core.Excel;

/// <summary>Yêu cầu sinh sheet in cho workbook chỉ có ML (loại 2).</summary>
public sealed class PrintGenerationRequest
{
    public string InputPath { get; init; } = string.Empty;

    /// <summary>
    /// Đường dẫn file kết quả. Để trống thì tạo file tạm cạnh file nguồn.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>Cho phép ghi đè file kết quả đã tồn tại.</summary>
    public bool Overwrite { get; init; } = true;

    /// <summary>Sinh cả khi file đã có một phần sheet in (chỉ sinh các năm còn thiếu).</summary>
    public bool GenerateMissingYearsOnly { get; init; } = true;
}

public sealed class PrintGenerationOutcome
{
    public bool Success { get; init; }

    public bool Generated { get; init; }

    public string InputPath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public IReadOnlyList<int> GeneratedYears { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> SkippedYears { get; init; } = Array.Empty<int>();

    public int GeneratedSheetCount => GeneratedYears.Count;

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Sinh sheet in năm từ sheet ML bằng ClosedXML, không cần cài Excel.
/// </summary>
public interface IPrintSheetGenerator
{
    PrintGenerationOutcome Generate(PrintGenerationRequest request, CancellationToken cancellationToken = default);
}
