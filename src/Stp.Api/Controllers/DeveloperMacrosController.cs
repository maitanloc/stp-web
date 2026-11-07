using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stp.Api.Mapping;
using Stp.Contracts;
using Stp.Core.Macros;

namespace Stp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dev/macros")]
public sealed class DeveloperMacrosController : ControllerBase
{
    private readonly IMacroService _macros;

    public DeveloperMacrosController(IMacroService macros)
    {
        _macros = macros;
    }

    /// <summary>Danh sách phiên bản macro, mới nhất trước.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MacroVersionSummaryResponse>>> List(
        CancellationToken cancellationToken)
    {
        var versions = await _macros.ListAsync(cancellationToken);
        return Ok(versions.Select(MacroMapping.ToSummary).ToList());
    }

    /// <summary>Phiên bản macro đang được dùng khi chuyển đổi.</summary>
    [HttpGet("active")]
    public async Task<ActionResult<MacroVersionDetailResponse>> Active(
        CancellationToken cancellationToken)
    {
        var active = await _macros.GetActiveAsync(cancellationToken);
        return Ok(active.ToResponse());
    }

    /// <summary>Chi tiết một phiên bản kèm toàn bộ mã nguồn.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MacroVersionDetailResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var version = await _macros.GetAsync(id, cancellationToken);
        return version is null ? NotFound() : Ok(version.ToResponse());
    }

    /// <summary>Lưu phiên bản macro mới; mặc định kích hoạt luôn.</summary>
    [HttpPost]
    public async Task<ActionResult<MacroVersionDetailResponse>> Save(
        [FromBody] MacroSaveVersionRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code ?? string.Empty;
        var validation = _macros.Validate(code);
        if (!validation.IsValid)
        {
            return UnprocessableEntity(validation.ToResponse());
        }

        try
        {
            var version = await _macros.SaveAsync(new MacroSaveRequest
            {
                Code = code,
                ChangeNote = request.ChangeNote,
                ParentVersionId = request.ParentVersionId,
                Activate = request.Activate,
                CreatedBy = User.Identity?.Name ?? "developer"
            }, cancellationToken);

            return CreatedAtAction(nameof(Get), new { id = version.Id }, version.ToResponse());
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Không lưu được macro",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>Kích hoạt một phiên bản đã có.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<MacroVersionDetailResponse>> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var version = await _macros.ActivateAsync(id, cancellationToken);
        return version is null ? NotFound() : Ok(version.ToResponse());
    }

    /// <summary>Kiểm tra tĩnh mã macro trước khi lưu.</summary>
    [HttpPost("validate")]
    public ActionResult<MacroValidationResponse> Validate([FromBody] MacroSaveVersionRequest request)
    {
        return Ok(_macros.Validate(request.Code ?? string.Empty).ToResponse());
    }

    /// <summary>
    /// So sánh phiên bản hiện tại với một phiên bản khác (mặc định là cha,
    /// nếu không có thì phiên bản liền trước).
    /// </summary>
    [HttpGet("{id:guid}/diff")]
    public async Task<ActionResult<MacroDiffResponse>> Diff(
        Guid id,
        [FromQuery] Guid? against,
        CancellationToken cancellationToken)
    {
        var current = await _macros.GetAsync(id, cancellationToken);
        if (current is null)
        {
            return NotFound();
        }

        Guid? oldId = against ?? current.ParentVersionId;
        MacroVersionDetail? old = null;

        if (oldId is not null)
        {
            old = await _macros.GetAsync(oldId.Value, cancellationToken);
        }

        if (old is null)
        {
            var versions = await _macros.ListAsync(cancellationToken);
            var previous = versions
                .Where(version => version.VersionNumber < current.VersionNumber)
                .OrderByDescending(version => version.VersionNumber)
                .FirstOrDefault();

            if (previous is not null)
            {
                old = await _macros.GetAsync(previous.Id, cancellationToken);
            }
        }

        return Ok(MacroMapping.Diff(current.Id, old?.Id, current.Code, old?.Code));
    }
}
