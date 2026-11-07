namespace Stp.Contracts;

/// <summary>Tùy chọn job trả về cho SPA; khớp với ConversionJobOptions.</summary>
public sealed record ConversionOptionsResponse(
    string OriginalFileMode,
    bool CreateZip,
    bool MergeOutput,
    bool MergeOnly,
    bool GeneratePrintSheets,
    bool InjectMacro,
    string? HeaderText,
    string? FooterText);

public sealed record JobSummaryResponse(
    Guid Id,
    string Type,
    string Status,
    int TotalItems,
    int SucceededItems,
    int FailedItems,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? OutputFileName,
    bool CanDownload);

public sealed record JobItemResponse(
    Guid Id,
    string InputFileName,
    string Status,
    string WorkbookKind,
    int DossierCount,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool CanDownload);

public sealed record JobLogResponse(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? Details);

public sealed record JobDetailResponse(
    JobSummaryResponse Summary,
    ConversionOptionsResponse Options,
    IReadOnlyList<JobItemResponse> Items,
    IReadOnlyList<JobLogResponse> Logs);

public sealed record JobCreatedResponse(
    Guid Id,
    string Status,
    string Message);
