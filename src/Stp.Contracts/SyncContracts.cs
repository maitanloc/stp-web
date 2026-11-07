namespace Stp.Contracts;

public sealed record SyncResponse(
    bool Success,
    Guid SyncSessionId,
    Guid? WorkbookId,
    int AddedCount,
    int UpdatedCount,
    int DeletedCount,
    int ConflictCount,
    string? ErrorMessage);

public sealed record SyncSessionSummaryResponse(
    Guid Id,
    Guid? WorkbookId,
    string SourceFileName,
    DateTimeOffset UploadedAt,
    string Status,
    int AddedCount,
    int UpdatedCount,
    int DeletedCount,
    int ConflictCount,
    string? ErrorMessage);

public sealed record SyncChangeResponse(
    Guid Id,
    string EntityType,
    string EntityKey,
    string ChangeType,
    bool IsConflict,
    string? Resolution,
    string? OldValueJson,
    string? NewValueJson);

public sealed record SyncSessionDetailResponse(
    SyncSessionSummaryResponse Summary,
    IReadOnlyList<SyncChangeResponse> Changes);
