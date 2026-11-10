using Microsoft.EntityFrameworkCore;
using Stp.Core.Domain;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Services;

/// <summary>
/// Ghi nhật ký hoạt động nhóm vào bảng AuditLogs (bảng có sẵn, trước đó trống).
/// Trả về fire-and-forget an toàn: nhật ký gãy tuyệt đối không được phép làm
/// rớt xuất file / đồng bộ của người dùng.
/// </summary>
public sealed class TeamAudit
{
    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly ILogger<TeamAudit> _logger;

    public TeamAudit(
        IDbContextFactory<StpDbContext> factory,
        ILogger<TeamAudit> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>Tên người gửi theo header <c>X-Stp-User</c>; thiếu thì "Không rõ".</summary>
    public static string ActorFromHeader(HttpRequest request)
    {
        var candidates = request.Headers.TryGetValue("X-Stp-User", out var headerValue)
            ? (string[]?)headerValue.ToArray() ?? Array.Empty<string>()
            : Array.Empty<string>();
        var actor = candidates
            .Select(DecodeHeader)
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
        return actor.Length == 0 ? "Không rõ" : Truncate(actor, 100);
    }

    /// <summary>Client gửi header dạng encodeURIComponent; giải ngược, lỗi thì giữ nguyên.</summary>
    private static string DecodeHeader(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    public void Write(string actor, string action, string? entityType, string? entityId, string? details)
    {
        try
        {
            using var db = _factory.CreateDbContext();
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Actor = Truncate(string.IsNullOrWhiteSpace(actor) ? "Không rõ" : actor, 100),
                Action = Truncate(action, 200),
                EntityType = string.IsNullOrWhiteSpace(entityType) ? null : Truncate(entityType, 100),
                EntityId = string.IsNullOrWhiteSpace(entityId) ? null : Truncate(entityId, 200),
                Details = details
            });
            db.SaveChanges();
        }
        catch (Exception caught)
        {
            _logger.LogWarning(caught, "Không ghi được nhật ký hoạt động ({Action})", action);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
