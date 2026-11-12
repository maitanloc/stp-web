using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Stp.Api.Hubs;
using Stp.Api.Services;
using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

/// <summary>
/// Ghi chú theo dòng trên bảng sống kho hồ sơ (Trụ D — J2): đọc toàn bộ
/// comment của một hồ sơ, thêm comment mới (broadcast hub cho cả nhóm thấy
/// bong bóng hiện ngay) — cực hợp cho "hồ sơ này thiếu số tờ, nhờ Lộc check".
/// </summary>
[ApiController]
[Route("api/dossiers/{dossierId:guid}/comments")]
public sealed class DossierCommentsController : ControllerBase
{
    private const int MaxBodyLength = 1000;

    private readonly StpDbContext _db;
    private readonly IHubContext<TeamHub> _hubContext;
    private readonly TeamAudit _audit;
    private readonly ILogger<DossierCommentsController> _logger;

    public DossierCommentsController(
        StpDbContext db,
        IHubContext<TeamHub> hubContext,
        TeamAudit audit,
        ILogger<DossierCommentsController> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Toàn bộ comment của một hồ sơ, cũ → mới.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DossierCommentResponse>>> List(
        Guid dossierId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Dossiers.AnyAsync(dossier => dossier.Id == dossierId, cancellationToken))
        {
            return NotFound();
        }

        var items = await _db.DossierComments
            .AsNoTracking()
            .Where(comment => comment.DossierId == dossierId)
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new DossierCommentResponse(
                comment.Id,
                comment.DossierId,
                comment.Actor,
                comment.Body,
                comment.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>Thêm comment cho hồ sơ — broadcast realtime cho cả nhóm.</summary>
    [HttpPost]
    public async Task<ActionResult<DossierCommentResponse>> Create(
        Guid dossierId,
        [FromBody] DossierCommentRequest request,
        CancellationToken cancellationToken)
    {
        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            return BadRequest(new { message = "Nội dung ghi chú đang trống." });
        }

        if (body.Length > MaxBodyLength)
        {
            body = body[..MaxBodyLength];
        }

        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "Không rõ" : request.Actor.Trim();
        if (actor.Length > 100)
        {
            actor = actor[..100];
        }

        var dossierExists = await _db.Dossiers
            .AnyAsync(dossier => dossier.Id == dossierId, cancellationToken);
        if (!dossierExists)
        {
            return NotFound();
        }

        var comment = new DossierComment
        {
            DossierId = dossierId,
            Actor = actor,
            Body = body,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.DossierComments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);

        var response = new DossierCommentResponse(
            comment.Id,
            comment.DossierId,
            comment.Actor,
            comment.Body,
            comment.CreatedAt);

        // Broadcast cho cả nhóm — lỗi hub không rớt hành động chính.
        try
        {
            _ = _hubContext.Clients.All.SendAsync("dossierComment", response);
        }
        catch (Exception caught)
        {
            _logger.LogWarning(caught, "Không broadcast được comment mới qua hub");
        }

        _audit.Write(
            actor,
            "dossier.comment",
            "Dossier",
            dossierId.ToString(),
            body.Length > 120 ? body[..120] + "…" : body);

        return Ok(response);
    }
}
