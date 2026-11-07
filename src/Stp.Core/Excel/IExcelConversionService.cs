namespace Stp.Core.Excel;

/// <summary>Yêu cầu chuyển một workbook sang .xlsm có macro STP.</summary>
public sealed class ConversionRequest
{
    public string InputPath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public string MacroCode { get; init; } = string.Empty;

    /// <summary>Chạy thử macro sau khi chèn để phát hiện lỗi biên dịch.</summary>
    public bool VerifyMacro { get; init; } = true;

    /// <summary>Xóa code cũ trong ThisWorkbook trước khi chèn macro mới.</summary>
    public bool ReplaceThisWorkbookCode { get; init; } = true;

    /// <summary>
    /// Có chèn macro VBA hay không. WPS không có VBA runtime nên engine sẽ bỏ qua
    /// và trả cảnh báo thay vì làm hỏng file.
    /// </summary>
    public bool InjectMacro { get; init; } = true;

    /// <summary>
    /// Tự sinh sheet in năm bằng ClosedXML khi file chỉ có ML (loại 2).
    /// File loại 1 đã có sheet in sẽ được giữ nguyên.
    /// </summary>
    public bool GeneratePrintSheets { get; init; } = true;

    /// <summary>Thư mục làm việc cho file tạm; mặc định cạnh file kết quả.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Tạo bản sao yêu cầu với đường dẫn đầu vào khác (dùng cho file tạm).</summary>
    public ConversionRequest WithInputPath(string inputPath)
    {
        return new ConversionRequest
        {
            InputPath = inputPath,
            OutputPath = OutputPath,
            MacroCode = MacroCode,
            VerifyMacro = VerifyMacro,
            ReplaceThisWorkbookCode = ReplaceThisWorkbookCode,
            InjectMacro = InjectMacro,
            GeneratePrintSheets = GeneratePrintSheets,
            WorkingDirectory = WorkingDirectory
        };
    }
}

public sealed class ConversionOutcome
{
    public bool Success { get; init; }

    public string InputPath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public int MacroLines { get; init; }

    public bool MacroVerified { get; init; }

    public TimeSpan Duration { get; init; }

    public string? ErrorMessage { get; init; }

    public string? Warning { get; init; }

    /// <summary>Các năm đã được sinh sheet in trong lần chuyển này.</summary>
    public IReadOnlyList<int> GeneratedPrintYears { get; init; } = Array.Empty<int>();

    public int PrintSheetsGenerated => GeneratedPrintYears.Count;
}

/// <summary>
/// Chuyển workbook bằng Microsoft Excel COM. Chỉ dùng được trên Windows có Excel.
/// </summary>
public interface IExcelConversionService
{
    bool IsAvailable { get; }

    string? ExcelPath { get; }

    Task<ConversionOutcome> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default);
}
