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

    public SummaryToolController(
        ISummaryToolService summary,
        IDbContextFactory<StpDbContext> factory,
        TeamAudit audit,
        IHubContext<TeamHub> hubContext,
        NotificationService notifications,
        IArchiveSyncService sync,
        IFileStorage storage)
    {
        _summary = summary;
        _factory = factory;
        _audit = audit;
        _hubContext = hubContext;
        _notifications = notifications;
        _sync = sync;
        _storage = storage;
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

            if (request.NumberFrom.HasValue
                && request.NumberTo.HasValue
                && request.NumberFrom.Value > request.NumberTo.Value)
            {
                error = "Số bắt đầu không được lớn hơn số kết thúc.";
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
