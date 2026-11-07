namespace Stp.Contracts;

public sealed record OverviewResponse(
    int WorkbookCount,
    int DossierCount,
    int YearCount,
    int TypeCount,
    int JobCount,
    int RunningJobCount,
    int SyncSessionCount,
    DateTimeOffset? LastSyncAt,
    string ExcelHost,
    string? ExcelPath,
    bool ExcelAvailable,
    bool DatabaseConnected,
    DateTimeOffset ServerTime,
    IReadOnlyList<JobSummaryResponse> RecentJobs,
    IReadOnlyList<WorkbookResponse> RecentWorkbooks);
