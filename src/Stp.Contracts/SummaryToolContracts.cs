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
    IReadOnlyList<SummaryTypeResponse> Types,
    string NumberColumn = "B",
    IReadOnlyList<SummaryCatalogMetaResponse>? CatalogMeta = null,
    IReadOnlyList<SummarySheetResponse>? Sheets = null);

public sealed record SummaryYearResponse(int Year, int Count);

public sealed record SummaryTypeResponse(string Name, int Count);

/// <summary>Một sheet dữ liệu tạo nên phiên (file nhập có Year = null).</summary>
public sealed record SummarySheetResponse(string Name, int? Year, int RowCount);

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
    string? Type,
    bool NeedsNormalization = false,
    string NormalizedSummary = "",
    string NormalizedBaseSummary = "",
    string Sheet = "",
    int SourceRowNumber = 0);

public sealed record SummaryRowsResponse(
    IReadOnlyList<SummaryRowResponse> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Thay đổi mong muốn của một dòng. <see cref="Items"/> ghép lại danh mục giấy tờ;
/// <see cref="SummaryText"/> sửa toàn bộ câu trích yếu (ưu tiên hơn Items);
/// <see cref="SheetNumber"/> cập nhật cột số tờ; <see cref="DocumentNumber"/> cập nhật
/// hồ sơ số; <see cref="NumberLabel"/> cập nhật số hồ sơ (null = giữ nguyên, rỗng = xoá).
/// </summary>
public sealed record SummaryChangeRequest(
    int RowNumber,
    IReadOnlyList<string> Items,
    string? SummaryText = null,
    string? SheetNumber = null,
    string? DocumentNumber = null,
    string? NumberLabel = null);

public sealed record SummaryPreviewRequest(
    IReadOnlyList<SummaryChangeRequest> Changes,
    IReadOnlyList<int>? OnlyRowNumbers = null);

public sealed record SummaryPreviewRowResponse(
    int RowNumber,
    string Summary,
    bool Changed,
    string OriginalSummary,
    string? SheetNumber,
    string? OriginalSheetNumber,
    bool SummaryChanged,
    bool SheetNumberChanged,
    string? DocumentNumber = null,
    string? OriginalDocumentNumber = null,
    bool DocumentNumberChanged = false,
    string? NumberLabel = null,
    string? OriginalNumberLabel = null,
    bool NumberLabelChanged = false,
    string Sheet = "",
    int SourceRowNumber = 0);

public sealed record SummaryExportRequest(
    IReadOnlyList<SummaryChangeRequest> Changes,
    string? OutputFileName = null,
    IReadOnlyList<int>? OnlyRowNumbers = null);

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
    bool MissingSheetNumberFirst = false,
    bool OnlyNeedsNormalization = false,
    bool SortByNumber = false);

public sealed record SummaryFilteredExportRequest(
    IReadOnlyList<SummaryChangeRequest>? Changes,
    SummaryScopeRequest? Scope,
    string? OutputFileName = null);

public sealed record SummaryYearReportResponse(
    int Year,
    int Total,
    int MissingSheetNumber,
    int MissingDocumentNumber,
    int MissingNumber,
    int NeedsNormalization);

public sealed record SummarySessionInfoResponse(
    Guid SessionId,
    string FileName,
    long FileSizeBytes,
    DateTimeOffset CreatedAt,
    int RowCount);

public sealed record SummaryFilteredPreviewResponse(
    int TotalRows,
    int ChangedRows,
    int IgnoredChanges,
    int NormalizedRows,
    bool Truncated,
    IReadOnlyList<SummaryPreviewRowResponse> Items);

/// <summary>Xem trước mức sẵn sàng in của các năm sau khi áp dụng chỉnh sửa.</summary>
public sealed record SummaryPrintPreviewRequest(
    IReadOnlyList<SummaryChangeRequest>? Changes,
    IReadOnlyList<int>? Years = null);

public sealed record SummaryPrintYearStatusResponse(
    int Year,
    int Total,
    int Ready,
    int MissingNumber,
    int MissingDocumentCode,
    int MissingDate,
    int MissingSheetNumber,
    int MissingSummary);

public sealed record SummaryPrintPreviewResponse(
    IReadOnlyList<SummaryPrintYearStatusResponse> Years,
    int TotalRows,
    bool HasIssues);

/// <summary>Xuất bản in theo năm; mode = perYear (zip) hoặc combined (một .xlsm).</summary>
public sealed record SummaryPrintExportRequest(
    IReadOnlyList<SummaryChangeRequest>? Changes,
    IReadOnlyList<int>? Years = null,
    string? Mode = null,
    bool AllowIncomplete = false,
    string? OutputFileName = null);

/// <summary>Meta một mục trong danh mục phiên: tần suất + nguồn ("user"/"learned"/"file"/"default").</summary>
public sealed record SummaryCatalogMetaResponse(string Name, int UsageCount, string Source);

/// <summary>Mục trong danh mục học được (database).</summary>
public sealed record SummaryCatalogItemResponse(
    Guid Id,
    string DisplayName,
    int UsageCount,
    string Source,
    DateTimeOffset LastUsedAt,
    string? DossierTypeHint);

public sealed record SummaryCatalogListResponse(IReadOnlyList<SummaryCatalogItemResponse> Items);

public sealed record SummaryCatalogUpsertRequest(
    string DisplayName,
    string? DossierTypeHint = null);

public sealed record SummaryCatalogPatchRequest(
    string? DisplayName = null,
    bool? IsDisabled = null);

public sealed record SummaryCatalogSeedRequest(
    int MinCount = 50,
    int? MaxCount = null);

public sealed record SummaryCatalogSeedResponse(int AddedCount, int UpdatedCount, int MinCount);

/// <summary>Mục đề xuất nạp từ Kho (dùng cho band review 2–49 của dev).</summary>
public sealed record SummaryCatalogSuggestionResponse(
    string DisplayName,
    int Count);

public sealed record SummaryCatalogSuggestionsResponse(
    IReadOnlyList<SummaryCatalogSuggestionResponse> Items,
    int MinCount,
    int MaxCount);

/// <summary>
/// Lưu bản nháp lên server (P0.2 data safety): payload là JSON nguyên trạng
/// phía client gửi — server không cần biết cấu trúc, chỉ trữ và trả lại.
/// </summary>
public sealed record SummaryDraftSaveRequest(
    string FileName,
    System.Text.Json.JsonElement Payload);

/// <summary>Bản nháp đã lưu trên server cho (phiên + người dùng).</summary>
public sealed record SummaryDraftResponse(
    string FileName,
    DateTimeOffset SavedAt,
    System.Text.Json.JsonElement Payload);

/// <summary>Thông tin phiên làm việc chung của cả phòng (Team Active Session).</summary>
public sealed record SummaryActiveSessionResponse(
    bool HasActiveSession,
    Guid? SessionId,
    string? FileName,
    string? UploadedBy,
    DateTimeOffset? UploadedAt,
    int RowCount,
    int EditCount);

/// <summary>Yêu cầu thiết lập phiên làm việc chung của phòng.</summary>
public sealed record SummarySetActiveSessionRequest(
    Guid SessionId);

/// <summary>Kết quả nạp trực tiếp phiên trích yếu vào Kho hồ sơ (SQL Server).</summary>
public sealed record SummaryDirectSyncResponse(
    bool Success,
    int AddedCount,
    int UpdatedCount,
    int ConflictCount,
    string? Message);

public sealed record SummaryCatalogPresetResponse(
    Guid Id,
    string Name,
    string? DossierType,
    IReadOnlyList<string> Items,
    bool IsDefault,
    int Order,
    DateTimeOffset CreatedAt);

public sealed record SummaryCatalogPresetUpsertRequest(
    Guid? Id,
    string Name,
    string? DossierType,
    IReadOnlyList<string> Items,
    bool IsDefault = false,
    int? Order = null);
