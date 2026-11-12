using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Mapping;
using Stp.Contracts;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/dossiers")]
public sealed class DossiersController : ControllerBase
{
    private readonly StpDbContext _db;

    public DossiersController(StpDbContext db)
    {
        _db = db;
    }

    /// <summary>Chi tiết một hồ sơ kèm danh sách văn bản.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DossierResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var dossier = await _db.Dossiers
            .AsNoTracking()
            .Include(entity => entity.Year)
            .Include(entity => entity.Documents)
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (dossier is null)
        {
            return NotFound();
        }

        return Ok(dossier.ToResponse());
    }

    /// <summary>
    /// Tìm nhanh hồ sơ cho thanh lệnh Ctrl+K (H2): khớp số hồ sơ hoặc trích
    /// yếu gốc. Tách từ khoá thành token — MỌI token phải xuất hiện (như
    /// Google), ưu tiên khớp số hồ sơ, mới nhất trước. Chỉ trả ít kết quả để
    /// palette hiện gọn.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<DossierSearchHitResponse>>> Search(
        [FromQuery] string? q = null,
        [FromQuery] int take = 8,
        CancellationToken cancellationToken = default)
    {
        var tokens = (q ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .Take(6)
            .ToArray();

        if (tokens.Length == 0)
        {
            return Ok(Array.Empty<DossierSearchHitResponse>());
        }

        take = Math.Clamp(take, 1, 20);

        var query = _db.Dossiers.AsNoTracking();
        foreach (var token in tokens)
        {
            var current = token;
            query = query.Where(dossier =>
                dossier.NumberLabel.Contains(current)
                || dossier.OriginalSummary.Contains(current)
                || (dossier.Note != null && dossier.Note.Contains(current)));
        }

        var hits = await query
            .OrderBy(dossier => tokens.All(token => dossier.NumberLabel.Contains(token)) ? 0 : 1)
            .ThenByDescending(dossier => dossier.Year.Year)
            .ThenBy(dossier => dossier.NumberLabel)
            .Take(take)
            .Select(dossier => new DossierSearchHitResponse(
                dossier.Id,
                dossier.Year.Year,
                dossier.NumberLabel,
                dossier.Type,
                dossier.OriginalSummary))
            .ToListAsync(cancellationToken);

        return Ok(hits);
    }
}
