using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/workbooks")]
public sealed class WorkbooksController : ControllerBase
{
    private readonly StpDbContext _db;

    public WorkbooksController(StpDbContext db)
    {
        _db = db;
    }

    /// <summary>Danh sách workbook đã import hoặc đồng bộ.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkbookResponse>>> List(
        CancellationToken cancellationToken)
    {
        var items = await _db.ArchiveWorkbooks
            .AsNoTracking()
            .OrderByDescending(workbook => workbook.ImportedAt)
            .Select(workbook => new WorkbookResponse(
                workbook.Id,
                workbook.Name,
                workbook.SourceFileName,
                workbook.Kind.ToString(),
                workbook.ImportedAt,
                workbook.LastSyncedAt,
                workbook.Version,
                workbook.IsMaster,
                workbook.Notes,
                workbook.Years.Count,
                workbook.Years.Sum(year => year.Dossiers.Count)))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>Các năm của một workbook.</summary>
    [HttpGet("{id:guid}/years")]
    public async Task<ActionResult<IReadOnlyList<YearResponse>>> Years(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await _db.ArchiveWorkbooks.AnyAsync(workbook => workbook.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var years = await _db.ArchiveYears
            .AsNoTracking()
            .Where(year => year.WorkbookId == id)
            .OrderBy(year => year.Year)
            .Select(year => new YearResponse(
                year.Id,
                year.Year,
                year.SheetName,
                year.Dossiers.Count))
            .ToListAsync(cancellationToken);

        return Ok(years);
    }

    /// <summary>Danh sách loại hồ sơ có trong workbook, theo thứ tự hiển thị chuẩn.</summary>
    [HttpGet("{id:guid}/types")]
    public async Task<ActionResult<IReadOnlyList<string>>> Types(
        Guid id,
        CancellationToken cancellationToken)
    {
        var types = await _db.Dossiers
            .AsNoTracking()
            .Where(dossier => dossier.WorkbookId == id && dossier.Type != string.Empty)
            .Select(dossier => dossier.Type)
            .Distinct()
            .ToListAsync(cancellationToken);

        return Ok(types
            .OrderBy(DossierTypes.Order)
            .ThenBy(type => type, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    /// <summary>Tìm/lọc hồ sơ theo năm, loại và từ khoá.</summary>
    [HttpGet("{id:guid}/dossiers")]
    public async Task<ActionResult<PagedResponse<DossierSummaryResponse>>> Dossiers(
        Guid id,
        [FromQuery] int? year = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await _db.ArchiveWorkbooks.AnyAsync(workbook => workbook.Id == id, cancellationToken))
        {
            return NotFound();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Dossiers
            .AsNoTracking()
            .Where(dossier => dossier.WorkbookId == id);

        if (year.HasValue)
        {
            query = query.Where(dossier => dossier.Year.Year == year.Value);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(dossier => dossier.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(dossier =>
                dossier.NumberLabel.Contains(term)
                || dossier.OriginalSummary.Contains(term)
                || dossier.BoxNumber.Contains(term)
                || dossier.Documents.Any(line => line.TrichYeu.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(dossier => dossier.Year.Year)
            .ThenBy(dossier => dossier.Number)
            .ThenBy(dossier => dossier.MlRow)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(dossier => new DossierSummaryResponse(
                dossier.Id,
                dossier.Year.Year,
                dossier.MlRow,
                dossier.Number,
                dossier.NumberLabel,
                dossier.Type,
                dossier.Group,
                dossier.BoxNumber,
                dossier.OriginalSummary,
                dossier.HasPrintLayout,
                dossier.IsAggregate,
                dossier.TotalPages,
                dossier.Documents.Count))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<DossierSummaryResponse>(items, total, page, pageSize));
    }
}
