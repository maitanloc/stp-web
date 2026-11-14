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

        if (!background)
        {
            var syncResult = await _autoTick.SyncSummaryRowsToOneDriveAsync(request, ct).ConfigureAwait(false);
            return Ok(syncResult);
        }

        // Chạy ngầm hoàn toàn trên server: Người dùng có thể đóng tab hoặc chuyển trang mà robot vẫn tiếp tục chạy đến khi hoàn tất
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _autoTick.SyncSummaryRowsToOneDriveAsync(request, CancellationToken.None).ConfigureAwait(false);
                if (result.Success)
                {
                    _notificationService?.NotifyAll(
                        "system",
                        "Đồng bộ OneDrive thành công",
                        $"Đã điền trích yếu và cập nhật tick cho {result.SyncedRowsCount} hồ sơ trên sheet {request.SheetName} ({result.TickedCellsCount} ô thay đổi).",
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
                    _notificationService?.NotifyAll(
                        "system",
                        "Đồng bộ OneDrive chưa hoàn tất",
                        result.Message,
                        "/summary-tool");

                    if (_hubContext is not null)
                    {
                        _ = _hubContext.Clients.All.SendAsync("oneDriveSyncCompleted", new
                        {
                            success = false,
                            message = result.Message
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi chạy ngầm đồng bộ OneDrive");
                _notificationService?.NotifyAll(
                    "system",
                    "Lỗi đồng bộ OneDrive",
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
        });

        return Ok(new AutoTickSyncSummaryResponse
        {
            Success = true,
            SyncedRowsCount = request.Rows.Count,
            TickedCellsCount = 0,
            Message = $"Robot đã bắt đầu chạy ngầm trên OneDrive Cloud ({request.Rows.Count} hồ sơ). Bạn có thể yên tâm đóng tab hoặc làm việc khác, hệ thống sẽ tự động gửi thông báo khi hoàn tất!"
        });
    }
}

