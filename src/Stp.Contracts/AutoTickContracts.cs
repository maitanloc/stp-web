namespace Stp.Contracts;

public sealed class AutoTickInspectRequest
{
    public string OneDriveUrl { get; set; } = string.Empty;
    public string? SheetName { get; set; }
}

public sealed class AutoTickSheetHeader
{
    public int ColumnIndex { get; set; }
    public string ColumnLetter { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int ExistingTicksCount { get; set; }
}

public sealed class AutoTickRowPreview
{
    public int RowIndex { get; set; }
    public string? NumberLabel { get; set; }
    public string? CitizenNames { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public bool HasParentheses { get; set; }
    public IReadOnlyList<string> ExtractedItems { get; set; } = Array.Empty<string>();
    public int TickedCount { get; set; }
    public bool IsFullyTicked { get; set; }
}

public sealed class AutoTickInspectResponse
{
    public string FileName { get; set; } = string.Empty;
    public string OneDriveUrl { get; set; } = string.Empty;
    public bool UserCanWrite { get; set; }
    public string? ETag { get; set; }
    public IReadOnlyList<string> Sheets { get; set; } = Array.Empty<string>();
    public string ActiveSheet { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int RowsWithParentheses { get; set; }
    public int RowsFullyTicked { get; set; }
    public int RowsPartiallyTicked { get; set; }
    public int RowsUnticked { get; set; }
    public IReadOnlyList<AutoTickSheetHeader> DocumentHeaders { get; set; } = Array.Empty<AutoTickSheetHeader>();
    public IReadOnlyList<AutoTickRowPreview> SampleRows { get; set; } = Array.Empty<AutoTickRowPreview>();
}

public sealed class AutoTickStartRequest
{
    public string OneDriveUrl { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public int? NumberFrom { get; set; }
    public int? NumberTo { get; set; }
    public IReadOnlyList<string>? SelectedHeaders { get; set; }
    public bool OnlyUnticked { get; set; }
}

public sealed class AutoTickRowResult
{
    public int RowIndex { get; set; }
    public string? NumberLabel { get; set; }
    public string? CitizenNames { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public IReadOnlyList<string> ExtractedItems { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> NewlyTickedColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> AlreadyTickedColumns { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> UnmatchedItems { get; set; } = Array.Empty<string>();
    public string Status { get; set; } = string.Empty; // "Ticked", "SkippedFullyTicked", "SkippedNoParentheses", "SkippedFilter"
}

public sealed class AutoTickProgressEvent
{
    public Guid JobId { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public int ProcessedRows { get; set; }
    public int TotalRows { get; set; }
    public int NewlyTickedRowsCount { get; set; }
    public int NewlyTickedCellsCount { get; set; }
    public int SkippedRowsCount { get; set; }
    public int Percent { get; set; }
    public AutoTickRowResult? CurrentRow { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class AutoTickJobStatusResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty; // "Running", "Completed", "Failed", "Cancelled"
    public string SheetName { get; set; } = string.Empty;
    public int ProcessedRows { get; set; }
    public int TotalRows { get; set; }
    public int NewlyTickedRowsCount { get; set; }
    public int NewlyTickedCellsCount { get; set; }
    public int SkippedRowsCount { get; set; }
    public int Percent { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<AutoTickRowResult> RecentRowResults { get; set; } = Array.Empty<AutoTickRowResult>();
}

public sealed class AutoTickWatcherCheckRequest
{
    public string OneDriveUrl { get; set; } = string.Empty;
    public string? SheetName { get; set; }
    public string? LastETag { get; set; }
    public int? KnownRowCount { get; set; }
}

public sealed class AutoTickWatcherCheckResponse
{
    public bool HasChanges { get; set; }
    public string? LatestETag { get; set; }
    public int NewUntickedRowsCount { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AutoTickSaveToOneDriveResponse
{
    public bool Success { get; set; }
    public string SavedPath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class AutoTickSyncSummaryItem
{
    public int RowIndex { get; set; }
    public string? SheetName { get; set; }
    public string NumberLabel { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public IReadOnlyList<string> Items { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string>? TargetCells { get; set; }
    public string? FormulaH { get; set; }
}

public sealed class AutoTickSyncSummaryRequest
{
    public string OneDriveUrl { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public IReadOnlyList<AutoTickSyncSummaryItem> Rows { get; set; } = Array.Empty<AutoTickSyncSummaryItem>();
    public bool EnsureFormulaH { get; set; } = true;
}

public sealed class AutoTickSyncSummaryRowResult
{
    public int RowIndex { get; set; }
    public string NumberLabel { get; set; } = string.Empty;
    public bool SummaryFilled { get; set; }
    public bool FormulaHFilled { get; set; }
    public IReadOnlyList<string> TickedColumns { get; set; } = Array.Empty<string>();
    public string? Error { get; set; }
}

public sealed class AutoTickSyncSummaryResponse
{
    public bool Success { get; set; }
    public bool IsQueued { get; set; }
    public int SyncedRowsCount { get; set; }
    public int TickedCellsCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<AutoTickSyncSummaryRowResult> Details { get; set; } = Array.Empty<AutoTickSyncSummaryRowResult>();
}


