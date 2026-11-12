using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Hubs;
using Stp.Core.Domain;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Services;

/// <summary>
/// Sinh thông báo nhóm (G1) tại chỗ sự kiện phát sinh: tin nhắn chat mới →
/// kind "chat" cho mọi tài khoản khác; hành động đáng kể của đồng đội (xuất,
/// đồng bộ, áp hàng loạt) → kind "system". Nhóm nhỏ nên ghi một dòng cho mỗi
/// người — bell topbar đọc thẳng bảng, không cần đếm ngược.
/// Fire-and-forget an toàn: gãy tuyệt đối không được làm rớt hành động chính.
/// </summary>
public sealed class NotificationService
{
    /// <summary>Hành động đáng xuất hiện trong bell (khớp SIGNIFICANT_ACTIONS client).</summary>
    public static readonly HashSet<string> SignificantActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "export",
        "export-filtered",
        "export-print",
        "sync",
        "bulk",
    };

    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly IHubContext<TeamHub> _hubContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IDbContextFactory<StpDbContext> factory,
        IHubContext<TeamHub> hubContext,
        ILogger<NotificationService> logger)
    {
        _factory = factory;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>Ghi thông báo cho mọi tài khoản đang hoạt động, trừ chính người gây ra.</summary>
    public void NotifyAllExcept(
        string actor,
        string kind,
        string title,
        string body,
        string? link)
    {
        try
        {
            using var db = _factory.CreateDbContext();
            var recipients = db.StpUsers
                .AsNoTracking()
                .Where(user => user.IsActive && user.DisplayName != actor)
                .Select(user => user.DisplayName)
                .ToList();

            if (recipients.Count == 0)
            {
                return;
            }

            var at = DateTimeOffset.UtcNow;
            db.Notifications.AddRange(recipients.Select(recipient => new Notification
            {
                Id = Guid.NewGuid(),
                CreatedAt = at,
                ForUser = recipient,
                Kind = kind,
                Title = title,
                Body = body,
                Link = link,
            }));
            db.SaveChanges();

            // Bell realtime: mỗi người tự lọc thông báo của mình ở client.
            foreach (var recipient in recipients)
            {
                _ = _hubContext.Clients.All.SendAsync("notification", new
                {
                    forUser = recipient,
                    kind,
                    title,
                    body,
                    link,
                    timestamp = at,
                });
            }
        }
        catch (Exception caught)
        {
            _logger.LogWarning(caught, "Không sinh được thông báo nhóm ({Kind})", kind);
        }
    }
}
