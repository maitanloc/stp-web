namespace Stp.Core.Summary;

/// <summary>Kiểu đóng gói bản in: mỗi năm một file .xlsm (zip) hoặc gộp mọi năm vào một .xlsm.</summary>
public enum SummaryPrintOutputMode
{
    PerYear = 0,
    Combined = 1
}

/// <summary>
/// Một dòng đã hoàn thiện để đưa vào sheet ML trung gian của bản in.
/// Các giá trị đã bao gồm chỉnh sửa và chuẩn hoá của người dùng.
/// </summary>
public sealed record SummaryPrintRow(
    int Year,
    string NumberLabel,
    string DocumentNumber,
    string Summary,
    string Date,
    string DocumentCode,
    string SheetNumber,
    string Note,
    string OriginalSummary,
    bool IsAggregate = false);

/// <summary>Thống kê mức sẵn sàng in của một năm sau khi áp dụng chỉnh sửa.</summary>
public sealed record SummaryPrintYearStatus(
    int Year,
    int Total,
    int Ready,
    int MissingNumber,
    int MissingDocumentCode,
    int MissingDate,
    int MissingSheetNumber,
    int MissingSummary)
{
    public int MissingTotal =>
        MissingNumber + MissingDocumentCode + MissingDate + MissingSheetNumber + MissingSummary;

    public bool HasIssues => MissingTotal > 0;
}

public sealed record SummaryPrintPreview(
    IReadOnlyList<SummaryPrintYearStatus> Years,
    int TotalRows,
    bool HasIssues);

/// <summary>Tuỳ chọn xuất bản in do người dùng chọn.</summary>
public sealed record SummaryPrintOptions(
    IReadOnlyList<int>? Years = null,
    SummaryPrintOutputMode Mode = SummaryPrintOutputMode.PerYear,
    bool AllowIncomplete = false,
    string? OutputFileName = null);

/// <summary>Kết quả xuất bản in trả về API: một file .xlsm hoặc một zip nhiều .xlsm.</summary>
public sealed record SummaryPrintExportResult(
    byte[] Content,
    string FileName,
    string ContentType,
    int RowCount,
    int ChangedRows,
    int NormalizedRows,
    IReadOnlyList<int> Years,
    IReadOnlyList<string> Warnings);

/// <summary>Yêu cầu dựng bản in từ các dòng đã hoàn thiện.</summary>
public sealed record SummaryPrintBuildRequest(
    IReadOnlyList<SummaryPrintRow> Rows,
    IReadOnlyList<int> Years,
    SummaryPrintOutputMode Mode,
    string BaseFileName);

public sealed record SummaryPrintBuildResult(
    byte[] Content,
    string FileName,
    string ContentType,
    IReadOnlyList<int> Years,
    int RowCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Dựng workbook chỉ có ML từ các dòng đã hoàn thiện, sinh sheet in năm bằng
/// <c>Type2SheetGenerator</c> rồi chèn macro <c>StpArchiveMacro.bas</c>.
/// </summary>
public interface ISummaryPrintService
{
    Task<SummaryPrintBuildResult> BuildAsync(
        SummaryPrintBuildRequest request,
        CancellationToken cancellationToken = default);
}
