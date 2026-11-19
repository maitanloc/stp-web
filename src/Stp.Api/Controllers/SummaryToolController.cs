using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Hubs;
using Stp.Api.Services;
using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Core.Security;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Core.Sync;
using Stp.Infrastructure.Persistence;
using Stp.Infrastructure.AI;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/summary")]
public sealed class SummaryToolController : ControllerBase
{
    private const long UploadRequestLimit = 210L * 1024 * 1024;
    // Quy mô thật của phòng STP: file 22.600 dòng, một lần "Áp dụng cho dòng
    // lọc còn thiếu" (C3) có thể tạo hơn 22.000 dòng thay đổi cùng lúc → phải
    // đủ lớn để xuất nguyên một file trong 1 request (payload ~8 MB < 30 MB).
    private const int MaxChanges = 30000;
    private const int MaxItemsPerRow = 80;
    private const int MaxItemLength = 200;
    private const int MaxSummaryLength = 4000;
    private const int MaxSheetNumberLength = 40;

    private readonly ISummaryToolService _summary;
    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly TeamAudit _audit;
    private readonly IHubContext<TeamHub> _hubContext;
    private readonly NotificationService _notifications;
    private readonly IArchiveSyncService _sync;
    private readonly IFileStorage _storage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SummaryToolController(
        ISummaryToolService summary,
        IDbContextFactory<StpDbContext> factory,
        TeamAudit audit,
        IHubContext<TeamHub> hubContext,
        NotificationService notifications,
        IArchiveSyncService sync,
        IFileStorage storage,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _summary = summary;
        _factory = factory;
        _audit = audit;
        _hubContext = hubContext;
        _notifications = notifications;
        _sync = sync;
        _storage = storage;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <summary>Mở workbook và tách danh mục giấy tờ trong cột trích yếu.</summary>
    [HttpPost("inspect")]
    [RequestSizeLimit(UploadRequestLimit)]
    public async Task<ActionResult<SummaryInspectResponse>> Inspect(
        [FromForm] IFormFile file,
        [FromQuery] bool? makeActive,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "File rỗng",
                Detail = "File upload không có nội dung.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var session = await _summary.CreateSessionAsync(
                stream,
                file.FileName,
                cancellationToken);

            var actor = await TeamAudit.ActorFromAsync(HttpContext);
            var identity = await StpTeamIdentity.FromAsync(HttpContext);
            var isAdmin = identity?.Role == AccountRules.RoleAdmin;

            var existingActive = _summary.GetActiveSession();
            var shouldMakeActive = false;

            if (makeActive == true)
            {
                // Chỉ cho phép đặt làm active nếu chưa có file live, hoặc người này là uploader file cũ hoặc là admin
                if (existingActive is null
                    || isAdmin
                    || string.Equals(existingActive.UploadedBy, actor, StringComparison.OrdinalIgnoreCase))
                {
                    shouldMakeActive = true;
                }
            }
            else if (makeActive == null)
            {
                // Mặc định: nếu chưa có file active nào thì đặt làm active
                // Nếu ĐÃ CÓ file active của người khác thì KHÔNG tự ý đè làm mất file live của họ!
                if (existingActive is null)
                {
                    shouldMakeActive = true;
                }
            }

            if (shouldMakeActive)
            {
                _summary.SetActiveSession(session.SessionId, actor);

                _notifications.NotifyAllExcept(
                    actor,
                    "system",
                    "Phiên làm việc chung mới",
                    $"{actor} vừa tải lên file làm việc chung: {file.FileName} ({session.Rows.Count:N0} hồ sơ)",
                    "/trich-yeu");

                _ = _hubContext.Clients.All.SendAsync("teamSessionChanged", new
                {
                    sessionId = session.SessionId,
                    fileName = session.FileName,
                    uploadedBy = actor,
                    uploadedAt = DateTimeOffset.UtcNow,
                    rowCount = session.Rows.Count,
                    editCount = 0
                });
            }

            return Ok(ToResponse(session));
        }
        catch (Exception ex) when (ex is InvalidDataException
                                       or NotSupportedException
                                       or IOException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Không đọc được workbook",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>Lấy thông tin phiên làm việc chung đang hoạt động của cả phòng.</summary>
    [HttpGet("active")]
    public ActionResult<SummaryActiveSessionResponse> GetActive()
    {
        var active = _summary.GetActiveSession();
        if (active is null)
        {
            return Ok(new SummaryActiveSessionResponse(
                HasActiveSession: false,
                SessionId: null,
                FileName: null,
                UploadedBy: null,
                UploadedAt: null,
                RowCount: 0,
                EditCount: 0));
        }

        var edits = _summary.GetSessionEdits(active.SessionId);
        return Ok(new SummaryActiveSessionResponse(
            HasActiveSession: true,
            SessionId: active.SessionId,
            FileName: active.FileName,
            UploadedBy: active.UploadedBy,
            UploadedAt: active.UploadedAt,
            RowCount: active.RowCount,
            EditCount: edits.Count));
    }

    /// <summary>Thiết lập một phiên làm việc có sẵn thành phiên làm việc chung của phòng.</summary>
    [HttpPost("active")]
    public async Task<ActionResult<SummaryActiveSessionResponse>> SetActive(
        [FromBody] SummarySetActiveSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = _summary.GetSession(request.SessionId);
        if (session is null)
        {
            return NotFoundProblem("Không tìm thấy phiên làm việc.");
        }

        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        var isAdmin = identity?.Role == AccountRules.RoleAdmin;

        var existingActive = _summary.GetActiveSession();
        if (existingActive is not null
            && !isAdmin
            && !string.Equals(existingActive.UploadedBy, actor, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Phòng đang có file live khác",
                Detail = $"Phòng đang mở file chung '{existingActive.FileName}' do {existingActive.UploadedBy} tải lên. Chỉ {existingActive.UploadedBy} hoặc Quản trị viên mới được thay thế.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        _summary.SetActiveSession(request.SessionId, actor);

        _notifications.NotifyAllExcept(
            actor,
            "system",
            "Phiên làm việc chung",
            $"{actor} vừa chọn file làm việc chung: {session.FileName} ({session.Rows.Count:N0} hồ sơ)",
            "/trich-yeu");

        _ = _hubContext.Clients.All.SendAsync("teamSessionChanged", new
        {
            sessionId = session.SessionId,
            fileName = session.FileName,
            uploadedBy = actor,
            uploadedAt = DateTimeOffset.UtcNow,
            rowCount = session.Rows.Count,
            editCount = _summary.GetSessionEdits(session.SessionId).Count
        });

        return Ok(new SummaryActiveSessionResponse(
            HasActiveSession: true,
            SessionId: session.SessionId,
            FileName: session.FileName,
            UploadedBy: actor,
            UploadedAt: DateTimeOffset.UtcNow,
            RowCount: session.Rows.Count,
            EditCount: _summary.GetSessionEdits(session.SessionId).Count));
    }

    /// <summary>Huỷ thiết lập phiên làm việc chung của phòng (chỉ người tải lên hoặc quản trị viên mới được gỡ).</summary>
    [HttpDelete("active")]
    public async Task<ActionResult> ClearActive()
    {
        var active = _summary.GetActiveSession();
        if (active is null)
        {
            return NoContent();
        }

        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        var isAdmin = identity?.Role == AccountRules.RoleAdmin;
        var isUploader = string.Equals(active.UploadedBy, actor, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin && !isUploader)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Không có quyền gỡ file",
                Detail = $"Chỉ {active.UploadedBy} (người tải lên) hoặc Quản trị viên mới có quyền gỡ file làm việc chung này.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        _summary.ClearActiveSession();
        _ = _hubContext.Clients.All.SendAsync("teamSessionCleared");
        return NoContent();
    }

    /// <summary>Lấy toàn bộ các chỉnh sửa chung của phiên trên server (Shared Edits).</summary>
    [HttpGet("{sessionId:guid}/edits")]
    public ActionResult<IReadOnlyList<SummaryChangeRequest>> GetEdits(Guid sessionId)
    {
        var edits = _summary.GetSessionEdits(sessionId);
        var list = edits.Values.Select(change => new SummaryChangeRequest(
            change.RowNumber,
            change.Items,
            change.SummaryText,
            change.SheetNumber,
            change.DocumentNumber,
            change.NumberLabel)).ToList();

        return Ok(list);
    }

    /// <summary>Ghi nhận một thay đổi của dòng và phát realtime cho đồng đội (Google Sheets live commit).</summary>
    [HttpPost("{sessionId:guid}/change")]
    public async Task<IActionResult> RecordChange(
        Guid sessionId,
        [FromBody] SummaryChangeRequest request)
    {
        if (request.RowNumber <= 0)
        {
            return BadRequest("RowNumber không hợp lệ.");
        }

        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        var change = new SummaryChange(
            request.RowNumber,
            request.Items ?? Array.Empty<string>(),
            request.SummaryText,
            request.SheetNumber,
            request.DocumentNumber,
            request.NumberLabel);

        _summary.RecordEdit(sessionId, change);

        _ = _hubContext.Clients.Group($"summary:{sessionId}").SendAsync("summaryCellChanged", new
        {
            sessionId,
            rowNumber = request.RowNumber,
            items = request.Items ?? Array.Empty<string>(),
            summaryText = request.SummaryText,
            sheetNumber = request.SheetNumber,
            documentNumber = request.DocumentNumber,
            numberLabel = request.NumberLabel,
            actor,
            timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new { success = true });
    }

    /// <summary>Ghi nhận nhiều thay đổi cùng lúc (áp bộ loại / bulk) và phát realtime cho đồng đội.</summary>
    [HttpPost("{sessionId:guid}/changes")]
    public async Task<IActionResult> RecordBulkChanges(
        Guid sessionId,
        [FromBody] IReadOnlyList<SummaryChangeRequest> requests)
    {
        if (requests == null || requests.Count == 0)
        {
            return Ok(new { count = 0 });
        }

        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        var changes = requests.Select(request => new SummaryChange(
            request.RowNumber,
            request.Items ?? Array.Empty<string>(),
            request.SummaryText,
            request.SheetNumber,
            request.DocumentNumber,
            request.NumberLabel)).ToList();

        _summary.RecordBulkEdits(sessionId, changes);

        _ = _hubContext.Clients.Group($"summary:{sessionId}").SendAsync("summaryBulkChanged", new
        {
            sessionId,
            count = changes.Count,
            actor,
            timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new { count = changes.Count });
    }

    /// <summary>
    /// Nạp trực tiếp phiên trích yếu vào Kho hồ sơ (CSDL SQL Server) mà không cần tải file về máy.
    /// </summary>
    [HttpPost("{sessionId:guid}/sync-to-archive")]
    public async Task<ActionResult<SummaryDirectSyncResponse>> SyncToArchive(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = _summary.GetSession(sessionId);
        if (session is null)
        {
            return NotFoundProblem("Không tìm thấy phiên làm việc.");
        }

        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        var edits = _summary.GetSessionEdits(sessionId).Values.ToList();

        try
        {
            // 1. Xuất workbook Excel hoàn thiện kèm mọi thay đổi
            var exportResult = await _summary.ExportFilteredAsync(
                sessionId,
                edits,
                new SummaryScope(), // Phạm vi toàn bộ
                cancellationToken,
                session.FileName);

            var tempFileName = $"direct-sync-{sessionId:N}.xlsx";
            using var stream = new MemoryStream(exportResult.Content);
            var stored = await _storage.SaveUploadAsync(stream, tempFileName, sessionId, cancellationToken);

            // 2. Chạy dịch vụ đồng bộ chuẩn vào SQL Server
            var syncOutcome = await _sync.ApplyAsync(new SyncRequest
            {
                FilePath = stored.FullPath,
                SourceFileName = session.FileName
            }, cancellationToken);

            // 3. Ghi nhật ký & thông báo cả nhóm
            _audit.Write(
                actor,
                "sync-direct",
                "ArchiveWorkbook",
                syncOutcome.WorkbookId?.ToString(),
                $"{session.FileName}: nạp trực tiếp {syncOutcome.UpdatedCount} hồ sơ vào Kho (thêm {syncOutcome.AddedCount})");

            _notifications.NotifyAllExcept(
                actor,
                "system",
                "Đồng bộ Kho hoàn tất",
                $"{actor} vừa nạp trực tiếp dữ liệu {session.FileName} vào Kho ({syncOutcome.UpdatedCount:N0} cập nhật, {syncOutcome.AddedCount:N0} thêm mới)",
                "/kho");

            return Ok(new SummaryDirectSyncResponse(
                syncOutcome.Success,
                syncOutcome.AddedCount,
                syncOutcome.UpdatedCount,
                syncOutcome.ConflictCount,
                syncOutcome.ErrorMessage));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Lỗi đồng bộ vào Kho",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>Thông tin phiên để khôi phục sau F5 / mất kết nối.</summary>
    [HttpGet("{sessionId:guid}")]
    public ActionResult<SummaryInspectResponse> GetSession(Guid sessionId)
    {
        var session = _summary.GetSession(sessionId);
        return session is null
            ? NotFoundProblem("Phiên làm việc không tồn tại hoặc đã hết hạn.")
            : Ok(ToResponse(session));
    }

    /// <summary>Danh sách phiên đang dở để mở lại không cần import.</summary>
    [HttpGet("sessions")]
    public ActionResult<IReadOnlyList<SummarySessionInfoResponse>> ListSessions()
    {
        var sessions = _summary.ListSessions()
            .Select(item => new SummarySessionInfoResponse(
                item.SessionId,
                item.FileName,
                item.FileSizeBytes,
                item.CreatedAt,
                item.RowCount))
            .ToList();

        return Ok(sessions);
    }

    /// <summary>Danh sách dòng hồ sơ, lọc theo năm/loại/nội dung.</summary>
    [HttpGet("{sessionId:guid}/rows")]
    public ActionResult<SummaryRowsResponse> GetRows(
        Guid sessionId,
        [FromQuery] int? year,
        [FromQuery] string? type,
        [FromQuery] string? search,
        [FromQuery] bool onlyExisting = false,
        [FromQuery] string? numberMode = null,
        [FromQuery] int? numberFrom = null,
        [FromQuery] int? numberTo = null,
        [FromQuery] int? number = null,
        [FromQuery] int? yearFrom = null,
        [FromQuery] int? yearTo = null,
        [FromQuery] bool onlyMissingSheetNumber = false,
        [FromQuery] bool onlyMissingDocumentNumber = false,
        [FromQuery] bool onlyMissingNumber = false,
        [FromQuery] bool missingSheetNumberFirst = false,
        [FromQuery] bool onlyNeedsNormalization = false,
        [FromQuery] bool sortByNumber = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryCreateScope(
                new SummaryScopeRequest(
                    year,
                    type,
                    search,
                    onlyExisting,
                    numberMode,
                    numberFrom,
                    numberTo,
                    number,
                    yearFrom,
                    yearTo,
                    onlyMissingSheetNumber,
                    onlyMissingDocumentNumber,
                    onlyMissingNumber,
                    missingSheetNumberFirst,
                    onlyNeedsNormalization,
                    sortByNumber),
                out var scope,
                out var scopeError))
        {
            return InvalidProblem(scopeError!);
        }

        try
        {
            var rows = _summary.GetRows(sessionId, scope, page, pageSize);

            return Ok(new SummaryRowsResponse(
                rows.Items.Select(ToResponse).ToList(),
                rows.Total,
                rows.Page,
                rows.PageSize));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
    }

    /// <summary>Xem trước câu trích yếu sau khi bổ sung danh mục.</summary>
    [HttpPost("{sessionId:guid}/preview")]
    public ActionResult<IReadOnlyList<SummaryPreviewRowResponse>> Preview(
        Guid sessionId,
        [FromBody] SummaryPreviewRequest request)
    {
        if (ValidateChanges(request.Changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var changes = (request.Changes ?? Array.Empty<SummaryChangeRequest>())
                .Select(ToChange)
                .ToList();

            var preview = _summary
                .Preview(sessionId, changes, request.OnlyRowNumbers)
                .Select(ToResponse)
                .ToList();

            return Ok(preview);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>Xuất lại đúng workbook gốc, chỉ thay các ô trích yếu đã chọn.</summary>
    [HttpPost("{sessionId:guid}/export")]
    public async Task<IActionResult> Export(
        Guid sessionId,
        [FromBody] SummaryExportRequest request,
        CancellationToken cancellationToken)
    {
        var requestedChanges = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(requestedChanges, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var changes = requestedChanges
                .Select(ToChange)
                .ToList();

            var result = await _summary.ExportAsync(
                sessionId,
                changes,
                cancellationToken,
                request.OutputFileName,
                request.OnlyRowNumbers);

            _audit.Write(
                await TeamAudit.ActorFromAsync(HttpContext),
                "export",
                "SummarySession",
                sessionId.ToString(),
                $"{result.FileName}: {result.ChangedRows} dòng sửa, {result.NormalizedRows} dòng chuẩn hoá");

            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or InvalidDataException)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>Xem trước bản xuất lọc: số hồ sơ trong phạm vi và các thay đổi được áp dụng.</summary>
    [HttpPost("{sessionId:guid}/export-filtered/preview")]
    public ActionResult<SummaryFilteredPreviewResponse> PreviewFiltered(
        Guid sessionId,
        [FromBody] SummaryFilteredExportRequest request)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        if (!TryCreateScope(request.Scope, out var scope, out var scopeError))
        {
            return InvalidProblem(scopeError!);
        }

        try
        {
            var preview = _summary.PreviewFiltered(
                sessionId,
                changes.Select(ToChange).ToList(),
                scope);

            return Ok(new SummaryFilteredPreviewResponse(
                preview.TotalRows,
                preview.ChangedRows,
                preview.IgnoredChanges,
                preview.NormalizedRows,
                preview.Truncated,
                preview.Items.Select(ToResponse).ToList()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>
    /// Xuất bản lọc: chỉ giữ sheet trích yếu và các hồ sơ trong phạm vi,
    /// áp dụng các thay đổi thuộc phạm vi rồi xoá phần còn lại.
    /// </summary>
    [HttpPost("{sessionId:guid}/export-filtered")]
    public async Task<IActionResult> ExportFiltered(
        Guid sessionId,
        [FromBody] SummaryFilteredExportRequest request,
        CancellationToken cancellationToken)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        if (!TryCreateScope(request.Scope, out var scope, out var scopeError))
        {
            return InvalidProblem(scopeError!);
        }

        try
        {
            var result = await _summary.ExportFilteredAsync(
                sessionId,
                changes.Select(ToChange).ToList(),
                scope,
                cancellationToken,
                request.OutputFileName);

            Response.Headers["X-Summary-Total-Rows"] =
                result.TotalRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Changed-Rows"] =
                result.ChangedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Ignored-Changes"] =
                result.IgnoredChanges.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Normalized-Rows"] =
                result.NormalizedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);

            _audit.Write(
                await TeamAudit.ActorFromAsync(HttpContext),
                "export-filtered",
                "SummarySession",
                sessionId.ToString(),
                $"{result.FileName}: {result.TotalRows} hồ sơ, {result.ChangedRows} dòng sửa, {result.NormalizedRows} dòng chuẩn hoá");

            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or InvalidDataException)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>
    /// Lưu bản nháp lên server (P0.2 data safety): upsert theo (phiên + người
    /// dùng), sống qua F5/restart/máy khác — localStorage chỉ là bản nháp tạm.
    /// </summary>
    [HttpPut("{sessionId:guid}/draft")]
    public async Task<IActionResult> SaveDraft(
        Guid sessionId,
        [FromBody] SummaryDraftSaveRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return InvalidProblem("Chưa có nội dung bản nháp.");
        }

        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        if (actor == "Không rõ")
        {
            return InvalidProblem("Chưa khai tên người dùng — không biết lưu bản nháp cho ai.");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var draft = await db.SummaryDrafts.SingleOrDefaultAsync(
            candidate => candidate.SessionId == sessionId && candidate.UserName == actor,
            cancellationToken);

        if (draft is null)
        {
            draft = new SummaryDraft { SessionId = sessionId, UserName = actor };
            db.SummaryDrafts.Add(draft);
        }

        draft.FileName = request.FileName.Trim();
        draft.SavedAt = DateTimeOffset.UtcNow;
        draft.PayloadJson = JsonSerializer.Serialize(request.Payload);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Bản nháp đã lưu trên server cho (phiên + người dùng); 204 khi chưa có.</summary>
    [HttpGet("{sessionId:guid}/draft")]
    public async Task<IActionResult> GetDraft(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var draft = await db.SummaryDrafts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.SessionId == sessionId && candidate.UserName == actor,
                cancellationToken);

        if (draft is null)
        {
            return NoContent();
        }

        return Ok(new SummaryDraftResponse(
            draft.FileName,
            draft.SavedAt,
            JsonSerializer.Deserialize<JsonElement>(draft.PayloadJson)));
    }

    /// <summary>Xoá bản nháp trên server (sau khi xuất thành công).</summary>
    [HttpDelete("{sessionId:guid}/draft")]
    public async Task<IActionResult> DeleteDraft(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var draft = await db.SummaryDrafts.SingleOrDefaultAsync(
            candidate => candidate.SessionId == sessionId && candidate.UserName == actor,
            cancellationToken);

        if (draft is not null)
        {
            db.SummaryDrafts.Remove(draft);
            await db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    /// <summary>Xem trước mức sẵn sàng in theo năm sau khi áp dụng chỉnh sửa hiện tại.</summary>
    [HttpPost("{sessionId:guid}/export-print/preview")]
    public ActionResult<SummaryPrintPreviewResponse> PreviewPrint(
        Guid sessionId,
        [FromBody] SummaryPrintPreviewRequest request)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var preview = _summary.PreviewPrint(
                sessionId,
                changes.Select(ToChange).ToList(),
                request.Years);

            return Ok(new SummaryPrintPreviewResponse(
                preview.Years
                    .Select(item => new SummaryPrintYearStatusResponse(
                        item.Year,
                        item.Total,
                        item.Ready,
                        item.MissingNumber,
                        item.MissingDocumentCode,
                        item.MissingDate,
                        item.MissingSheetNumber,
                        item.MissingSummary))
                    .ToList(),
                preview.TotalRows,
                preview.HasIssues));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>
    /// Xuất bản in theo năm: sinh sheet BANG_IN_HANG_LOAT + macro in/lọc.
    /// perYear trả về .zip nhiều .xlsm; combined trả về một .xlsm.
    /// </summary>
    [HttpPost("{sessionId:guid}/export-print")]
    public async Task<IActionResult> ExportPrint(
        Guid sessionId,
        [FromBody] SummaryPrintExportRequest request,
        CancellationToken cancellationToken)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        var mode = SummaryPrintOutputMode.PerYear;
        if (!string.IsNullOrWhiteSpace(request.Mode)
            && !Enum.TryParse(request.Mode, ignoreCase: true, out mode))
        {
            return InvalidProblem("Kiểu xuất bản in không hợp lệ (perYear hoặc combined).");
        }

        try
        {
            var result = await _summary.ExportPrintAsync(
                sessionId,
                changes.Select(ToChange).ToList(),
                new SummaryPrintOptions(
                    request.Years,
                    mode,
                    request.AllowIncomplete,
                    request.OutputFileName),
                cancellationToken);

            Response.Headers["X-Summary-Print-Rows"] =
                result.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Print-Changed-Rows"] =
                result.ChangedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Print-Normalized-Rows"] =
                result.NormalizedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Print-Years"] =
                string.Join(',', result.Years);
            // Header HTTP chỉ nhận ASCII; mã hoá để vẫn chuyển được cảnh báo tiếng Việt.
            Response.Headers["X-Summary-Print-Warnings"] =
                Uri.EscapeDataString(string.Join(" | ", result.Warnings));

            _audit.Write(
                await TeamAudit.ActorFromAsync(HttpContext),
                "export-print",
                "SummarySession",
                sessionId.ToString(),
                $"{result.FileName}: {result.RowCount} hồ sơ in hàng loạt ({string.Join(", ", result.Years)})");

            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or InvalidDataException)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>Báo cáo dữ liệu thiếu theo năm cho phiên hiện tại.</summary>
    [HttpGet("{sessionId:guid}/report")]
    public ActionResult<IReadOnlyList<SummaryYearReportResponse>> GetReport(Guid sessionId)
    {
        try
        {
            var report = _summary.GetReport(sessionId)
                .Select(item => new SummaryYearReportResponse(
                    item.Year,
                    item.Total,
                    item.MissingSheetNumber,
                    item.MissingDocumentNumber,
                    item.MissingNumber,
                    item.NeedsNormalization))
                .ToList();

            return Ok(report);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
    }

    /// <summary>Kết thúc phiên làm việc và xoá file upload tạm.</summary>
    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Delete(Guid sessionId)
    {
        var active = _summary.GetActiveSession();
        if (active?.SessionId == sessionId)
        {
            var actor = await TeamAudit.ActorFromAsync(HttpContext);
            var identity = await StpTeamIdentity.FromAsync(HttpContext);
            var isAdmin = identity?.Role == AccountRules.RoleAdmin;
            var isUploader = string.Equals(active.UploadedBy, actor, StringComparison.OrdinalIgnoreCase);

            if (!isAdmin && !isUploader)
            {
                // Bảo vệ phiên làm việc chung: thành viên thường đóng xem thì không được xóa file live của phòng
                return Ok(new { message = "Phiên làm việc chung được bảo vệ và tiếp tục live." });
            }
        }

        return _summary.DeleteSession(sessionId)
            ? NoContent()
            : NotFoundProblem("Phiên làm việc không tồn tại hoặc đã hết hạn.");
    }

    private ActionResult? ValidateChanges(
        IReadOnlyList<SummaryChangeRequest>? changes,
        bool allowEmpty = false)
    {
        if (changes is null || changes.Count == 0)
        {
            return allowEmpty ? null : InvalidProblem("Chưa có thay đổi nào.");
        }

        if (changes.Count > MaxChanges)
        {
            return InvalidProblem($"Một lần chỉ xuất tối đa {MaxChanges} dòng.");
        }

        foreach (var change in changes)
        {
            if (change.RowNumber <= 0)
            {
                return InvalidProblem("Số dòng không hợp lệ.");
            }

            if (change.Items is null || change.Items.Count > MaxItemsPerRow)
            {
                return InvalidProblem(
                    $"Mỗi dòng chỉ chọn tối đa {MaxItemsPerRow} loại giấy tờ.");
            }

            if (change.Items.Any(item => item is null || item.Length > MaxItemLength))
            {
                return InvalidProblem(
                    $"Tên giấy tờ không được dài quá {MaxItemLength} ký tự.");
            }

            if (change.SummaryText is { Length: > MaxSummaryLength })
            {
                return InvalidProblem(
                    $"Trích yếu không được dài quá {MaxSummaryLength} ký tự.");
            }

            if (change.SheetNumber is { Length: > MaxSheetNumberLength })
            {
                return InvalidProblem(
                    $"Số tờ không được dài quá {MaxSheetNumberLength} ký tự.");
            }
        }

        return null;
    }

    private static SummaryChange ToChange(SummaryChangeRequest change) =>
        new(
            change.RowNumber,
            change.Items,
            change.SummaryText,
            change.SheetNumber,
            change.DocumentNumber,
            change.NumberLabel);

    private static bool TryCreateScope(
        SummaryScopeRequest? request,
        out SummaryScope scope,
        out string? error)
    {
        scope = SummaryScope.All;
        error = null;

        if (request is null)
        {
            return true;
        }

        if (request.Year.HasValue && request.Year.Value is < 1900 or > 2200)
        {
            error = "Năm lọc không hợp lệ.";
            return false;
        }

        if ((request.YearFrom.HasValue && request.YearFrom.Value is < 1900 or > 2200)
            || (request.YearTo.HasValue && request.YearTo.Value is < 1900 or > 2200))
        {
            error = "Khoảng năm không hợp lệ.";
            return false;
        }

        if (request.YearFrom.HasValue
            && request.YearTo.HasValue
            && request.YearFrom.Value > request.YearTo.Value)
        {
            error = "Năm bắt đầu không được lớn hơn năm kết thúc.";
            return false;
        }

        var mode = SummaryNumberMode.All;
        if (!string.IsNullOrWhiteSpace(request.NumberMode)
            && !Enum.TryParse(request.NumberMode, ignoreCase: true, out mode))
        {
            error = "Kiểu lọc số hồ sơ không hợp lệ.";
            return false;
        }

        if (mode == SummaryNumberMode.Range)
        {
            if (!request.NumberFrom.HasValue && !request.NumberTo.HasValue)
            {
                error = "Nhập ít nhất một đầu của khoảng số hồ sơ.";
                return false;
            }

            if ((request.NumberFrom.HasValue && request.NumberFrom.Value <= 0)
                || (request.NumberTo.HasValue && request.NumberTo.Value <= 0))
            {
                error = "Số hồ sơ phải lớn hơn 0.";
                return false;
            }
        }
        else if (mode == SummaryNumberMode.Single)
        {
            if (!request.Number.HasValue || request.Number.Value <= 0)
            {
                error = "Nhập số hồ sơ cần lọc.";
                return false;
            }
        }

        scope = new SummaryScope(
            request.Year,
            request.Type,
            request.Search,
            request.OnlyExisting,
            mode,
            mode == SummaryNumberMode.Range ? request.NumberFrom : null,
            mode == SummaryNumberMode.Range ? request.NumberTo : null,
            mode == SummaryNumberMode.Single ? request.Number : null,
            request.YearFrom,
            request.YearTo,
            request.OnlyMissingSheetNumber,
            request.OnlyMissingDocumentNumber,
            request.OnlyMissingNumber,
            request.MissingSheetNumberFirst,
            request.OnlyNeedsNormalization,
            request.SortByNumber);

        return true;
    }

    /// <summary>Phân tích câu nói giọng nói bằng AI Local (Ollama Qwen 2.5).</summary>
    [HttpPost("ai/parse-voice")]
    public async Task<ActionResult<SummaryAiParseVoiceResponse>> ParseVoice(
        [FromBody] SummaryAiParseVoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Ok(new SummaryAiParseVoiceResponse(false, Array.Empty<string>(), Error: "Chuỗi văn bản rỗng"));
        }

        var baseUrl = _configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        var model = string.IsNullOrWhiteSpace(request.Model) 
            ? (_configuration["Ollama:Model"] ?? "qwen2.5:3b") 
            : request.Model.Trim();

        var text = PreprocessSpokenText(request.Text.Trim(), request.Learned);

        // Kiểm tra nhanh lệnh xác nhận / hủy (Interactive Confirmation Turn)
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^(?:đúng\s*rồi|đồng\s*ý|áp\s*đi|làm\s*đi|xác\s*nhận|chuẩn\s*rồi|chính\s*xác|có|phải|ok|tiến\s*hành|thực\s*hiện)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return Ok(new SummaryAiParseVoiceResponse(
                true,
                Array.Empty<string>(),
                Command: "confirm",
                Model: model,
                Reply: "Đã xác nhận thực hiện."));
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^(?:không|hủy|thôi|dừng\s*lại|bỏ\s*qua|không\s*phải|đừng\s*làm)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return Ok(new SummaryAiParseVoiceResponse(
                true,
                Array.Empty<string>(),
                Command: "cancel",
                Model: model,
                Reply: "Đã hủy thao tác."));
        }

        var catalogList = request.Catalog != null && request.Catalog.Count > 0
            ? string.Join(", ", request.Catalog.Select(c => $"\"{c}\""))
            : "\"giấy khai sinh\", \"bộ công hàm\", \"hộ khẩu\", \"chứng chỉ chuyên khoa\", \"CMND\", \"phiếu nhập xuất cảnh\", \"hộ chiếu\", \"thị thực\", \"tờ khai đăng ký kết hôn\", \"đơn xin xác nhận độc thân và hôn nhân thực tế\", \"tờ trình\", \"chứng nhận đăng ký khai sinh\", \"đơn xin xác nhận thường trú\", \"bản tuyên thệ\", \"chứng nhận kết hôn\", \"biên lai thu phí và lệ phí\", \"phiếu khám sức khỏe\", \"giấy cam kết\", \"giấy xác nhận\"";

        using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await EnsureDefaultMemoriesSeededAsync(db, cancellationToken);

        // Nạp các ký ức tương tác thực tế từ CSDL để đưa vào Dynamic Few-Shot Learning
        var topMemories = await db.VoiceMemories
            .AsNoTracking()
            .OrderByDescending(m => m.UsageCount)
            .ThenByDescending(m => m.LastUsedAt)
            .Take(6)
            .ToListAsync(cancellationToken);

        var fewShotBuilder = new System.Text.StringBuilder();
        if (topMemories.Count > 0)
        {
            fewShotBuilder.AppendLine("\nCÁC MẪU CÂU THỰC TẾ HỆ THỐNG ĐÃ HỌC TỪ PHÒNG (DYNAMIC FEW-SHOT):");
            int fIdx = 1;
            foreach (var mem in topMemories)
            {
                var itm = string.IsNullOrWhiteSpace(mem.ItemsJson) ? "[]" : mem.ItemsJson;
                var cmd = string.IsNullOrWhiteSpace(mem.Command) ? "null" : $"\"{mem.Command}\"";
                var sht = string.IsNullOrWhiteSpace(mem.SheetNumber) ? "null" : $"\"{mem.SheetNumber}\"";
                var row = mem.TargetRowNumber.HasValue ? mem.TargetRowNumber.Value.ToString() : "null";
                fewShotBuilder.AppendLine($"VÍ DỤ {fIdx++} (ĐÃ HỌC):");
                fewShotBuilder.AppendLine($"Người dùng: \"{mem.RawSpeech}\"");
                fewShotBuilder.AppendLine($"JSON: {{\"items\": {itm}, \"command\": {cmd}, \"sheetNumber\": {sht}, \"targetRowNumber\": {row}, \"year\": null, \"type\": null, \"numberFrom\": null, \"numberTo\": null, \"reply\": \"Đã hiểu và xử lý theo ngữ cảnh.\"}}");
            }
        }
        var dynamicFewShotBlock = fewShotBuilder.ToString();

        var systemPrompt = $$"""
Bạn là AI điều khiển toàn năng bảng trích yếu hồ sơ Sở Tư Pháp Việt Nam.
Danh mục giấy tờ chuẩn (Personal Dictionary): [{{catalogList}}].
Trả về DUY NHẤT một JSON hợp lệ (không kèm markdown, không giải thích).

CẤU TRÚC JSON:
{
  "items": [],
  "command": null,
  "sheetNumber": null,
  "targetRowNumber": null,
  "year": null,
  "type": null,
  "numberFrom": null,
  "numberTo": null,
  "reply": "Câu trả lời hoặc giải thích ngắn gọn bằng tiếng Việt"
}

CÁC LỆNH (command):
- "apply_missing": khi nói "áp nhanh", "sử dụng áp nhanh", "áp nhanh các giấy tờ", "áp nhanh toàn bộ các dòng lọc", "áp nhanh các dòng lọc", "áp vào dòng lọc còn thiếu", "áp các dòng lọc còn thiếu", "áp dòng lọc", "áp các dòng còn thiếu", "áp dòng thiếu", "áp hàng loạt", "áp cho toàn bộ dòng thiếu" (đây là nút '✓ Áp dụng cho dòng lọc còn thiếu' trong thanh Áp nhanh để điền bộ chuẩn vào tất cả các dòng lọc).
- "apply_fast": CHỈ KHI nói rõ "áp dòng này", "áp hồ sơ này", "áp ô này", "điền dòng hiện tại" (áp bộ chuẩn vào duy nhất 1 dòng đang mở).
- "select_row": khi nói "chọn dòng 1", "chọn dòng đầu tiên", "chọn dòng 2", "tích dòng 1", "tích dòng đầu", "mở dòng 1", "mở dòng đầu tiên", "sửa dòng 1".
- "clear_items": khi nói "xóa hết giấy tờ", "bỏ hết giấy tờ", "làm lại dòng này", "xóa trắng".
- "remove_item": khi nói "xóa [giấy tờ]", "bỏ [giấy tờ]" (items chứa tên giấy tờ cần xóa).
- "replace_items": khi nói "thay bằng", "đổi thành", "chỉ có".
- "done": "xong", "lưu", "lưu dòng".
- "next": "tiếp", "kế tiếp", "dòng sau".
- "prev": "quay lại", "dòng trước".
- "seven": "bộ bảy", "bộ 7".
- "clear_seven": khi nói "bỏ bộ 7", "bỏ bộ bảy", "xóa bộ 7", "hủy bộ 7", "bỏ bộ 7 đi", "bỏ bộ bảy đi" (xóa bỏ bộ 7 giấy tờ khỏi dòng hoặc khỏi danh sách áp nhanh).

QUY TẮC BẮT BUỘC:
1. Dòng / Hồ sơ đích (targetRowNumber):
   - "hồ sơ 5192", "hồ sơ số 5192", "số 5192" -> targetRowNumber: 5192. Số đứng sau chữ "hồ sơ" là targetRowNumber, KHÔNG PHẢI sheetNumber!
   - "dòng đầu tiên", "dòng 1", "dòng đầu", "dòng thứ nhất" -> targetRowNumber: 1.
   - "dòng 2", "dòng thứ hai" -> targetRowNumber: 2.
   - "dòng cuối", "dòng cuối cùng" -> targetRowNumber: 999.
   - "dòng 1601" -> targetRowNumber: 1601.
   - Không nói dòng hoặc hồ sơ cụ thể -> null.

2. Số tờ (sheetNumber):
   - CHỈ lấy khi có từ "tờ" hoặc "số tờ" (ví dụ: "số tờ 05", "10 tờ", "01-34").
   - TUYỆT ĐỐI KHÔNG lấy số hồ sơ (như 5192, 5193) làm số tờ! Nếu không có số tờ -> null.

3. Lọc bảng (year, type, numberFrom, numberTo):
   - "5127 và 5128", "từ 5127 đến 5128", "5127 tới 5128", "từ số 5192 đến 5204" -> numberFrom: số đầu, numberTo: số sau (ví dụ: numberFrom: 5127, numberTo: 5128).
   - "hồ sơ 5127", "số 5127" -> numberFrom: 5127, numberTo: 5127, targetRowNumber: 5127.
   - "năm 2000", "xem năm 1998" -> year: 2000, 1998. CHÚ Ý: "từ số 5192" là số hồ sơ, TUYỆT ĐỐI KHÔNG nhầm là năm 1994! Nếu không nói năm -> year: null.
   - "loại kết hôn" -> type: "Đăng ký kết hôn".
   - Khi câu nói CHỈ là lệnh lọc (không nhắc tên giấy tờ) -> items PHẢI LÀ [].

4. Giấy tờ (items):
   - Chỉ thêm khi người dùng nói tên giấy tờ.
   - Nhận diện đúng 12 giấy tờ chuẩn Đăng ký kết hôn và các giấy tờ hộ tịch phổ biến:
     * "khai sinh" -> "giấy khai sinh"
     * "bộ công hàm", "công hàm", "bản công hàm", "bộ công an" -> "bộ công hàm"
     * "khẩu", "hộ khẩu", "sổ hộ khẩu" -> "hộ khẩu"
     * "phiếu đề xuất", "đề xuất" -> "phiếu đề xuất"
     * "chứng chỉ chuyên khoa", "chuyên khoa" -> "chứng chỉ chuyên khoa"
     * "sức khỏe", "khám sức khỏe", "phiếu khám sức khỏe" -> "phiếu khám sức khỏe"
     * "cmnd", "chứng minh", "chứng minh nhân dân", "căn cước", "cccd" -> "CMND"
     * "nhập xuất cảnh", "xuất nhập cảnh", "phiếu nhập xuất cảnh", "phiếu xuất nhập cảnh", "nxc", "xnc" -> "phiếu nhập xuất cảnh"
     * "hộ chiếu", "passport" -> "hộ chiếu"
     * "thị thực", "visa" -> "thị thực"
     * "tờ khai đăng ký kết hôn", "tờ khai kết hôn", "tờ khai" -> "tờ khai đăng ký kết hôn"
     * "đơn xin xác nhận độc thân và hôn nhân thực tế", "độc thân và hôn nhân thực tế", "hôn nhân thực tế", "đơn độc thân", "xác nhận độc thân" -> "đơn xin xác nhận độc thân và hôn nhân thực tế"
     * "kết hôn/chứng nhận kết hôn" -> "giấy chứng nhận kết hôn"
     * "sơ yếu lý lịch" -> "sơ yếu lý lịch"
     * "ảnh" -> "ảnh"

5. Phản hồi đối thoại & Giải thích (reply):
   - Quy định câu trả lời (reply): CỰC KỲ NGẮN GỌN (tối đa 5-8 từ), đúng trọng tâm, tuyệt đối không giải thích dài dòng để AI phản hồi nhanh trong 1-2 giây.
   - Nếu câu nói là trò chuyện, chào hỏi, hỏi thăm: Trả lời ngắn gọn, thân thiện (ví dụ: "Chào bạn, tôi nghe rõ.", "Tôi sẵn sàng hỗ trợ.").
   - Nếu câu nói là thao tác (điền giấy tờ, lọc, chuyển trang...): Trả lời xác nhận ngắn gọn (ví dụ: "Đã lọc hồ sơ từ 5127 đến 5128.", "Đã chọn dòng 1.", "Đã áp nhanh bộ 7 chuẩn.").

VÍ DỤ 1:
Người dùng: "áp nhanh" hoặc "sử dụng áp nhanh" hoặc "áp nhanh toàn bộ các dòng lọc" hoặc "áp vào dòng lọc còn thiếu"
JSON:
{"items": [], "command": "apply_missing", "sheetNumber": null, "targetRowNumber": null, "year": null, "type": null, "numberFrom": null, "numberTo": null, "reply": "Đã áp nhanh bộ 7 giấy tờ chuẩn."}

VÍ DỤ 2:
Người dùng: "hồ sơ 5192 bao gồm giấy chứng nhận kết hôn"
JSON:
{"items": ["Giấy chứng nhận kết hôn"], "command": null, "sheetNumber": null, "targetRowNumber": 5192, "year": null, "type": null, "numberFrom": null, "numberTo": null, "reply": "Đã thêm giấy chứng nhận kết hôn vào hồ sơ 5192."}

VÍ DỤ 3:
Người dùng: "bạn làm được gì" hoặc "bạn có thể nói chuyện được không"
JSON:
{"items": [], "command": null, "sheetNumber": null, "targetRowNumber": null, "year": null, "type": null, "numberFrom": null, "numberTo": null, "reply": "Tôi nghe rõ. Bạn cần tôi làm gì?"}

VÍ DỤ 4:
Người dùng: "5127 và 5128" hoặc "từ 5127 đến 5128"
JSON:
{"items": [], "command": null, "sheetNumber": null, "targetRowNumber": null, "year": null, "type": null, "numberFrom": 5127, "numberTo": 5128, "reply": "Đã lọc hồ sơ từ 5127 đến 5128."}
{{dynamicFewShotBlock}}
""";

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutSec = _configuration.GetValue<int>("Ollama:TimeoutSeconds", 25);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSec)));

            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);

            var ollamaPayload = new
            {
                model = model,
                system = systemPrompt,
                prompt = $"Câu nói: \"{text}\"\nJSON:",
                stream = false,
                format = "json",
                options = new
                {
                    temperature = 0.0,
                    top_p = 0.9
                }
            };

            var httpResponse = await client.PostAsJsonAsync("/api/generate", ollamaPayload, timeoutCts.Token);
            if (!httpResponse.IsSuccessStatusCode)
            {
                return Ok(new SummaryAiParseVoiceResponse(false, Array.Empty<string>(), Model: model, Error: $"Ollama HTTP {(int)httpResponse.StatusCode}"));
            }

            var ollamaResult = await httpResponse.Content.ReadFromJsonAsync<OllamaVoiceGenerateResponse>(cancellationToken: timeoutCts.Token);
            if (ollamaResult == null || string.IsNullOrWhiteSpace(ollamaResult.Response))
            {
                return Ok(new SummaryAiParseVoiceResponse(false, Array.Empty<string>(), Model: model, Error: "Ollama trả về rỗng"));
            }

            using var doc = JsonDocument.Parse(ollamaResult.Response);
            var root = doc.RootElement;

            var items = new List<string>();
            if (root.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in itemsElem.EnumerateArray())
                {
                    var val = el.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(val))
                    {
                        if (request.Catalog != null)
                        {
                            var matched = request.Catalog.FirstOrDefault(c => string.Equals(c, val, StringComparison.OrdinalIgnoreCase));
                            items.Add(matched ?? val);
                        }
                        else
                        {
                            items.Add(val);
                        }
                    }
                }
            }

            string? command = null;
            if (root.TryGetProperty("command", out var cmdElem) && cmdElem.ValueKind == JsonValueKind.String)
            {
                var cmdStr = cmdElem.GetString()?.Trim().ToLowerInvariant();
                if (cmdStr is "next" or "done" or "seven" or "clear_seven" or "prev" or "apply_missing" or "clear_items" or "remove_item" or "replace_items" or "apply_fast" or "select_row")
                {
                    command = cmdStr;
                }
            }

            // Chống nghe nhầm từ ngữ âm thanh nói nhỏ: "bộ công an" -> "bộ công hàm"
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Equals("Bộ công an", StringComparison.OrdinalIgnoreCase) ||
                    items[i].Equals("bộ công an", StringComparison.OrdinalIgnoreCase))
                {
                    items[i] = "Bộ công hàm";
                }
            }

            string? sheetNumber = null;
            if (root.TryGetProperty("sheetNumber", out var sheetElem) && sheetElem.ValueKind == JsonValueKind.String)
            {
                sheetNumber = sheetElem.GetString()?.Trim();
            }

            // Trích xuất số tờ bằng regex nếu người dùng nói "số tờ ..." hoặc "tờ ..."
            if (string.IsNullOrWhiteSpace(sheetNumber))
            {
                var sheetMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:số\s*tờ|tờ)\s*([0-9\/-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (sheetMatch.Success)
                {
                    sheetNumber = sheetMatch.Groups[1].Value.Trim();
                }
            }

            int? targetRowNumber = null;
            if (root.TryGetProperty("targetRowNumber", out var rowElem) && rowElem.ValueKind == JsonValueKind.Number && rowElem.TryGetInt32(out var rowVal))
            {
                targetRowNumber = rowVal;
            }

            int? targetDocumentNumber = null;

            // Post-processing deterministic:
            // 1. Trích xuất trực tiếp số hồ sơ nếu câu nói có "hồ sơ <số>" (ví dụ "hồ sơ 5192", "hồ sơ số 5192")
            var hsMatch = System.Text.RegularExpressions.Regex.Match(text, @"hồ\s*sơ\s*(?:số\s*)?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (hsMatch.Success && int.TryParse(hsMatch.Groups[1].Value, out var hsVal))
            {
                targetDocumentNumber = hsVal;
                targetRowNumber = null;
                if (sheetNumber == hsMatch.Groups[1].Value)
                {
                    sheetNumber = null;
                }
            }

            // 2. Nếu số tờ bị nhầm thành số hồ sơ (>= 500)
            if (sheetNumber != null && int.TryParse(sheetNumber, out var sNum) && sNum >= 500)
            {
                if (targetDocumentNumber == null && targetRowNumber == null)
                {
                    targetDocumentNumber = sNum;
                }
                sheetNumber = null;
            }

            // 3. Trích xuất "dòng đầu tiên" / "dòng 1" / "dòng [số]" (khi không phải chỉ định hồ sơ)
            if (targetDocumentNumber == null)
            {
                var rowMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:dòng|hàng)\s*(?:thứ\s*)?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (rowMatch.Success && int.TryParse(rowMatch.Groups[1].Value, out var parsedRow))
                {
                    targetRowNumber = parsedRow;
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:dòng|hàng)\s*(?:đầu\s*tiên|đầu|thứ\s*nhất\b|trên\s*cùng|1\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    targetRowNumber = 1;
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:dòng|hàng)\s*(?:cuối|cuối\s*cùng|dưới\s*cùng)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    targetRowNumber = 999;
                }
            }

            // 4. Trích xuất "áp nhanh" / "sử dụng áp nhanh" -> Hàng loạt cho các dòng lọc còn thiếu
            // CHỈ khi nói rõ "áp dòng này", "áp hồ sơ này", "áp ô này" mới là apply_fast (đơn lẻ 1 dòng).
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"áp\s*(?:nhanh\s*)?(?:dòng\s*này|hồ\s*sơ\s*này|ô\s*này|cho\s*dòng\s*này)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "apply_fast";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"áp\s*nhanh|sử\s*dụng\s*áp\s*nhanh|áp\s*(?:toàn\s*bộ\s*)?(?:các\s*)?dòng\s*lọc|áp\s*dòng\s*lọc|áp\s*(?:vào|cho)?\s*(?:các)?\s*dòng\s*(?:lọc\s*)?(?:còn\s*)?thiếu|áp\s*dòng\s*thiếu|áp\s*hàng\s*loạt|áp\s*bộ\s*7\s*(?:chuẩn)?|áp\s*bộ\s*chuẩn|điền\s*(?:các\s*)?dòng\s*(?:còn\s*)?thiếu|gán\s*(?:cho\s*)?dòng\s*thiếu|áp\s*(?:nhanh\s*)?(?:cho\s*)?giấy\s*tờ|áp\s*(?:nhanh\s*)?các\s*giấy\s*tờ|áp\s*(?:nhanh\s*)?(?:cho\s*)?toàn\s*bộ", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "apply_missing";
            }

            // 4b. Trích xuất "chọn dòng [số]", "tích dòng [số]", "mở dòng [số]", "sửa dòng [số]", "chọn hồ sơ [số]"
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:chọn|tích|mở|sửa)\s*(?:dòng|hàng|hồ\s*sơ|số)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                !System.Text.RegularExpressions.Regex.IsMatch(text, @"tất\s*cả|toàn\s*bộ|hết|thiếu", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (command == null || command == "toggle_select_current")
                {
                    command = "select_row";
                }
            }

            // 4c. Trích xuất "bỏ bộ 7" / "xóa bộ 7" / "hủy bộ 7" / "bỏ bộ 7 đi"
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:bỏ|xóa|hủy|loại\s*bỏ)\s*(?:bộ\s*(?:7|bảy)|bộ\s*chuẩn)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "clear_seven";
                items.Clear();
            }

            // 5. Trích xuất "xóa hết" / "làm lại" (ghi đè mọi phán đoán khác)
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"xóa\s*(?:hết|trắng|toàn\s*bộ)|bỏ\s*hết|làm\s*lại", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "clear_items";
            }

            // 6. Trích xuất "thay bằng" / "đổi thành"
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"thay\s*bằng|đổi\s*thành|chỉ\s*có\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "replace_items";
            }
            else if (command == "replace_items")
            {
                // Nếu người dùng không nói thay thế (ví dụ nói "thêm giấy...") thì không cho replace
                command = null;
            }

            // 7. Trích xuất "xóa [giấy tờ]" / "bỏ [giấy tờ]"
            if (command != "clear_items" && System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:xóa|bỏ)\s+(?:giấy|căn|sổ|bản|đơn|ảnh|chứng|phiếu|tờ)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "remove_item";
            }

            // 8. Trích xuất Phân trang (Pagination)
            int? targetPage = null;
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"trang\s*(?:sau|kế\s*tiếp|tiếp\s*theo)|qua\s*trang|tiếp\s*trang", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "next_page";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"trang\s*trước|lùi\s*trang|quay\s*lại\s*trang", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "prev_page";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"trang\s*đầu|về\s*trang\s*(?:đầu|1|một)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "first_page";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"trang\s*cuối|về\s*trang\s*cuối", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "last_page";
            }
            else
            {
                var pageMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:đến|nhảy\s*đến|về|qua)\s*trang\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out var pVal))
                {
                    command = "goto_page";
                    targetPage = pVal;
                }
            }

            // 9. Trích xuất Chọn dòng (Selection)
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:tích|chọn)\s*(?:tất\s*cả|toàn\s*bộ|hết)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "select_all";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"bỏ\s*(?:chọn|tích)\s*(?:tất\s*cả|toàn\s*bộ|hết)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "clear_selection";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:tích|chọn)\s*dòng\s*(?:này|hiện\s*tại)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "toggle_select_current";
            }

            // 10. Trích xuất Sao chép dòng trên (Phím C)
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"như\s*dòng\s*trên|sao\s*chép\s*dòng\s*trên|giống\s*dòng\s*trên", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "copy_above";
            }

            // 11. Trích xuất Thao tác hàng loạt: Áp cho các dòng đã chọn
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"áp\s*(?:cho\s*)?(?:các\s*)?dòng\s*đã\s*(?:chọn|tích)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "apply_selected";
            }

            // 12. Trích xuất Đồng bộ kho (SyncToArchive), Hoàn tác (Undo/Redo), Xuất file
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"đồng\s*bộ\s*(?:vào\s*)?kho|nạp\s*vào\s*kho|lưu\s*vào\s*kho", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "sync_to_archive";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bhoàn\s*tác\b|\bundo\b|lùi\s*thao\s*tác", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "undo";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\blàm\s*lại\b|\bredo\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "redo";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"bỏ\s*(?:toàn\s*bộ\s*)?thay\s*đổi|hủy\s*(?:toàn\s*bộ\s*)?thay\s*đổi|hủy\s*sửa", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "revert_edits";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"xuất\s*(?:file|excel|bản\s*lọc)|tải\s*(?:file|excel)\s*về", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "export_file";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"xem\s*trước\s*(?:rồi\s*xuất|khi\s*xuất|bản\s*in|in)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "preview_export";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"đóng\s*(?:hộp\s*thoại|cửa\s*sổ|bảng|modal|dòng|sửa|soạn)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "close_modal";
            }

            // 13. Trích xuất Bộ lọc thiếu
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:chỉ\s*xem|lọc)\s*(?:dòng\s*)?thiếu\s*số\s*tờ", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "filter_missing_sheet";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:chỉ\s*xem|lọc)\s*(?:dòng\s*)?thiếu\s*(?:hồ\s*sơ\s*số|số\s*hồ\s*sơ|số\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "filter_missing_doc";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:chỉ\s*xem|lọc)\s*(?:dòng\s*)?thiếu\s*(?:giấy\s*tờ|danh\s*mục|trích\s*yếu)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "filter_missing_items";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:dòng\s*)?thiếu\s*số\s*tờ\s*lên\s*trước|ưu\s*tiên\s*thiếu\s*số\s*tờ", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "missing_sheet_first";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:chỉ\s*xem\s*)?dòng\s*(?:đã\s*sửa|có\s*thay\s*đổi)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "filter_existing";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"bỏ\s*lọc\s*thiếu|bỏ\s*hết\s*lọc\s*thiếu", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = "clear_filter_missing";
            }

            // 14. Trích xuất Tìm kiếm (Search)
            string? search = null;
            var searchMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:tìm\s*kiếm|tìm)\s+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (searchMatch.Success)
            {
                var sVal = searchMatch.Groups[1].Value.Trim();
                if (!sVal.StartsWith("hồ sơ", StringComparison.OrdinalIgnoreCase) &&
                    !sVal.StartsWith("số", StringComparison.OrdinalIgnoreCase) &&
                    !sVal.StartsWith("dòng", StringComparison.OrdinalIgnoreCase) &&
                    !sVal.StartsWith("năm", StringComparison.OrdinalIgnoreCase))
                {
                    search = sVal;
                }
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"xóa\s*tìm\s*kiếm|bỏ\s*tìm\s*kiếm", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                search = "";
            }

            int? year = null;
            if (root.TryGetProperty("year", out var yrElem) && yrElem.ValueKind == JsonValueKind.Number && yrElem.TryGetInt32(out var yrVal))
            {
                year = yrVal;
            }

            string? type = null;
            if (root.TryGetProperty("type", out var typeElem) && typeElem.ValueKind == JsonValueKind.String)
            {
                var val = typeElem.GetString()?.Trim();
                if (!string.IsNullOrEmpty(val)) type = val;
            }

            int? numberFrom = null;
            if (root.TryGetProperty("numberFrom", out var nfElem) && nfElem.ValueKind == JsonValueKind.Number && nfElem.TryGetInt32(out var nfVal))
            {
                numberFrom = nfVal;
            }

            int? numberTo = null;
            if (root.TryGetProperty("numberTo", out var ntElem) && ntElem.ValueKind == JsonValueKind.Number && ntElem.TryGetInt32(out var ntVal))
            {
                numberTo = ntVal;
            }

            // Lệnh bỏ lọc loại / bỏ lọc năm / bỏ lọc số
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"bỏ\s*lọc\s*năm|tất\s*cả\s*(?:các\s*)?năm", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                year = -1; // -1 ký hiệu xóa bộ lọc năm
                command = null;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"bỏ\s*lọc\s*loại|tất\s*cả\s*(?:các\s*)?loại", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                type = ""; // chuỗi rỗng ký hiệu xóa bộ lọc loại
                command = null;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"bỏ\s*lọc\s*số|tất\s*cả\s*(?:các\s*)?số", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                numberFrom = -1; // -1 ký hiệu xóa bộ lọc số
                numberTo = -1;
                command = null;
            }

            // Lọc dải số: "từ số 5192 đến 5204", "từ 5192 đến 5204", "hồ sơ từ 5192 đến 5204"
            var rangeMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:từ\s*(?:số\s*)?|hồ\s*sơ\s*từ\s*)(\d+)\s*(?:đến|tới|-)\s*(?:số\s*)?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (rangeMatch.Success)
            {
                if (int.TryParse(rangeMatch.Groups[1].Value, out var fVal)) numberFrom = fVal;
                if (int.TryParse(rangeMatch.Groups[2].Value, out var tVal)) numberTo = tVal;
                // Khi đã có dải lọc từ... đến..., câu nói này là lệnh lọc bảng, không phải điền vào 1 hồ sơ đích hay áp hàng loạt
                targetDocumentNumber = null;
                targetRowNumber = null;
                command = null;
            }
            else
            {
                var singleFilterMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:chỉ\s*lọc|lọc\s*số|chỉ\s*xem\s*số)\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (singleFilterMatch.Success && int.TryParse(singleFilterMatch.Groups[1].Value, out var sFilterVal))
                {
                    numberFrom = sFilterVal;
                    numberTo = sFilterVal;
                    targetDocumentNumber = null;
                    targetRowNumber = null;
                    command = null;
                }
            }

            // Chống nhầm command = apply_missing khi người dùng không hề nói "áp"
            if (command == "apply_missing" && !System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(áp|app|gán|điền)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                command = null;
            }

            // Năm: Chỉ nhận diện khi câu nói thực sự có chữ "năm" + 4 chữ số (ví dụ: "năm 2000", "năm 1998")
            var yrMatch = System.Text.RegularExpressions.Regex.Match(text, @"năm\s*(\d{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (yrMatch.Success && int.TryParse(yrMatch.Groups[1].Value, out var explicitYear))
            {
                year = explicitYear;
            }
            else if (year != -1 && !System.Text.RegularExpressions.Regex.IsMatch(text, @"\bnăm\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                // Nếu không có chữ "năm" trong câu thoại, tuyệt đối không tự bịa năm (tránh LLM ảo giác ra 1994)
                year = null;
            }

            // 15. Bóc tách bổ sung danh mục giấy tờ từ câu thoại
            foreach (var (pattern, standardName) in KnownPaperPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    var targetName = standardName;
                    if (request.Catalog != null)
                    {
                        var match = request.Catalog.FirstOrDefault(c => string.Equals(c, standardName, StringComparison.OrdinalIgnoreCase));
                        if (match != null) targetName = match;
                    }
                    if (!items.Any(it => string.Equals(it, targetName, StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(targetName);
                    }
                }
            }

            string? reply = null;
            if (root.TryGetProperty("reply", out var repElem) && repElem.ValueKind == JsonValueKind.String)
            {
                var rStr = repElem.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(rStr))
                {
                    reply = rStr;
                }
            }

            bool needsConfirmation = false;
            string? confirmationPrompt = null;

            if (command == "apply_missing")
            {
                needsConfirmation = true;
                if (items.Count > 0)
                {
                    confirmationPrompt = $"Tôi nghe bạn muốn áp {items.Count} loại giấy tờ ({string.Join(", ", items)}) cho tất cả các dòng lọc còn thiếu. Bạn có đồng ý áp dụng không?";
                    reply = confirmationPrompt;
                }
                else
                {
                    confirmationPrompt = "Bạn có muốn áp bộ 7 giấy tờ chuẩn cho tất cả các dòng lọc còn thiếu không?";
                    reply = confirmationPrompt;
                }
            }
            else if (command == "clear_items" && targetRowNumber == null && targetDocumentNumber == null)
            {
                needsConfirmation = true;
                confirmationPrompt = "Bạn có chắc chắn muốn xóa hết giấy tờ không?";
                reply = confirmationPrompt;
            }
            else if (command == "export_file")
            {
                reply = "Đang xuất file Excel bản lọc.";
            }
            else if (command == "preview_export")
            {
                reply = "Đã mở xem trước bản xuất.";
            }
            else if (command == "sync_to_archive")
            {
                reply = "Đang tiến hành đồng bộ dữ liệu vào Kho hồ sơ.";
            }
            else if (command == "undo")
            {
                reply = "Đã hoàn tác thao tác.";
            }
            else if (command == "redo")
            {
                reply = "Đã làm lại thao tác.";
            }
            else if (command == "revert_edits")
            {
                reply = "Đã hủy các thay đổi chưa lưu.";
            }
            else if (string.IsNullOrWhiteSpace(reply))
            {
                if (targetDocumentNumber != null)
                {
                    reply = items.Count > 0
                        ? $"Đã thêm {string.Join(", ", items)} vào hồ sơ {targetDocumentNumber}."
                        : (sheetNumber != null ? $"Hồ sơ {targetDocumentNumber}: số tờ {sheetNumber}." : $"Đã chọn hồ sơ {targetDocumentNumber}.");
                }
                else if (command == "apply_fast")
                {
                    reply = targetRowNumber != null
                        ? $"Đã áp nhanh bộ 7 giấy tờ chuẩn vào dòng {targetRowNumber}."
                        : (targetDocumentNumber != null ? $"Đã áp nhanh bộ 7 giấy tờ chuẩn vào hồ sơ {targetDocumentNumber}." : "Đã áp nhanh bộ 7 giấy tờ chuẩn vào dòng đang xử lý.");
                }
                else if (command == "select_row")
                {
                    reply = targetRowNumber != null
                        ? $"Đã chọn và mở dòng {targetRowNumber}."
                        : (targetDocumentNumber != null ? $"Đã chọn hồ sơ {targetDocumentNumber}." : "Đã chọn dòng.");
                }
                else if (command == "copy_above")
                {
                    reply = "Đã sao chép như dòng trên.";
                }
                else if (command == "select_all")
                {
                    reply = "Đã tích chọn tất cả dòng trên trang.";
                }
                else if (command == "clear_selection")
                {
                    reply = "Đã bỏ chọn các dòng.";
                }
                else if (command == "next_page")
                {
                    reply = "Đã chuyển sang trang kế tiếp.";
                }
                else if (command == "prev_page")
                {
                    reply = "Đã lùi về trang trước.";
                }
                else if (command == "first_page")
                {
                    reply = "Đã quay về trang đầu tiên.";
                }
                else if (command == "last_page")
                {
                    reply = "Đã chuyển tới trang cuối cùng.";
                }
                else if (command == "goto_page" && targetPage != null)
                {
                    reply = $"Đã chuyển tới trang {targetPage}.";
                }
                else if (numberFrom != null && numberTo != null && numberFrom > 0)
                {
                    reply = $"Đã lọc dải hồ sơ từ số {numberFrom} đến {numberTo}.";
                }
                else if (year != null && year > 0)
                {
                    reply = $"Đã lọc các hồ sơ năm {year}.";
                }
                else if (items.Count > 0)
                {
                    reply = $"Đã nhận diện: {string.Join(", ", items)}.";
                }
                else
                {
                    reply = "Tôi đã nghe rõ câu nói của bạn.";
                }
            }

            return Ok(new SummaryAiParseVoiceResponse(
                true,
                items,
                command,
                sheetNumber,
                targetRowNumber,
                year,
                type,
                numberFrom,
                numberTo,
                model,
                TargetDocumentNumber: targetDocumentNumber,
                Search: search,
                TargetPage: targetPage,
                Reply: reply,
                NeedsConfirmation: needsConfirmation,
                ConfirmationPrompt: confirmationPrompt));
        }
        catch (Exception ex)
        {
            // Tận dụng SemanticCatalogMatcher thông minh để bóc tách giấy tờ không phụ thuộc regex cứng
            var activeCatalog = request.Catalog ?? new List<string>
            {
                "giấy khai sinh", "bộ công hàm", "hộ khẩu", "chứng chỉ chuyên khoa",
                "CMND", "phiếu nhập xuất cảnh", "hộ chiếu", "thị thực",
                "tờ khai đăng ký kết hôn", "đơn xin xác nhận độc thân và hôn nhân thực tế",
                "tờ trình", "chứng nhận đăng ký khai sinh", "đơn xin xác nhận thường trú",
                "bản tuyên thệ", "chứng nhận kết hôn", "biên lai thu phí và lệ phí",
                "phiếu khám sức khỏe", "giấy cam kết", "giấy xác nhận", "phiếu đề xuất", "sơ yếu lý lịch", "ảnh"
            };

            var fallbackItems = SemanticCatalogMatcher.MatchItems(text, activeCatalog);

            string? fallbackCommand = null;
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"áp\s*nhanh|sử\s*dụng\s*áp\s*nhanh|áp\s*(?:toàn\s*bộ\s*)?(?:các\s*)?dòng\s*lọc|áp\s*dòng\s*lọc|áp\s*(?:vào|cho)?\s*(?:các)?\s*dòng\s*(?:lọc\s*)?(?:còn\s*)?thiếu|áp\s*dòng\s*thiếu|áp\s*hàng\s*loạt|áp\s*bộ\s*7\s*(?:chuẩn)?|áp\s*bộ\s*chuẩn|điền\s*(?:các\s*)?dòng\s*(?:còn\s*)?thiếu|gán\s*(?:cho\s*)?dòng\s*thiếu|áp\s*(?:nhanh\s*)?(?:cho\s*)?giấy\s*tờ|áp\s*(?:nhanh\s*)?các\s*giấy\s*tờ|áp\s*(?:nhanh\s*)?(?:cho\s*)?toàn\s*bộ", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                fallbackCommand = "apply_missing";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"bỏ\s*bộ\s*7|xóa\s*bộ\s*7|hủy\s*bộ\s*7|bỏ\s*bộ\s*bảy", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                fallbackCommand = "clear_seven";
            }

            if (fallbackItems.Count > 0 || fallbackCommand != null)
            {
                var prompt = fallbackItems.Count > 0
                    ? $"Tôi nghe bạn muốn áp {fallbackItems.Count} loại giấy tờ ({string.Join(", ", fallbackItems)}) cho tất cả các dòng lọc còn thiếu. Bạn có đồng ý áp dụng không?"
                    : (fallbackCommand == "clear_seven" ? "Đã bỏ bộ 7 giấy tờ." : "Bạn có muốn áp bộ 7 giấy tờ chuẩn cho tất cả các dòng lọc còn thiếu không?");

                return Ok(new SummaryAiParseVoiceResponse(
                    true,
                    fallbackItems,
                    fallbackCommand,
                    Model: "semantic-matcher",
                    NeedsConfirmation: fallbackCommand == "apply_missing",
                    ConfirmationPrompt: fallbackCommand == "apply_missing" ? prompt : null,
                    Reply: prompt));
            }

            return Ok(new SummaryAiParseVoiceResponse(false, Array.Empty<string>(), Model: model, Error: ex is OperationCanceledException ? "Quá thời gian chờ phản hồi AI (timeout)" : ex.Message));
        }
    }

    /// <summary>Lấy danh sách các tương tác giọng nói đã học được từ phòng.</summary>
    [HttpGet("ai/memories")]
    public async Task<ActionResult<IReadOnlyList<VoiceMemoryDto>>> GetVoiceMemories(CancellationToken cancellationToken)
    {
        using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await EnsureDefaultMemoriesSeededAsync(db, cancellationToken);

        var list = await db.VoiceMemories
            .AsNoTracking()
            .OrderByDescending(m => m.UsageCount)
            .ThenByDescending(m => m.LastUsedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var dtos = list.Select(m => new VoiceMemoryDto(
            m.Id,
            m.RawSpeech,
            m.CleanSpeech,
            m.Command,
            JsonSerializer.Deserialize<List<string>>(m.ItemsJson) ?? new List<string>(),
            m.SheetNumber,
            m.TargetRowNumber,
            m.TargetDocumentNumber,
            m.Actor,
            m.UsageCount,
            m.CreatedAt,
            m.LastUsedAt
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>Lưu hoặc học một mẫu câu tương tác giọng nói mới.</summary>
    [HttpPost("ai/memories")]
    public async Task<ActionResult<VoiceMemoryDto>> SaveVoiceMemory(
        [FromBody] VoiceMemorySaveRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RawSpeech))
        {
            return BadRequest("RawSpeech không được để trống.");
        }

        using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var clean = SemanticCatalogMatcher.NormalizeForMatching(request.RawSpeech);
        var actor = (await StpTeamIdentity.FromAsync(HttpContext))?.DisplayName ?? "Đồng nghiệp";

        var existing = await db.VoiceMemories.FirstOrDefaultAsync(m => m.CleanSpeech == clean, cancellationToken);
        var itemsJson = JsonSerializer.Serialize(request.Items ?? (IReadOnlyList<string>)Array.Empty<string>());

        if (existing != null)
        {
            existing.UsageCount++;
            existing.Command = request.Command ?? existing.Command;
            existing.ItemsJson = itemsJson;
            existing.SheetNumber = request.SheetNumber ?? existing.SheetNumber;
            existing.TargetRowNumber = request.TargetRowNumber ?? existing.TargetRowNumber;
            existing.TargetDocumentNumber = request.TargetDocumentNumber ?? existing.TargetDocumentNumber;
            existing.Actor = actor;
            existing.LastUsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new VoiceMemoryDto(
                existing.Id,
                existing.RawSpeech,
                existing.CleanSpeech,
                existing.Command,
                request.Items ?? Array.Empty<string>(),
                existing.SheetNumber,
                existing.TargetRowNumber,
                existing.TargetDocumentNumber,
                existing.Actor,
                existing.UsageCount,
                existing.CreatedAt,
                existing.LastUsedAt));
        }

        var newMem = new VoiceMemory
        {
            RawSpeech = request.RawSpeech.Trim(),
            CleanSpeech = clean,
            Command = request.Command ?? string.Empty,
            ItemsJson = itemsJson,
            SheetNumber = request.SheetNumber,
            TargetRowNumber = request.TargetRowNumber,
            TargetDocumentNumber = request.TargetDocumentNumber,
            Actor = actor,
            UsageCount = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow
        };

        db.VoiceMemories.Add(newMem);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new VoiceMemoryDto(
            newMem.Id,
            newMem.RawSpeech,
            newMem.CleanSpeech,
            newMem.Command,
            request.Items ?? Array.Empty<string>(),
            newMem.SheetNumber,
            newMem.TargetRowNumber,
            newMem.TargetDocumentNumber,
            newMem.Actor,
            newMem.UsageCount,
            newMem.CreatedAt,
            newMem.LastUsedAt));
    }

    private static async Task EnsureDefaultMemoriesSeededAsync(StpDbContext db, CancellationToken cancellationToken)
    {
        if (await db.VoiceMemories.AnyAsync(cancellationToken)) return;

        var defaults = new List<VoiceMemory>
        {
            new()
            {
                RawSpeech = "app dành cho giấy tờ bao gồm giấy chứng nhận thức hồn tờ khai kết hôn chứng minh nhân dân bản sao sổ hộ khẩu sổ hộ khẩu gia đình",
                CleanSpeech = "ap nhanh cho giay to bao gom giay chung nhan ket hon to khai ket hon chung minh nhan dan ban sao so ho khau so ho khau",
                Command = "apply_missing",
                ItemsJson = JsonSerializer.Serialize(new[]
                {
                    "Giấy chứng nhận kết hôn",
                    "Tờ khai đăng ký kết hôn",
                    "Chứng minh nhân dân",
                    "Bản sao sổ hộ khẩu",
                    "Sổ hộ khẩu"
                }),
                Actor = "Hệ thống",
                UsageCount = 10,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                RawSpeech = "bỏ bộ 7 đi",
                CleanSpeech = "bo bo 7 di",
                Command = "clear_seven",
                ItemsJson = "[]",
                Actor = "Hệ thống",
                UsageCount = 8,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                RawSpeech = "5127 và 5128",
                CleanSpeech = "5127 va 5128",
                Command = "filter",
                ItemsJson = "[]",
                Actor = "Hệ thống",
                UsageCount = 8,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                RawSpeech = "áp nhanh toàn bộ các dòng lọc còn thiếu",
                CleanSpeech = "ap nhanh toan bo cac dong loc con thieu",
                Command = "apply_missing",
                ItemsJson = "[]",
                Actor = "Hệ thống",
                UsageCount = 7,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow
            }
        };

        db.VoiceMemories.AddRange(defaults);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch {}
    }

    private sealed record OllamaVoiceGenerateResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("response")] string? Response);

    private static readonly (string Pattern, string StandardName)[] KnownPaperPatterns = new[]
    {
        (@"giấy\s*khai\s*sinh|khai\s*sinh\b", "giấy khai sinh"),
        (@"bản\s*công\s*hàm|bộ\s*công\s*hàm|công\s*hàm\b", "bộ công hàm"),
        (@"sổ\s*hộ\s*khẩu|hộ\s*khẩu\b", "hộ khẩu"),
        (@"phiếu\s*đề\s*xuất|đề\s*xuất\b", "phiếu đề xuất"),
        (@"chứng\s*chỉ\s*chuyên\s*khoa|chuyên\s*khoa\b", "chứng chỉ chuyên khoa"),
        (@"phiếu\s*khám\s*sức\s*khỏe|giấy\s*khám\s*sức\s*khỏe|khám\s*sức\s*khỏe|sức\s*khỏe\b", "phiếu khám sức khỏe"),
        (@"chứng\s*minh\s*nhân\s*dân|cmnd\b|căn\s*cước|cccd\b", "CMND"),
        (@"phiếu\s*(?:nhập\s*xuất|xuất\s*nhập)\s*cảnh|(?:nhập\s*xuất|xuất\s*nhập)\s*cảnh|nxc\b|xnc\b", "phiếu nhập xuất cảnh"),
        (@"hộ\s*chiếu|passport\b", "hộ chiếu"),
        (@"thị\s*thực|visa\b", "thị thực"),
        (@"tờ\s*khai\s*(?:đăng\s*ký\s*)?kết\s*hôn|tờ\s*khai\b", "tờ khai đăng ký kết hôn"),
        (@"đơn\s*xin\s*xác\s*nhận\s*độc\s*thân\s*và\s*hôn\s*nhân\s*thực\s*tế|xác\s*nhận\s*độc\s*thân\s*và\s*hôn\s*nhân\s*thực\s*tế|độc\s*thân\s*và\s*hôn\s*nhân\s*thực\s*tế|hôn\s*nhân\s*thực\s*tế|hntt\b|giấy\s*xác\s*nhận\s*tình\s*trạng\s*hôn\s*nhân|xác\s*nhận\s*hôn\s*nhân|độc\s*thân\b", "đơn xin xác nhận độc thân và hôn nhân thực tế"),
        (@"tờ\s*trình\b", "tờ trình"),
        (@"chứng\s*nhận\s*đăng\s*ký\s*khai\s*sinh", "chứng nhận đăng ký khai sinh"),
        (@"đơn\s*xin\s*xác\s*nhận\s*thường\s*trú|xác\s*nhận\s*thường\s*trú|thường\s*trú\b", "đơn xin xác nhận thường trú"),
        (@"bản\s*tuyên\s*thệ|tuyên\s*thệ\b", "bản tuyên thệ"),
        (@"chứng\s*nhận\s*kết\s*hôn", "chứng nhận kết hôn"),
        (@"biên\s*lai\s*(?:thu\s*)?(?:phí|lệ\s*phí)|lệ\s*phí\b", "biên lai thu phí và lệ phí"),
        (@"giấy\s*cam\s*kết|cam\s*kết\b", "giấy cam kết"),
        (@"giấy\s*xác\s*nhận|xác\s*nhận\b", "giấy xác nhận"),
        (@"giấy\s*chứng\s*nhận\s*kết\s*hôn|chứng\s*nhận\s*kết\s*hôn", "giấy chứng nhận kết hôn"),
        (@"sơ\s*yếu\s*lý\s*lịch", "sơ yếu lý lịch"),
        (@"bản\s*sao\s*trích\s*lục\s*khai\s*sinh|trích\s*lục\s*khai\s*sinh", "bản sao trích lục khai sinh"),
        (@"đơn\s*xin\s*đăng\s*ký\s*kết\s*hôn|đơn\s*kết\s*hôn", "đơn xin đăng ký kết hôn"),
        (@"\bảnh\b", "ảnh"),
    };

    private static string PreprocessSpokenText(string text, IReadOnlyList<SummaryVoiceLearnedPair>? learned)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var s = text;

        if (learned != null && learned.Count > 0)
        {
            foreach (var pair in learned)
            {
                if (!string.IsNullOrWhiteSpace(pair.Heard) && !string.IsNullOrWhiteSpace(pair.Fixed))
                {
                    s = System.Text.RegularExpressions.Regex.Replace(s, System.Text.RegularExpressions.Regex.Escape(pair.Heard), pair.Fixed, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
        }

        // 1. Áp nhanh / app dành / áp dành / áp ranh
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(?:app|ap|ep)\s*(?:dành|ranh|nhanh)\b", "áp nhanh", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(?:áp|app)\s*dành\b", "áp nhanh", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\báp\s*ranh\b", "áp nhanh", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\báp\s*dành\s*cho\b", "áp nhanh cho", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bapp\s*dành\s*cho\b", "áp nhanh cho", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 2. Kết hôn / Thức hồn
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bgiấy\s*chứng\s*nhận\s*(?:thức|thực)\s*(?:hồn|hôn)\b", "giấy chứng nhận kết hôn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bchứng\s*nhận\s*(?:thức|thực)\s*(?:hồn|hôn)\b", "giấy chứng nhận kết hôn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(?:thức|thực)\s*(?:hồn|hôn)\b", "kết hôn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bkết\s*hon\b", "kết hôn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bhết\s*hôn\b", "kết hôn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 3. Giấy tờ phổ biến
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bbộ\s*công\s*an\b", "bộ công hàm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bbản\s*sao\s*sổ\s*hộ\s*khẩu\b", "sổ hộ khẩu", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bsổ\s*hộ\s*khẩu\s*gia\s*đình\b", "sổ hộ khẩu", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\btờ\s*khai\s*kết\s*hôn\b", "tờ khai đăng ký kết hôn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bchứng\s*minh\s*nhân\s*dân\b", "chứng minh nhân dân", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bcăn\s*cước\s*công\s*dân\b", "căn cước công dân", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return s;
    }

    /// <summary>Phát âm giọng đọc tiếng Việt chuẩn (Vietnamese TTS stream) từ dịch vụ TTS Local.</summary>
    [HttpGet("ai/tts")]
    public async Task<IActionResult> GetTts([FromQuery] string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Text is required");
        }

        var whisperBase = _configuration["Whisper:BaseUrl"] ?? "http://127.0.0.1:5216";
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.GetAsync($"{whisperBase}/tts?text={Uri.EscapeDataString(text)}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return File(stream, "audio/mpeg");
            }
        }
        catch (Exception)
        {
            // Bỏ qua lỗi kết nối TTS
        }

        return StatusCode(503, "TTS service currently unavailable");
    }

    /// <summary>Nhận diện âm thanh từ micro bằng mô hình Whisper Large-v3-Turbo Local.</summary>
    [HttpPost("ai/whisper-transcribe")]
    public async Task<ActionResult<SummaryWhisperTranscribeResponse>> WhisperTranscribe(
        [FromForm] IFormFile? file,
        [FromForm] string? prompt,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return Ok(new SummaryWhisperTranscribeResponse(false, "", Error: "Tệp âm thanh rỗng"));
        }

        var whisperBase = _configuration["Whisper:BaseUrl"] ?? "http://127.0.0.1:5216";
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var content = new MultipartFormDataContent();

            using var fileStream = file.OpenReadStream();
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "audio/webm");
            content.Add(streamContent, "file", file.FileName ?? "recording.webm");
            content.Add(new StringContent("vi"), "language");
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                content.Add(new StringContent(prompt), "initial_prompt");
            }

            var resp = await client.PostAsync($"{whisperBase}/transcribe", content, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
                var root = doc.RootElement;
                var text = root.TryGetProperty("text", out var tProp) ? tProp.GetString() ?? "" : "";
                var lang = root.TryGetProperty("language", out var lProp) ? lProp.GetString() : "vi";
                var dur = root.TryGetProperty("duration", out var dProp) && dProp.TryGetDouble(out var dVal) ? dVal : 0;
                return Ok(new SummaryWhisperTranscribeResponse(true, text, lang, dur));
            }

            return Ok(new SummaryWhisperTranscribeResponse(false, "", Error: $"Whisper trả về mã lỗi: {resp.StatusCode}"));
        }
        catch (Exception ex)
        {
            return Ok(new SummaryWhisperTranscribeResponse(false, "", Error: ex.Message));
        }
    }

    private ObjectResult InvalidProblem(string detail) =>
        BadRequest(new ProblemDetails
        {
            Title = "Dữ liệu chưa hợp lệ",
            Detail = detail,
            Status = StatusCodes.Status400BadRequest
        });

    private ObjectResult NotFoundProblem(string detail) =>
        NotFound(new ProblemDetails
        {
            Title = "Không tìm thấy phiên làm việc",
            Detail = detail,
            Status = StatusCodes.Status404NotFound
        });

    private static SummaryInspectResponse ToResponse(SummaryWorkbook workbook) =>
        new(
            workbook.SessionId,
            workbook.FileName,
            workbook.FileSizeBytes,
            workbook.SheetName,
            workbook.SummaryColumn,
            workbook.SummaryHeader,
            workbook.SheetNumberColumn,
            workbook.SheetNumberHeader,
            workbook.DocumentNumberColumn,
            workbook.Rows.Count,
            workbook.ExistingListCount,
            workbook.BrokenSummaryCount,
            workbook.FormulaSummaryCount,
            workbook.MissingSheetNumberCount,
            workbook.MissingDocumentNumberCount,
            workbook.MissingNumberCount,
            workbook.NormalizableCount,
            workbook.Catalog,
            workbook.Years
                .Select(item => new SummaryYearResponse(item.Year, item.Count))
                .ToList(),
            workbook.Types
                .Select(item => new SummaryTypeResponse(item.Name, item.Count))
                .ToList(),
            workbook.NumberColumn,
            workbook.CatalogMeta
                .Select(item => new SummaryCatalogMetaResponse(item.Name, item.UsageCount, item.Source))
                .ToList(),
            workbook.Sheets
                .Select(item => new SummarySheetResponse(item.Name, item.Year, item.RowCount))
                .ToList());

    private static SummaryPreviewRowResponse ToResponse(SummaryPreviewRow row) =>
        new(
            row.RowNumber,
            row.Summary,
            row.Changed,
            row.OriginalSummary,
            row.SheetNumber,
            row.OriginalSheetNumber,
            row.SummaryChanged,
            row.SheetNumberChanged,
            row.DocumentNumber,
            row.OriginalDocumentNumber,
            row.DocumentNumberChanged,
            row.NumberLabel,
            row.OriginalNumberLabel,
            row.NumberLabelChanged,
            row.SheetName,
            row.SourceRowNumber);

    private static SummaryRowResponse ToResponse(SummaryRow row) =>
        new(
            row.RowNumber,
            row.Year,
            row.NumberLabel,
            row.DocumentNumber,
            row.SheetNumber,
            row.OriginalSummary,
            row.BaseSummary,
            row.Items,
            row.HasDocumentList,
            row.HasFormula,
            row.IsBrokenSummary,
            row.Group,
            row.Type,
            row.NeedsNormalization,
            row.NormalizedSummary,
            row.NormalizedBaseSummary,
            row.SheetName,
            row.SourceRowNumber);
}
