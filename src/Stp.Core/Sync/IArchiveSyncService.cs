namespace Stp.Core.Sync;

/// <summary>Một lần đẩy workbook từ Excel lên web (file là nguồn dữ liệu chính).</summary>
public sealed class SyncRequest
{
    public string FilePath { get; init; } = string.Empty;

    public string SourceFileName { get; init; } = string.Empty;

    public string? FileHash { get; init; }

    public Guid? WorkbookId { get; init; }

    /// <summary>Version client đã đọc; nếu lệch sẽ ghi conflict và áp dụng last-write-wins.</summary>
    public int? BaseVersion { get; init; }
}

public sealed class SyncOutcome
{
    public bool Success { get; init; }

    public Guid SyncSessionId { get; init; }

    public Guid? WorkbookId { get; init; }

    public int AddedCount { get; init; }

    public int UpdatedCount { get; init; }

    public int DeletedCount { get; init; }

    public int ConflictCount { get; init; }

    public string? ErrorMessage { get; init; }
}

public interface IArchiveSyncService
{
    Task<SyncOutcome> ApplyAsync(SyncRequest request, CancellationToken cancellationToken = default);
}
