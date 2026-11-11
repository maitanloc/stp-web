using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Core.Security;
using Stp.Infrastructure.Persistence;
using Stp.Api.Services;

namespace Stp.Api.Controllers;

/// <summary>
/// Trang quản trị tài khoản: tạo/sửa/ngừng kích hoạt/đặt lại mật khẩu cho thành viên.
/// Chỉ admin (cookie "StpTeam") vào được; mọi thao tác ghi vào AuditLogs để vào lại
/// vẫn còn rõ ai đã làm gì với ai.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(AuthenticationSchemes = StpTeamIdentity.SchemeName, Roles = AccountRules.RoleAdmin)]
public sealed class UsersController : ControllerBase
{
    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly TeamAudit _audit;

    private readonly PasswordHasher<StpUser> _hasher = new();

    public UsersController(
        IDbContextFactory<StpDbContext> factory,
        TeamAudit audit)
    {
        _factory = factory;
        _audit = audit;
    }

    /// <summary>Danh sách tất cả tài khoản (theo thời gian tạo).</summary>
    [HttpGet]
    public async Task<ActionResult<UserAccountListResponse>> List()
    {
        using var db = _factory.CreateDbContext();
        var users = await db.StpUsers
            .AsNoTracking()
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Username)
            .ToListAsync();

        return Ok(new UserAccountListResponse(users.Select(ToResponse).ToList()));
    }

    /// <summary>Tạo tài khoản mới cho một thành viên (mật khẩu gốc do quản trị đặt).</summary>
    [HttpPost]
    public async Task<ActionResult<UserAccountResponse>> Create(
        [FromBody] CreateUserRequest? request)
    {
        var username = AccountRules.NormalizeInput((request?.Username ?? string.Empty).Trim());
        var displayName = AccountRules.NormalizeInput((request?.DisplayName ?? string.Empty).Trim());
        var password = AccountRules.NormalizeInput(request?.Password ?? string.Empty);
        var role = (request?.Role ?? string.Empty).Trim();

        var errors = new List<string>();
        errors.AddRange(AccountRules.ValidateUsername(username));
        errors.AddRange(AccountRules.ValidateDisplayName(displayName));
        errors.AddRange(AccountRules.ValidatePassword(password));
        errors.AddRange(AccountRules.ValidateRole(role));

        if (errors.Count > 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Chưa hợp lệ",
                Detail = string.Join(" ", errors),
                Status = StatusCodes.Status400BadRequest
            });
        }

        using var db = _factory.CreateDbContext();
        var exists = await db.StpUsers
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Username == username);
        if (exists)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Tên đăng nhập đã tồn tại",
                Detail = $"\"{username}\" đã có người dùng rồi — chọn tên khác.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var user = new StpUser
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Username = username,
            DisplayName = displayName,
            Role = role,
            IsActive = true,
            PasswordHash = string.Empty
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        db.StpUsers.Add(user);
        await db.SaveChangesAsync();

        _audit.Write(await CurrentDisplayNameAsync(), "admin.user-create", "StpUser", user.Id.ToString(),
            $"Tạo tài khoản {user.Username} ({user.DisplayName}), quyền {user.Role}");

        return Ok(ToResponse(user));
    }

    /// <summary>Sửa tên hiển thị / quyền / kích hoạt. Không cho quản trị tự ngừng kích hoạt hay tự hạ quyền mình.</summary>
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<UserAccountResponse>> Update(
        Guid id,
        [FromBody] UpdateUserRequest? request)
    {
        var current = await StpTeamIdentity.FromAsync(HttpContext);
        if (current is null)
        {
            return Unauthorized(ToProblem("Phiên đăng nhập đã hết — đăng nhập lại."));
        }

        using var db = _factory.CreateDbContext();
        var user = await db.StpUsers.SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (user is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy tài khoản",
                Status = StatusCodes.Status404NotFound
            });
        }

        var changes = new List<string>();

        if (request is not null)
        {
            if (request.DisplayName is not null)
            {
                var displayName = request.DisplayName.Trim();
                var errors = AccountRules.ValidateDisplayName(displayName);
                if (errors.Count > 0)
                {
                    return BadRequest(ToProblem(errors[0]));
                }

                if (displayName != user.DisplayName)
                {
                    changes.Add($"đổi tên hiển thị \"{user.DisplayName}\" → \"{displayName}\"");
                    user.DisplayName = displayName;
                }
            }

            if (request.Role is not null)
            {
                var role = request.Role.Trim();
                var errors = AccountRules.ValidateRole(role);
                if (errors.Count > 0)
                {
                    return BadRequest(ToProblem(errors[0]));
                }

                if (current is not null && current.Id == id && role != current.Role)
                {
                    return BadRequest(ToProblem(
                        "Không tự đổi quyền của chính mình — dùng tài khoản quản trị khác để đổi."));
                }

                if (role != user.Role)
                {
                    changes.Add($"đổi quyền {user.Role} → {role}");
                    user.Role = role;
                }
            }

            if (request.IsActive is not null)
            {
                if (request.IsActive.Value == false && current is not null && current.Id == id)
                {
                    return BadRequest(ToProblem("Không thể tự ngừng kích hoạt tài khoản của chính mình."));
                }

                if (request.IsActive.Value != user.IsActive)
                {
                    changes.Add(request.IsActive.Value
                        ? "kích hoạt lại tài khoản"
                        : "ngừng kích hoạt tài khoản");
                    user.IsActive = request.IsActive.Value;
                }
            }
        }

        if (changes.Count > 0)
        {
            await db.SaveChangesAsync();
            _audit.Write(await CurrentDisplayNameAsync(), "admin.user-update", "StpUser", user.Id.ToString(),
                $"{string.Join("; ", changes)} (tài khoản {user.Username})");
        }

        return Ok(ToResponse(user));
    }

    /// <summary>Đặt lại mật khẩu cho thành viên (khi quên mật khẩu hoặc đổi định kỳ).</summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        [FromBody] ResetPasswordRequest? request)
    {
        var password = AccountRules.NormalizeInput(request?.Password ?? string.Empty);
        var errors = AccountRules.ValidatePassword(password);
        if (errors.Count > 0)
        {
            return BadRequest(ToProblem(errors[0]));
        }

        using var db = _factory.CreateDbContext();
        var user = await db.StpUsers.SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (user is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy tài khoản",
                Status = StatusCodes.Status404NotFound
            });
        }

        user.PasswordHash = _hasher.HashPassword(user, password);
        await db.SaveChangesAsync();

        _audit.Write(await CurrentDisplayNameAsync(), "admin.password-reset", "StpUser", user.Id.ToString(),
            $"Đặt lại mật khẩu cho {user.Username}");

        return NoContent();
    }

    private UserAccountResponse ToResponse(StpUser user) =>
        new(user.Id, user.Username, user.DisplayName, user.Role, user.IsActive, user.CreatedAt, user.LastLoginAt);

    private async Task<string> CurrentDisplayNameAsync() =>
        (await StpTeamIdentity.FromAsync(HttpContext))?.DisplayName ?? "Không rõ";

    private static ProblemDetails ToProblem(string detail) => new()
    {
        Title = "Không thực hiện được",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
