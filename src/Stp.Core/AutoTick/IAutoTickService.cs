using Stp.Contracts;

namespace Stp.Core.AutoTick;

public interface IAutoTickService
{
    Task<AutoTickInspectResponse> InspectAsync(AutoTickInspectRequest request, CancellationToken ct = default);

    Task<Guid> StartJobAsync(AutoTickStartRequest request, CancellationToken ct = default);

    Task<AutoTickJobStatusResponse?> GetJobStatusAsync(Guid jobId, CancellationToken ct = default);

    Task<bool> CancelJobAsync(Guid jobId, CancellationToken ct = default);

    Task<(Stream Stream, string FileName)?> GetResultFileAsync(Guid jobId, CancellationToken ct = default);

    Task<AutoTickSaveToOneDriveResponse> SaveToLocalOneDriveAsync(Guid jobId, CancellationToken ct = default);

    Task<AutoTickWatcherCheckResponse> CheckWatcherAsync(AutoTickWatcherCheckRequest request, CancellationToken ct = default);

    Task<AutoTickSyncSummaryResponse> SyncSummaryRowsToOneDriveAsync(AutoTickSyncSummaryRequest request, CancellationToken ct = default);
}

