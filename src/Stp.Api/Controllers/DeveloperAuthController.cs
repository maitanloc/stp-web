using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stp.Contracts;
using Stp.Core.Security;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/dev/auth")]
public sealed class DeveloperAuthController : ControllerBase
{
    private readonly IDeveloperAuthService _auth;

    public DeveloperAuthController(IDeveloperAuthService auth)
    {
        _auth = auth;
    }

    /// <summary>Trạng thái thiết lập tài khoản developer (công khai).</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public ActionResult<DeveloperAuthStatusResponse> Status()
    {
        var status = _auth.GetStatus();
        return Ok(new DeveloperAuthStatusResponse(
            status.SetupRequired,
            status.Configured,
            status.Source));
    }

    /// <summary>Thiết lập mật khẩu lần đầu; chỉ thành công khi chưa có cấu hình.</summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<ActionResult<DeveloperSessionResponse>> Setup(
        [FromBody] DeveloperSetupRequest request,
        CancellationToken cancellationToken)
    {
        var password = request.Password ?? string.Empty;
        if (request.ConfirmPassword is not null
            && !string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Mật khẩu không khớp",
                Detail = "Hai lần nhập mật khẩu phải giống nhau.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var session = await _auth.SetupAsync(password, cancellationToken);
            return Ok(ToResponse(session));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Mật khẩu chưa đạt yêu cầu",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Không thể thiết lập",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>Đăng nhập developer, trả JWT dùng cho các API /api/dev.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<DeveloperSessionResponse>> Login(
        [FromBody] DeveloperLoginRequest request,
        CancellationToken cancellationToken)
    {
        var clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local";
        var result = await _auth.LoginAsync(
            request.Password ?? string.Empty,
            clientKey,
            cancellationToken);

        switch (result.Status)
        {
            case DeveloperLoginStatus.Success when result.Session is not null:
                return Ok(ToResponse(result.Session));

            case DeveloperLoginStatus.Throttled:
            {
                var seconds = (int)Math.Ceiling((result.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds);
                Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    new ProblemDetails
                    {
                        Title = "Đăng nhập bị tạm khoá",
                        Detail = $"Sai quá nhiều lần. Thử lại sau {Math.Max(seconds, 1)} giây.",
                        Status = StatusCodes.Status429TooManyRequests
                    });
            }

            case DeveloperLoginStatus.NotConfigured:
                return Conflict(new ProblemDetails
                {
                    Title = "Chưa thiết lập tài khoản",
                    Detail = "Hãy thiết lập mật khẩu developer trước khi đăng nhập.",
                    Status = StatusCodes.Status409Conflict
                });

            default:
                return Unauthorized(new ProblemDetails
                {
                    Title = "Sai mật khẩu",
                    Detail = "Mật khẩu developer không đúng.",
                    Status = StatusCodes.Status401Unauthorized
                });
        }
    }

    /// <summary>Thông tin phiên developer hiện tại.</summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<DeveloperMeResponse> Me()
    {
        var username = User.Identity?.Name ?? "Developer";
        var role = User.FindFirst("role")?.Value ?? "Developer";

        DateTimeOffset? expiresAt = null;
        var exp = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (long.TryParse(exp, out var unixSeconds))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return Ok(new DeveloperMeResponse(username, role, expiresAt));
    }

    private static DeveloperSessionResponse ToResponse(DeveloperSession session) =>
        new(session.Token, session.ExpiresAt, session.Username);
}
