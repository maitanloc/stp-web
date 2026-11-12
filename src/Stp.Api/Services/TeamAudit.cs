using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Hubs;
using Stp.Core.Domain;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Services;

/// <summary>
/// Ghi nhật ký hoạt động nhóm vào bảng AuditLogs (bảng có sẵn, trước đó trống).
/// Trả về fire-and-forget an toàn: nhật ký gãy tuyệt đối không được phép làm
/// rớt xuất file / đồng bộ của người dùng. Ghi xong còn broadcast qua hub
/// realtime (F3) để đồng đội thấy chip hành động ngay, không chờ poll.
/// </summary>
public sealed class TeamAudit
{
    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly IHubContext<TeamHub> _hubContext;
    private readonly NotificationService _notifications;
    private readonly ILogger<TeamAudit> _logger;

    public TeamAudit(
        IDbContextFactory<StpDbContext> factory,
        IHubContext<TeamHub> hubContext,
        NotificationService notifications,
        ILogger<TeamAudit> logger)
    {
        _factory = factory;
        _hubContext = hubContext;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Ai ghi log: ưu tiên tài khoản đã đăng nhập (cookie StpTeam — không thể giả danh),
    /// thiếu thì lùi về tên tự khai theo header <c>X-Stp-User</c>, cuối cùng "Không rõ".
    /// </summary>
    public static async Task<string> ActorFromAsync(HttpContext context)
    {
        var identity = await StpTeamIdentity.FromAsync(context);
        if (identity is not null)
        {
            return Truncate(identity.DisplayName, 100);
        }

        return ActorFromHeader(context.Request);
    }

    /// <summary>Tên người gửi theo header <c>X-Stp-User</c>; thiếu thì "Không rõ".</summary>
    public static string ActorFromHeader(HttpRequest request)
    {
        var actor = request.Headers.TryGetValue("X-Stp-User", out var headerValue)
            ? headerValue
                .Select(DecodeHeader)
                .Select(value => value.Trim())
                .FirstOrDefault(value => value.Length > 0) ?? string.Empty
            : string.Empty;
        return actor.Length == 0 ? "Không rõ" : Truncate(actor, 100);
    }

    /// <summary>Client gửi header dạng encodeURIComponent; giải ngược, lỗi thì giữ nguyên.</summary>
    private static string DecodeHeader(string? value)
    {
        var text = value ?? string.Empty;
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }

    public void Write(string actor, string action, string? entityType, string? entityId, string? details)
    {
        var loggedActor = Truncate(string.IsNullOrWhiteSpace(actor) ? "Không rõ" : actor, 100);
        var at = DateTimeOffset.UtcNow;
        var loggedAction = Truncate(action, 200);

        try
        {
            using var db = _factory.CreateDbContext();
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Timestamp = at,
                Actor = loggedActor,
                Action = loggedAction,
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

        // F3: broadcast realtime cho đồng đội (chip hành động + feed). Lỗi hub
        // tuyệt đối không rớt hành động chính — nuốt im.
        try
        {
            _ = _hubContext.Clients.All.SendAsync("activity", new
            {
                actor = loggedActor,
                action = loggedAction,
                entityType,
                entityId,
                details,
                timestamp = at,
            });
        }
        catch (Exception caught)
        {
            _logger.LogWarning(caught, "Không broadcast được hành động qua hub ({Action})", action);
        }

        // G1: hành động đáng kể (xuất/đồng bộ/áp hàng loạt) → thông báo hệ thống
        // cho mọi thành viên khác để bell topbar nhấp nháy, kể cả ai đang offline.
        if (NotificationService.SignificantActions.Contains(loggedAction))
        {
            _notifications.NotifyAllExcept(
                loggedActor,
                "system",
                $"{loggedActor} · {ActionTitle(loggedAction)}",
                details ?? loggedAction,
                DeepLinkFor(loggedAction));
        }
    }

    /// <summary>Chữ dễ đọc cho tiêu đề thông báo (khớp actionLabel phía client).</summary>
    private static string ActionTitle(string action) => action switch
    {
        "export" => "Xuất trích yếu",
        "export-filtered" => "Xuất bản lọc",
        "export-print" => "Xuất in hàng loạt",
        "sync" => "Đồng bộ kho",
        "bulk" => "Áp giấy tờ hàng loạt",
        _ => action,
    };

    /// <summary>Deep link đưa người dùng tới đúng chỗ việc vừa xảy ra.</summary>
    private static string DeepLinkFor(string action) => action switch
    {
        "sync" => "/dong-bo",
        "export-print" => "/in-hang-loat",
        _ => "/trich-yeu",
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
