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
