namespace Stp.Core.Domain;

public enum SyncStatus
{
    Pending = 0,
    Applied = 1,
    Failed = 2,
    Conflict = 3
}

/// <summary>
/// Một lần Excel đẩy thay đổi về web (qua nút Đồng bộ hoặc upload file).
/// </summary>
public sealed class SyncSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? WorkbookId { get; set; }

    public ArchiveWorkbook? Workbook { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public SyncStatus Status { get; set; } = SyncStatus.Pending;

    public int AddedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int DeletedCount { get; set; }

    public int ConflictCount { get; set; }

    public string? ErrorMessage { get; set; }

    public List<SyncChange> Changes { get; set; } = new();
}

public sealed class SyncChange
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SyncSessionId { get; set; }

    public SyncSession SyncSession { get; set; } = null!;

    public string EntityType { get; set; } = "Dossier";

    public string EntityKey { get; set; } = string.Empty;

    public string ChangeType { get; set; } = "Updated";

    public string? OldValueJson { get; set; }

    public string? NewValueJson { get; set; }

    public bool IsConflict { get; set; }

    public string? Resolution { get; set; }
}
