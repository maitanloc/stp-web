namespace Stp.Contracts;

/// <summary>Kết quả nhận diện workbook, dùng chung cho trang kiểm tra và job.</summary>
public sealed record InspectionResponse(
    string FileName,
    long FileSizeBytes,
    string Kind,
    bool HasMlSheet,
    string? MlSheetName,
    bool HasPrintSheets,
    bool HasMacro,
    int SheetCount,
    int YearCount,
    int DossierCount,
    int PrintReadyCount,
    int TotalPages,
    IReadOnlyList<string> Types,
    IReadOnlyList<YearInspectionResponse> Years,
    IReadOnlyList<SheetInspectionResponse> Sheets,
    IReadOnlyList<string> Warnings);

public sealed record YearInspectionResponse(
    int Year,
    string SheetName,
    int DossierCount,
    int MinNumber,
    int MaxNumber,
    bool HasPrintSheet,
    IReadOnlyList<string> SourceSheets);

public sealed record SheetInspectionResponse(
    string Name,
    bool IsMl,
    bool IsPrint,
    int? Year,
    int LastRow,
    int LastColumn,
    int BlockCount);

/// <summary>Số liệu tổng hợp của một workbook đã import/đồng bộ.</summary>
public sealed record WorkbookResponse(
    Guid Id,
    string Name,
    string SourceFileName,
    string Kind,
    DateTimeOffset ImportedAt,
    DateTimeOffset? LastSyncedAt,
    int Version,
    bool IsMaster,
    string? Notes,
    int YearCount,
    int DossierCount);

public sealed record YearResponse(
    Guid Id,
    int Year,
    string SheetName,
    int DossierCount);

/// <summary>Dòng danh sách hồ sơ; không kèm toàn bộ văn bản để nhẹ payload.</summary>
public sealed record DossierSummaryResponse(
    Guid Id,
    int Year,
    int MlRow,
    int Number,
    string NumberLabel,
    string Type,
    string Group,
    string BoxNumber,
    string OriginalSummary,
    bool HasPrintLayout,
    bool IsAggregate,
    int TotalPages,
    int DocumentCount);

public sealed record DossierResponse(
    Guid Id,
    Guid WorkbookId,
    int Year,
    int MlRow,
    int Number,
    string NumberLabel,
    string Title,
    string Group,
    string Type,
    string BoxNumber,
    string SubNumber,
    string OriginalSummary,
    string NumberNote,
    string SourceVolume,
    bool HasPrintLayout,
    bool IsAggregate,
    int TotalPages,
    string? FullText,
    string? Note,
    string RowVersion,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DocumentLineResponse> Documents);

public sealed record DocumentLineResponse(
    int Stt,
    string SoKyHieu,
    string Ngay,
    string TrichYeu,
    string TacGia,
    string ToSo,
    string GhiChu);

/// <summary>
/// Kết quả tìm nhanh hồ sơ cho thanh lệnh Ctrl+K (H2): số hồ sơ + năm +
/// trích yếu gốc để hiện ngay trong palette, mở chi tiết bằng Id.
/// </summary>
public sealed record DossierSearchHitResponse(
    Guid Id,
    int Year,
    string NumberLabel,
    string Type,
    string OriginalSummary);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize);
