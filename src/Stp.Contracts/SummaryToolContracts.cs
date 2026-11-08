namespace Stp.Contracts;

/// <summary>Thông tin workbook sau khi mở bằng công cụ bổ sung trích yếu.</summary>
public sealed record SummaryInspectResponse(
    Guid SessionId,
    string FileName,
    long FileSizeBytes,
    string SheetName,
    string SummaryColumn,
    string? SummaryHeader,
    string SheetNumberColumn,
    string? SheetNumberHeader,
    string DocumentNumberColumn,
    int RowCount,
    int ExistingListCount,
    int BrokenSummaryCount,
    int FormulaSummaryCount,
    int MissingSheetNumberCount,
    int MissingDocumentNumberCount,
    int MissingNumberCount,
    int NormalizableCount,
    IReadOnlyList<string> Catalog,
    IReadOnlyList<SummaryYearResponse> Years,
    IReadOnlyList<SummaryTypeResponse> Types);

public sealed record SummaryYearResponse(int Year, int Count);

public sealed record SummaryTypeResponse(string Name, int Count);

/// <summary>Một dòng hồ sơ kèm phần trích yếu đã tách khỏi danh mục giấy tờ.</summary>
public sealed record SummaryRowResponse(
    int RowNumber,
    int? Year,
    string NumberLabel,
    string DocumentNumber,
    string SheetNumber,
    string Summary,
    string BaseSummary,
    IReadOnlyList<string> Items,
    bool HasDocumentList,
    bool HasFormula,
    bool IsBrokenSummary,
    string? Group,
    string? Type);

public sealed record SummaryRowsResponse(
    IReadOnlyList<SummaryRowResponse> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Thay đổi mong muốn của một dòng. <see cref="Items"/> ghép lại danh mục giấy tờ;
/// <see cref="SummaryText"/> sửa toàn bộ câu trích yếu (ưu tiên hơn Items);
/// <see cref="SheetNumber"/> cập nhật cột số tờ (null = giữ nguyên, rỗng = xoá).
/// </summary>
public sealed record SummaryChangeRequest(
    int RowNumber,
    IReadOnlyList<string> Items,
    string? SummaryText = null,
    string? SheetNumber = null);

public sealed record SummaryPreviewRequest(IReadOnlyList<SummaryChangeRequest> Changes);

public sealed record SummaryPreviewRowResponse(
    int RowNumber,
    string Summary,
    bool Changed,
    string OriginalSummary,
    string? SheetNumber,
    string? OriginalSheetNumber,
    bool SummaryChanged,
    bool SheetNumberChanged);

public sealed record SummaryExportRequest(IReadOnlyList<SummaryChangeRequest> Changes);

/// <summary>Phạm vi lọc dùng cho danh sách và bản xuất lọc.</summary>
public sealed record SummaryScopeRequest(
    int? Year,
    string? Type,
    string? Search,
    bool OnlyExisting,
    string? NumberMode,
    int? NumberFrom,
    int? NumberTo,
    int? Number,
    int? YearFrom = null,
    int? YearTo = null,
    bool OnlyMissingSheetNumber = false,
    bool OnlyMissingDocumentNumber = false,
    bool OnlyMissingNumber = false,
    bool MissingSheetNumberFirst = false);

public sealed record SummaryFilteredExportRequest(
    IReadOnlyList<SummaryChangeRequest>? Changes,
    SummaryScopeRequest? Scope);

public sealed record SummaryFilteredPreviewResponse(
    int TotalRows,
    int ChangedRows,
    int IgnoredChanges,
    int NormalizedRows,
    bool Truncated,
    IReadOnlyList<SummaryPreviewRowResponse> Items);
