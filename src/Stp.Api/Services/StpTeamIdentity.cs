using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Stp.Api.Services;

/// <summary>
/// Danh tính người dùng nhóm đọc từ cookie auth scheme "StpTeam".
/// Tách khỏi JWT developer-auth hiện có: hai hệ song song, không đụng nhau —
/// dev vẫn vào bằng token JWT, nhóm nội bộ vào bằng cookie sau /api/auth/login.
/// Scheme mặc định của app là JWT, nên <c>HttpContext.User</c> thường không
/// chứa cookie nhóm: mọi endpoint phải dùng <see cref="FromAsync"/> — nó tự
/// authenticate đúng scheme cookie.
/// </summary>
public sealed record StpTeamIdentity(Guid Id, string Username, string DisplayName, string Role)
{
    public const string SchemeName = "StpTeam";
    public const string ClaimUsername = "stp.username";

    /// <summary>Đọc identity từ cookie phiên scheme "StpTeam"; null khi chưa đăng nhập.</summary>
    public static async Task<StpTeamIdentity?> FromAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(SchemeName);
        if (!result.Succeeded || result.Principal is null)
        {
            return null;
        }

        return Read(result.Principal);
    }

    private static StpTeamIdentity? Read(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var displayNameClaim = user.FindFirst(ClaimTypes.Name)?.Value;
        var usernameClaim = user.FindFirst(ClaimUsername)?.Value;
        var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value;

        if (!Guid.TryParse(idClaim, out var id)
            || string.IsNullOrWhiteSpace(displayNameClaim)
            || string.IsNullOrWhiteSpace(usernameClaim)
            || string.IsNullOrWhiteSpace(roleClaim))
        {
            return null;
        }

        return new StpTeamIdentity(id, usernameClaim, displayNameClaim, roleClaim);
    }
}
