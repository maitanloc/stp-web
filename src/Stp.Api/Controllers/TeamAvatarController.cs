using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Hubs;
using Stp.Api.Services;
using Stp.Core.Storage;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

/// <summary>
/// Ảnh đại diện cho tài khoản nhóm (Trụ B — K1). Client cắt vuông + nén
/// 96×96 webp trước khi gửi; server chỉ kiểm tra rồi lưu
/// <c>data/avatars/{userId}.webp</c>, tăng <c>StpUsers.AvatarVersion</c> để
/// mọi máy phá cache qua <c>?v=</c>. Ảnh chỉ phục người trong team (cookie
/// "StpTeam") — đúng yêu cầu riêng tư của plan.
/// </summary>
[ApiController]
[Route("api/team")]
public sealed class TeamAvatarController : ControllerBase
{
    /// <summary>Giới hạn trước khi nén — client đã nén về vài KB, đây là hàng rào cuối.</summary>
    private const long MaxUploadBytes = 2L * 1024 * 1024;

    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly IFileStorage _storage;
    private readonly TeamAudit _audit;
    private readonly IHubContext<TeamHub> _hubContext;
    private readonly ILogger<TeamAvatarController> _logger;

    public TeamAvatarController(
        IDbContextFactory<StpDbContext> factory,
        IFileStorage storage,
        TeamAudit audit,
        IHubContext<TeamHub> hubContext,
        ILogger<TeamAvatarController> logger)
    {
        _factory = factory;
        _storage = storage;
        _audit = audit;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Danh sách thành viên CÓ ảnh đại diện — client dựng map theo tên hiển thị
    /// để renderAvatar biết chỗ nào dùng ảnh, chỗ nào giữ vòng chữ cái đầu.
    /// </summary>
    [HttpGet("avatars")]
    public async Task<ActionResult<IReadOnlyList<TeamAvatarEntryResponse>>> List(
        CancellationToken cancellationToken)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized();
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entries = await db.StpUsers
            .AsNoTracking()
            .Where(user => user.IsActive && user.AvatarVersion > 0)
            .Select(user => new TeamAvatarEntryResponse(
                user.Id,
                user.DisplayName,
                user.AvatarVersion))
            .ToListAsync(cancellationToken);

        return Ok(entries);
    }

    /// <summary>
    /// Upload avatar của CHÍNH MÌNH (multipart form, field "file") — client đã
    /// cắt 96×96 webp; server lưu data/avatars/{userId}.webp và tăng version.
    /// </summary>
    [HttpPost("avatar")]
    [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
    public async Task<ActionResult<TeamAvatarUploadResponse>> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized(new { message = "Đăng nhập để đổi ảnh đại diện." });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Chưa chọn ảnh nào." });
        }

        if (file.Length > MaxUploadBytes)
        {
            return BadRequest(new { message = "Ảnh quá lớn (giới hạn 2 MB trước nén)." });
        }

        var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();
        if (!contentType.StartsWith("image/"))
        {
            return BadRequest(new { message = "Tệp không phải ảnh." });
        }

        var directory = Path.Combine(_storage.RootPath, "avatars");
        var path = Path.Combine(directory, $"{identity.Id:N}.webp");
        if (!_storage.IsUnderRoot(path))
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Đường dẫn lưu ảnh không hợp lệ." });
        }

        Directory.CreateDirectory(directory);
        try
        {
            await using (var stream = new FileStream(
                             path,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }
        }
        catch (Exception caught)
        {
            _logger.LogWarning(caught, "Không lưu được avatar cho {UserId}", identity.Id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Không lưu được ảnh đại diện." });
        }

        await using (var db = await _factory.CreateDbContextAsync(cancellationToken))
        {
            var user = await db.StpUsers
                .SingleOrDefaultAsync(candidate => candidate.Id == identity.Id, cancellationToken);
            if (user is null)
            {
                return NotFound();
            }

            user.AvatarVersion += 1;
            await db.SaveChangesAsync(cancellationToken);

            _audit.Write(
                identity.DisplayName,
                "team.avatar-upload",
                "StpUser",
                user.Id.ToString(),
                $"Đổi ảnh đại diện (phiên bản {user.AvatarVersion})");

            try
            {
                await _hubContext.Clients.All.SendAsync(
                    "teamAvatar",
                    new TeamAvatarEntryResponse(user.Id, user.DisplayName, user.AvatarVersion),
                    cancellationToken);
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(hubEx, "Không gửi được broadcast avatar qua hub");
            }

            return Ok(new TeamAvatarUploadResponse(user.Id, user.AvatarVersion));
        }
    }

    /// <summary>
    /// Trả ảnh đại diện của một thành viên kèm ETag theo AvatarVersion:
    /// trình duyệt cache 7 ngày, đổi ảnh → version tăng → URL ?v= mới → tải lại.
    /// </summary>
    [HttpGet("avatar/{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken cancellationToken)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized();
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var version = await db.StpUsers
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (int?)user.AvatarVersion)
            .SingleOrDefaultAsync(cancellationToken);

        if (version is null or 0)
        {
            return NotFound();
        }

        var path = Path.Combine(_storage.RootPath, "avatars", $"{userId:N}.webp");
        if (!_storage.IsUnderRoot(path) || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var etag = $"\"avatar-{userId:N}-v{version}\"";
        if (Request.Headers.TryGetValue("If-None-Match", out var cached)
            && cached.ToString().Contains(etag, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        await using var stream = System.IO.File.OpenRead(path);
        Response.Headers["ETag"] = etag;
        Response.Headers["Cache-Control"] = "private, max-age=604800";
        return File(stream, "image/webp");
    }
}

/// <summary>Một thành viên có ảnh đại diện — cho map renderAvatar phía client.</summary>
public sealed record TeamAvatarEntryResponse(Guid UserId, string DisplayName, int AvatarVersion);

/// <summary>Kết quả upload — client cập nhật map ngay lập tức.</summary>
public sealed record TeamAvatarUploadResponse(Guid UserId, int AvatarVersion);
