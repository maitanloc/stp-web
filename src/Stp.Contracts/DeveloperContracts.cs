namespace Stp.Contracts;

public sealed record MacroVersionSummaryResponse(
    Guid Id,
    string Name,
    int VersionNumber,
    string CodeHash,
    string? ChangeNote,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    bool IsActive,
    Guid? ParentVersionId,
    int LineCount);

public sealed record MacroVersionDetailResponse(
    Guid Id,
    string Name,
    int VersionNumber,
    string CodeHash,
    string? ChangeNote,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    bool IsActive,
    Guid? ParentVersionId,
    int LineCount,
    string Code);

public sealed record MacroSaveVersionRequest(
    string? Code,
    string? ChangeNote,
    Guid? ParentVersionId,
    bool Activate = true);

public sealed record MacroValidationResponse(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record MacroDiffLineResponse(
    string Kind,
    int? OldLineNumber,
    int? NewLineNumber,
    string Text);

public sealed record MacroDiffResponse(
    Guid NewVersionId,
    Guid? OldVersionId,
    int Added,
    int Removed,
    int Unchanged,
    IReadOnlyList<MacroDiffLineResponse> Lines);

public sealed record SystemInfoResponse(
    string AppVersion,
    string Environment,
    DateTimeOffset ServerTime,
    string StorageRoot,
    string ExcelHost,
    string? ExcelPath,
    bool ExcelAvailable,
    bool DatabaseConnected,
    MacroVersionSummaryResponse? ActiveMacro,
    int WorkbookCount,
    int DossierCount,
    int JobCount,
    int SyncSessionCount);
