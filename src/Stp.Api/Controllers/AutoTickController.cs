using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Stp.Api.Hubs;
using Stp.Api.Services;
using Stp.Contracts;
using Stp.Core.AutoTick;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/auto-tick")]
public sealed class AutoTickController : ControllerBase
{
    private readonly IAutoTickService _autoTick;
    private readonly NotificationService? _notificationService;
    private readonly IHubContext<TeamHub>? _hubContext;
    private readonly ILogger<AutoTickController> _logger;

    public AutoTickController(IAutoTickService autoTick, ILogger<AutoTickController> logger)
        : this(autoTick, null, null, logger)
    {
    }

    [ActivatorUtilitiesConstructor]
    public AutoTickController(
        IAutoTickService autoTick,
        NotificationService? notificationService,
        IHubContext<TeamHub>? hubContext,
        ILogger<AutoTickController> logger)
    {
        _autoTick = autoTick;
        _notificationService = notificationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpPost("inspect")]
    public async Task<ActionResult<AutoTickInspectResponse>> Inspect(
        [FromBody] AutoTickInspectRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OneDriveUrl))
        {
            return BadRequest("Vui lòng cung cấp liên kết OneDrive.");
        }

        try
        {
            var result = await _autoTick.InspectAsync(request, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi quét liên kết OneDrive {Url}", request.OneDriveUrl);
            return StatusCode(500, $"Lỗi quét file từ OneDrive: {ex.Message}");
        }
    }

    [HttpPost("start")]
    public async Task<ActionResult<Guid>> Start(
        [FromBody] AutoTickStartRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OneDriveUrl))
        {
            return BadRequest("Vui lòng cung cấp liên kết OneDrive.");
        }

        try
        {
            var jobId = await _autoTick.StartJobAsync(request, ct).ConfigureAwait(false);
            return Ok(jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi bắt đầu AutoTick");
            return StatusCode(500, $"Lỗi bắt đầu job: {ex.Message}");
        }
    }

    [HttpGet("{jobId:guid}/status")]
    public async Task<ActionResult<AutoTickJobStatusResponse>> GetStatus(
        Guid jobId,
        CancellationToken ct)
    {
        var status = await _autoTick.GetJobStatusAsync(jobId, ct).ConfigureAwait(false);
        if (status is null)
        {
            return NotFound($"Không tìm thấy job {jobId}");
        }

        return Ok(status);
    }

    [HttpPost("{jobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid jobId, CancellationToken ct)
    {
        var success = await _autoTick.CancelJobAsync(jobId, ct).ConfigureAwait(false);
        return success ? Ok() : NotFound();
    }

    [HttpGet("{jobId:guid}/download")]
    public async Task<IActionResult> Download(Guid jobId, CancellationToken ct)
    {
        var result = await _autoTick.GetResultFileAsync(jobId, ct).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound("Chưa có file kết quả cho job này.");
        }

        return File(
            result.Value.Stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.Value.FileName);
    }

    [HttpPost("{jobId:guid}/save-to-onedrive")]
    public async Task<ActionResult<AutoTickSaveToOneDriveResponse>> SaveToOneDrive(Guid jobId, CancellationToken ct)
    {
        var result = await _autoTick.SaveToLocalOneDriveAsync(jobId, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("watcher/check")]
    public async Task<ActionResult<AutoTickWatcherCheckResponse>> CheckWatcher(
        [FromBody] AutoTickWatcherCheckRequest request,
        CancellationToken ct)
    {
        var result = await _autoTick.CheckWatcherAsync(request, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("sync-summary")]
    public async Task<ActionResult<AutoTickSyncSummaryResponse>> SyncSummary(
        [FromBody] AutoTickSyncSummaryRequest request,
        [FromQuery] bool background = true,
        CancellationToken ct = default)
    {
        if (request.Rows.Count == 0)
        {
            return BadRequest(new AutoTickSyncSummaryResponse
            {
                Success = false,
                Message = "Không có dòng nào để đồng bộ."
            });
        }

        bool isQueued = _autoTick.EnqueueOneDriveJob(request.OneDriveUrl);

        if (!background)
        {
            try
            {
                var syncResult = await _autoTick.SyncSummaryRowsToOneDriveAsync(request, null, ct).ConfigureAwait(false);
                syncResult.IsQueued = isQueued;
                return Ok(syncResult);
            }
            finally
            {
                _autoTick.DequeueOneDriveJob(request.OneDriveUrl);
            }
        }

        bool isGSheet = request.OneDriveUrl?.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase) == true;
        string platformName = isGSheet ? "Google Sheets" : "OneDrive";

        // Chạy ngầm hoàn toàn trên server: Người dùng có thể đóng tab hoặc chuyển trang mà robot vẫn tiếp tục chạy đến khi hoàn tất
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _autoTick.SyncSummaryRowsToOneDriveAsync(
                    request,
                    onLockAcquired: async () =>
                    {
                        if (_hubContext is not null)
                        {
                            await _hubContext.Clients.All.SendAsync("oneDriveSyncQueueStarted", new
                            {
                                sheetName = request.SheetName,
                                rowCount = request.Rows.Count,
                                message = $"Robot đã rời hàng chờ và bắt đầu điền {request.Rows.Count} hồ sơ lên {platformName}!"
                            });
                        }
                    },
                    CancellationToken.None).ConfigureAwait(false);

                if (result.Success && result.SyncedRowsCount > 0)
                {
                    string notifBody = (request.SkipTicking || isGSheet)
                        ? $"Đã điền trích yếu cho {result.SyncedRowsCount} hồ sơ trên sheet {request.SheetName}."
                        : $"Đã điền trích yếu và cập nhật tick cho {result.SyncedRowsCount} hồ sơ trên sheet {request.SheetName} ({result.TickedCellsCount} ô thay đổi).";

                    _notificationService?.NotifyAll(
                        "system",
                        $"Đồng bộ {platformName} thành công",
                        notifBody,
                        "/summary-tool");

                    if (_hubContext is not null)
                    {
                        _ = _hubContext.Clients.All.SendAsync("oneDriveSyncCompleted", new
                        {
                            success = true,
                            sheetName = request.SheetName,
                            syncedRowsCount = result.SyncedRowsCount,
                            tickedCellsCount = result.TickedCellsCount,
                            message = result.Message
                        });
                    }
                }
                else
                {
                    string errTitle = result.Success ? $"Đồng bộ {platformName} (0 hồ sơ)" : $"Đồng bộ {platformName} chưa hoàn tất";
                    string errMsg = result.Message ?? "Không có hồ sơ nào được cập nhật.";

                    _notificationService?.NotifyAll(
                        "system",
                        errTitle,
                        errMsg,
                        "/summary-tool");

                    if (_hubContext is not null)
                    {
                        _ = _hubContext.Clients.All.SendAsync("oneDriveSyncCompleted", new
                        {
                            success = false,
                            message = errMsg
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi chạy ngầm đồng bộ {Platform}", platformName);
                _notificationService?.NotifyAll(
                    "system",
                    $"Lỗi đồng bộ {platformName}",
                    ex.Message,
                    "/summary-tool");

                if (_hubContext is not null)
                {
                    _ = _hubContext.Clients.All.SendAsync("oneDriveSyncCompleted", new
                    {
                        success = false,
                        message = $"Lỗi thực thi robot: {ex.Message}"
                    });
                }
            }
            finally
            {
                _autoTick.DequeueOneDriveJob(request.OneDriveUrl);
            }
        });

        return Ok(new AutoTickSyncSummaryResponse
        {
            Success = true,
            IsQueued = isQueued,
            SyncedRowsCount = request.Rows.Count,
            TickedCellsCount = 0,
            Message = isQueued
                ? $"File OneDrive này hiện đang có robot khác đang chạy đồng bộ. Tiến trình của bạn ({request.Rows.Count} hồ sơ) đã được đưa vào HÀNG ĐỢI và sẽ tự động bắt đầu ngay khi robot trước hoàn tất!"
                : $"Robot đã bắt đầu chạy ngầm trên OneDrive Cloud ({request.Rows.Count} hồ sơ). Bạn có thể yên tâm đóng tab hoặc làm việc khác, hệ thống sẽ tự động gửi thông báo khi hoàn tất!"
        });
    }

    [HttpPost("check-permission")]
    public async Task<ActionResult<CheckPermissionResponse>> CheckPermission(
        [FromBody] CheckPermissionRequest request,
        CancellationToken ct)
    {
        var result = await _autoTick.CheckPermissionAsync(request.Url, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("open-login")]
    public IActionResult OpenLogin([FromBody] CheckPermissionRequest request)
    {
        var result = _autoTick.OpenInteractiveLogin(request.Url);
        return Ok(result);
    }
}

