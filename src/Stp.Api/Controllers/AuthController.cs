using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
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
/// Đăng nhập/đăng xuất/danh tính/tự đăng ký cho tài khoản nhóm (yêu cầu 11/11/2026:
/// mỗi người một tài khoản, trang ra vào "cuốn hồ sơ" lật đăng nhập/đăng ký).
/// Phiên dựa cookie HttpOnly scheme "StpTeam" — client không chạm được token,
/// tắt trình duyệt mở lại vẫn còn. Ghi nhớ 24 giờ kiểu sliding: còn vào app
/// mỗi ngày là cookie tự gia hạn, không phải đăng nhập lại.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly TeamAudit _audit;
    private readonly IConfiguration _config;

    private readonly PasswordHasher<StpUser> _hasher = new();

    public AuthController(
        IDbContextFactory<StpDbContext> factory,
        TeamAudit audit,
        IConfiguration config)
    {
        _factory = factory;
        _audit = audit;
        _config = config;
    }

    /// <summary>Đăng nhập: kiểm tra username + password, cấp cookie phiên 24 giờ (sliding).</summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthIdentityResponse>> Login(
        [FromBody] LoginRequest? request)
    {
        // Chuẩn hoá NFC trước khi so/hash: khác kiểu gõ dấu (ghép/dựng sẵn)
        // vẫn ra cùng một chuỗi byte, mật khẩu vào được ổn định mọi máy.
        var username = AccountRules.NormalizeInput((request?.Username ?? string.Empty).Trim());
        var password = AccountRules.NormalizeInput(request?.Password ?? string.Empty);

        if (username.Length == 0 || password.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Thiếu thông tin",
                Detail = "Cần cả tên đăng nhập và mật khẩu.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        using var db = _factory.CreateDbContext();
        var user = await db.StpUsers
            .SingleOrDefaultAsync(candidate => candidate.Username == username);

        // Thông báo chung cho cả hai trường hợp để không dò được tên nào tồn tại.
        var invalidProblem = new ProblemDetails
        {
            Title = "Không đăng nhập được",
            Detail = "Tên đăng nhập hoặc mật khẩu chưa đúng.",
            Status = StatusCodes.Status401Unauthorized
        };

        if (user is null)
        {
            return Unauthorized(invalidProblem);
        }

        if (!user.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Tài khoản đã ngừng sử dụng",
                Detail = "Tài khoản này bị quản trị ngừng kích hoạt. Liên hệ quản trị để mở lại.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        var verification = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(invalidProblem);
        }

        // Thuật toán hash mặc định đổi (ví dụ sau nâng cấp .NET) thì làm mới hash ngay.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(StpTeamIdentity.ClaimUsername, user.Username),
            new(ClaimTypes.Role, user.Role)
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, StpTeamIdentity.SchemeName));

        await HttpContext.SignInAsync(
            StpTeamIdentity.SchemeName,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
            });

        _audit.Write(user.DisplayName, "auth.login", "StpUser", user.Id.ToString(), $"Đăng nhập thành công ({user.Username})");

        return Ok(new AuthIdentityResponse(user.Id, user.Username, user.DisplayName, user.Role));
    }

    /// <summary>Danh tính hiện tại; chưa đăng nhập thì 401 — client dựa đó hiện màn hình đăng nhập.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<AuthIdentityResponse>> Me()
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Chưa đăng nhập",
                Detail = "Đăng nhập để chat, nhật ký và presence gắn đúng tài khoản của bạn.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        return Ok(new AuthIdentityResponse(identity.Id, identity.Username, identity.DisplayName, identity.Role));
    }

    /// <summary>Đăng xuất: xoá cookie; luôn trả 204 kể cả khi chưa đăng nhập (vô hại).</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is not null)
        {
            _audit.Write(identity.DisplayName, "auth.logout", "StpUser", identity.Id.ToString(), $"Đăng xuất ({identity.Username})");
            await HttpContext.SignOutAsync(StpTeamIdentity.SchemeName);
        }

        return NoContent();
    }

    /// <summary>
    /// Tự đăng ký tài khoản thành viên — người mới mở link là tự tạo được tài
    /// khoản và vào làm việc ngay (role member). Quản trị tắt được bằng cấu
    /// hình <c>StpTeam:AllowRegistration=false</c>; sau đó chỉ quản trị tạo
    /// tài khoản trong trang Quản lý.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthIdentityResponse>> Register(
        [FromBody] RegisterRequest? request)
    {
        if (!_config.GetValue("StpTeam:AllowRegistration", true))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Đăng ký đang tắt",
                Detail = "Quản trị đã tắt tự đăng ký. Liên hệ quản trị để được cấp tài khoản.",
                Status = StatusCodes.Status403Forbidden
            });
        }

        // NFC + trim trước khi validate — cùng luật với đăng nhập để tránh
        // "đăng ký kiểu này, đăng nhập kiểu khác".
        var username = AccountRules.NormalizeInput((request?.Username ?? string.Empty).Trim());
        var displayName = AccountRules.NormalizeInput((request?.DisplayName ?? string.Empty).Trim());
        var password = AccountRules.NormalizeInput(request?.Password ?? string.Empty);

        var errors = new[]
        {
            AccountRules.ValidateUsername(username),
            AccountRules.ValidateDisplayName(displayName),
            AccountRules.ValidatePassword(password)
        }.SelectMany(static group => group).ToList();

        if (errors.Count > 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Chưa hợp lệ",
                Detail = string.Join(' ', errors),
                Status = StatusCodes.Status400BadRequest
            });
        }

        using var db = _factory.CreateDbContext();
        var exists = await db.StpUsers.AnyAsync(candidate => candidate.Username == username);
        if (exists)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Tên đăng nhập đã có người dùng",
                Detail = $"\"{username}\" đã tồn tại — chọn tên khác, hoặc đăng nhập nếu đó là tài khoản của bạn.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var user = new StpUser
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Username = username,
            DisplayName = displayName,
            Role = AccountRules.RoleMember,
            IsActive = true,
            PasswordHash = string.Empty
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        db.StpUsers.Add(user);
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(StpTeamIdentity.ClaimUsername, user.Username),
            new(ClaimTypes.Role, user.Role)
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, StpTeamIdentity.SchemeName));
        await HttpContext.SignInAsync(
            StpTeamIdentity.SchemeName,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
            });

        _audit.Write(user.DisplayName, "auth.register", "StpUser", user.Id.ToString(), $"Tự đăng ký tài khoản ({user.Username})");

        return Ok(new AuthIdentityResponse(user.Id, user.Username, user.DisplayName, user.Role));
    }

    /// <summary>
    /// Tổng kết "bạn vừa làm gì, ở khu nào" lấy từ nhật ký nhóm thật (AuditLogs,
    /// không đếm sự kiện đăng nhập/đăng xuất) — client hiện ngay sau màn chờ
    /// vào hệ thống, giống kiểu thông báo hoạt động của Google Sheets.
    /// </summary>
    [HttpGet("recap")]
    public async Task<ActionResult<RecapListResponse>> Recap(
        [FromQuery] int take = 8,
        CancellationToken cancellationToken = default)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Chưa đăng nhập",
                Detail = "Đăng nhập để xem tổng kết việc của bạn.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        take = Math.Clamp(take, 1, 20);

        using var db = _factory.CreateDbContext();
        var rows = await db.AuditLogs
            .AsNoTracking()
            .Where(log => log.Actor == identity.DisplayName)
            .OrderByDescending(log => log.Timestamp)
            .Take(take * 2)
            .Select(log => new { log.Timestamp, log.Action, log.Details })
            .ToListAsync(cancellationToken);

        var items = rows
            .Where(static row => !row.Action.StartsWith("auth.", StringComparison.Ordinal))
            .Take(take)
            .Select(static row =>
            {
                var (area, label) = DescribeAction(row.Action);
                return new ActivityRecapResponse(row.Timestamp, row.Action, area, label, row.Details);
            })
            .ToList();

        return Ok(new RecapListResponse(items));
    }

    /// <summary>Ghép thao tác trong nhật ký thành "khu làm việc + tên việc" tiếng Việt cho thẻ tổng kết.</summary>
    private static (string Area, string Label) DescribeAction(string action) => action switch
    {
        "export" => ("Bổ sung trích yếu", "Xuất file"),
        "export-filtered" => ("Bổ sung trích yếu", "Xuất bản lọc"),
        "export-print" => ("Bổ sung trích yếu", "Xuất bản in"),
        "sync" => ("Đồng bộ từ Excel", "Đồng bộ dữ liệu"),
        "admin.user-create" => ("Quản lý tài khoản", "Tạo tài khoản"),
        "admin.user-update" => ("Quản lý tài khoản", "Sửa tài khoản"),
        "admin.password-reset" => ("Quản lý tài khoản", "Đặt lại mật khẩu"),
        _ => ("Hệ thống", action)
    };
}
