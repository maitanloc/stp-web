using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Models;
using Stp.Api.Services;
using Stp.Contracts;
using Stp.Core.Storage;
using Stp.Core.Sync;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private const long UploadRequestLimit = 420L * 1024 * 1024;

    private readonly IArchiveSyncService _sync;
    private readonly IFileStorage _storage;
    private readonly StpDbContext _db;
    private readonly TeamAudit _audit;

    public SyncController(
        IArchiveSyncService sync,
        IFileStorage storage,
        StpDbContext db,
        TeamAudit audit)
    {
        _sync = sync;
        _storage = storage;
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Nhận workbook Excel đã chỉnh sửa và đồng bộ ngược vào SQL Server.
    /// Sheet ML là nguồn dữ liệu chính.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(UploadRequestLimit)]
    public async Task<ActionResult<SyncResponse>> Upload(
        [FromForm] SyncUploadForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Chưa chọn file",
                Detail = "Cần chọn một workbook .xlsx/.xlsm để đồng bộ.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var uploadId = Guid.NewGuid();
        StoredFile stored;
        try
        {
            await using var stream = form.File.OpenReadStream();
            stored = await _storage.SaveUploadAsync(
                stream,
                form.File.FileName,
                uploadId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException
                                       or NotSupportedException
                                       or IOException)
        {
            _storage.DeleteDirectory(_storage.GetJobUploadDirectory(uploadId));
            return BadRequest(new ProblemDetails
            {
                Title = "Không nhận được file",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        var outcome = await _sync.ApplyAsync(new SyncRequest
        {
            FilePath = stored.FullPath,
            SourceFileName = stored.FileName,
            WorkbookId = form.WorkbookId,
            BaseVersion = form.BaseVersion
        }, cancellationToken);

        var response = new SyncResponse(
            outcome.Success,
            outcome.SyncSessionId,
            outcome.WorkbookId,
            outcome.AddedCount,
            outcome.UpdatedCount,
            outcome.DeletedCount,
            outcome.ConflictCount,
            outcome.ErrorMessage);

        if (!outcome.Success)
        {
            return UnprocessableEntity(response);
        }

        _audit.Write(
            TeamAudit.ActorFromHeader(Request),
            "sync",
            "SyncSession",
            outcome.SyncSessionId.ToString(),
            $"{stored.FileName}: thêm {outcome.AddedCount}, sửa {outcome.UpdatedCount}, xoá {outcome.DeletedCount}, xung đột {outcome.ConflictCount}");

        return Ok(response);
    }

    /// <summary>Các lần đồng bộ gần đây.</summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SyncSessionSummaryResponse>>> Sessions(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);

        var sessions = await _db.SyncSessions
            .AsNoTracking()
            .OrderByDescending(session => session.UploadedAt)
            .Take(take)
            .Select(session => new SyncSessionSummaryResponse(
                session.Id,
                session.WorkbookId,
                session.SourceFileName,
                session.UploadedAt,
                session.Status.ToString(),
                session.AddedCount,
                session.UpdatedCount,
                session.DeletedCount,
                session.ConflictCount,
                session.ErrorMessage))
            .ToListAsync(cancellationToken);

        return Ok(sessions);
    }

    /// <summary>Chi tiết một lần đồng bộ kèm danh sách thay đổi.</summary>
    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<SyncSessionDetailResponse>> Session(
        Guid id,
        CancellationToken cancellationToken)
    {
        var session = await _db.SyncSessions
            .AsNoTracking()
            .Include(entity => entity.Changes)
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        return Ok(new SyncSessionDetailResponse(
            new SyncSessionSummaryResponse(
                session.Id,
                session.WorkbookId,
                session.SourceFileName,
                session.UploadedAt,
                session.Status.ToString(),
                session.AddedCount,
                session.UpdatedCount,
                session.DeletedCount,
                session.ConflictCount,
                session.ErrorMessage),
            session.Changes
                .OrderBy(change => change.EntityKey)
                .Select(change => new SyncChangeResponse(
                    change.Id,
                    change.EntityType,
                    change.EntityKey,
                    change.ChangeType,
                    change.IsConflict,
                    change.Resolution,
                    change.OldValueJson,
                    change.NewValueJson))
                .ToList()));
    }
}
