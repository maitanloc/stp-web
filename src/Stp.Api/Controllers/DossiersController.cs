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
}
