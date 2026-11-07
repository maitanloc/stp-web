namespace Stp.Contracts;

/// <summary>Thông tin workbook sau khi mở bằng công cụ bổ sung trích yếu.</summary>
public sealed record SummaryInspectResponse(
    Guid SessionId,
    string FileName,
    long FileSizeBytes,
    string SheetName,
    string SummaryColumn,
    string? SummaryHeader,
    int RowCount,
    int ExistingListCount,
    int BrokenSummaryCount,
    int FormulaSummaryCount,
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

/// <summary>Danh mục giấy tờ mong muốn của một dòng.</summary>
public sealed record SummaryChangeRequest(int RowNumber, IReadOnlyList<string> Items);

public sealed record SummaryPreviewRequest(IReadOnlyList<SummaryChangeRequest> Changes);

public sealed record SummaryPreviewRowResponse(int RowNumber, string Summary, bool Changed);

public sealed record SummaryExportRequest(IReadOnlyList<SummaryChangeRequest> Changes);
