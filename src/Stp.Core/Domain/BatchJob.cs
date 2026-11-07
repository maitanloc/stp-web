namespace Stp.Core.Domain;

public enum BatchJobType
{
    Convert = 1,
    Merge = 2,
    Sync = 3,
    ExportPdf = 4
}

public enum BatchJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Một job xử lý hàng loạt. Worker chạy tuần tự để tránh tranh chấp Excel COM.
/// </summary>
public sealed class BatchJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public BatchJobType Type { get; set; }

    public BatchJobStatus Status { get; set; } = BatchJobStatus.Queued;

    /// <summary>JSON tùy chọn: chế độ file gốc, preset in, có gộp hay không...</summary>
    public string? OptionsJson { get; set; }

    public int TotalItems { get; set; }

    public int SucceededItems { get; set; }

    public int FailedItems { get; set; }

    public string? OutputPath { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<BatchJobItem> Items { get; set; } = new();

    public List<JobLog> Logs { get; set; } = new();
}

public sealed class BatchJobItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }

    public BatchJob Job { get; set; } = null!;

    public string InputFileName { get; set; } = string.Empty;

    public string? InputPath { get; set; }

    public string? OutputPath { get; set; }

    public BatchJobStatus Status { get; set; } = BatchJobStatus.Queued;

    public WorkbookKind WorkbookKind { get; set; } = WorkbookKind.Unknown;

    public int DossierCount { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class JobLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }

    public BatchJob Job { get; set; } = null!;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string Level { get; set; } = "Information";

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }
}
