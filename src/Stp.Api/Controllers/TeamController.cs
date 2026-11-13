using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Hubs;
using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Infrastructure.Persistence;
using Stp.Api.Services;

namespace Stp.Api.Controllers;

/// <summary>
/// "Nền tảng nhóm nội bộ" giống Google Sheets mà người dùng mong muốn:
///   • Chat tổng — gửi/đọc tin nhắn nhóm, lưu ChatMessages để vào lại vẫn đọc.
///   • Hoạt động — nhật ký ai vừa làm gì (AuditLogs, ghi tại chỗ xuất/đồng bộ).
///   • Presence — ai online + ai đang sửa dòng nào (heartbeat, tự tàn ngắn hạn).
/// Từ 11/11/2026: người đã đăng nhập (cookie StpTeam) thì mọi hành vi gắn
/// DisplayName từ tài khoản — không nhận tên gửi trong request nữa; người
/// chưa đăng nhập vẫn tự khai tên như giai đoạn đầu (xem AuthController).
/// </summary>
[ApiController]
[Route("api/team")]
public sealed class TeamController : ControllerBase
{
    private const int MaxChatBody = 500;
    private const int MaxActor = 100;
    private const int MaxChatHistory = 100;
    private const int MaxActivityTake = 100;

    private readonly IDbContextFactory<StpDbContext> _factory;
    private readonly TeamPresence _presence;
    private readonly TeamAudit _audit;
    private readonly NotificationService _notifications;
    private readonly IHubContext<TeamHub> _hubContext;

    public TeamController(
        IDbContextFactory<StpDbContext> factory,
        TeamPresence presence,
        TeamAudit audit,
        NotificationService notifications,
        IHubContext<TeamHub> hubContext)
    {
        _factory = factory;
        _presence = presence;
        _audit = audit;
        _notifications = notifications;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Đọc tin nhắn kênh chat (G4 cursor pagination): mới nhất trước, ?before=
    /// (iso của tin cũ nhất page trước) để lật page cũ hơn; tối đa 100 tin/page.
    /// </summary>
    [HttpGet("chat")]
    public async Task<ActionResult<ChatMessagesResponse>> Chat(
        [FromQuery] int? take,
        [FromQuery] string? before)
    {
        var takeCount = Math.Clamp(take ?? MaxChatHistory, 1, MaxChatHistory);
        await using var db = await _factory.CreateDbContextAsync();

        var query = db.ChatMessages.AsNoTracking();

        // Cursor ISO-8601 từ client (encodeURIComponent) — parse bất biến để
        // không phụ thuộc culture máy chủ; sai định dạng thì bỏ qua (trả page mới).
        if (!string.IsNullOrWhiteSpace(before)
            && DateTimeOffset.TryParse(
                before,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var cursor))
        {
            query = query.Where(message => message.CreatedAt < cursor);
        }

        var page = await query
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(takeCount + 1)
            .ToListAsync();

        // Lấy thêm 1 tin để biết còn page cũ nữa không, rồi thả phần thừa đi.
        var hasMore = page.Count > takeCount;
        var items = hasMore ? page.Take(takeCount).ToList() : page;

        // Cursor lật page cũ = mốc thời gian tin CŨ NHẤT trong page (before=
        // lọc CreatedAt < cursor nên không trùng page vừa đọc).
        var oldest = items.Count > 0 ? items.Min(message => message.CreatedAt) : (DateTimeOffset?)null;

        var response = new ChatMessagesResponse(
            items
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id)
                .Select(message => new ChatMessageResponse(
                    message.Id, message.Actor, message.Body, message.CreatedAt))
                .ToList(),
            oldest?.ToString("O"),
            hasMore);

        return Ok(response);
    }

    /// <summary>Gửi tin nhắn vào kênh tổng (lưu DB, trả tin đã lưu) + thông báo cho nhóm (G1).</summary>
    [HttpPost("chat")]
    public async Task<ActionResult<ChatMessagesResponse>> PostChat(
        [FromBody] ChatPostRequest? request)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        var actor = identity?.DisplayName ?? (request?.Actor ?? string.Empty).Trim();
        var body = (request?.Body ?? string.Empty).Trim();

        if (actor.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Chưa khai tên",
                Detail = "Cần khai tên (tối đa 100 ký tự) trước khi chat.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (body.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tin nhắn rỗng",
                Detail = "Nội dung tin nhắn không được trống.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Actor = actor.Length <= MaxActor ? actor : actor[..MaxActor],
            Body = body.Length <= MaxChatBody ? body : body[..MaxChatBody]
        };

        await using var db = await _factory.CreateDbContextAsync();
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        var chatResponse = new ChatMessageResponse(message.Id, message.Actor, message.Body, message.CreatedAt);

        try
        {
            _ = _hubContext.Clients.All.SendAsync("chatMessage", chatResponse);
        }
        catch
        {
            // Fire-and-forget: broadcast qua hub không được chặn phản hồi API
        }

        // G1: tin nhắn mới → thông báo kind "chat" cho mọi thành viên khác,
        // bấm bell là nhảy thẳng tới chat dock.
        _notifications.NotifyAllExcept(
            message.Actor,
            "chat",
            $"{message.Actor} nhắn tin nhóm",
            message.Body,
            "/trich-yeu");

        return Ok(new ChatMessagesResponse([chatResponse]));
    }

    /// <summary>
    /// Đánh dấu đã đọc toàn bộ tin nhắn tới thời điểm này (G3) — mốc seen nằm
    /// trên tài khoản nên đếm tin chưa đọc đúng trên mọi máy, mọi thiết bị.
    /// </summary>
    [HttpPost("chat/seen")]
    public async Task<ActionResult<ChatSeenResponse>> MarkChatSeen()
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized();
        }

        await using var db = await _factory.CreateDbContextAsync();
        var user = await db.StpUsers.SingleAsync(candidate => candidate.Id == identity.Id);
        var seenAt = DateTimeOffset.UtcNow;
        user.LastReadChatAt = seenAt;
        await db.SaveChangesAsync();

        return Ok(new ChatSeenResponse(seenAt, 0));
    }

    /// <summary>Mốc seen chat + số tin chưa đọc (G3) — cho bell tab "Tin nhắn".</summary>
    [HttpGet("chat/seen")]
    public async Task<ActionResult<ChatSeenResponse>> ChatSeen()
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized();
        }

        await using var db = await _factory.CreateDbContextAsync();
        var user = await db.StpUsers.AsNoTracking().SingleAsync(candidate => candidate.Id == identity.Id);
        var lastRead = user.LastReadChatAt ?? DateTimeOffset.MinValue;

        var unread = await db.ChatMessages
            .Where(message => message.CreatedAt > lastRead && message.Actor != identity.DisplayName)
            .CountAsync();

        return Ok(new ChatSeenResponse(user.LastReadChatAt ?? DateTimeOffset.MinValue, unread));
    }

    /// <summary>Đọc nhật ký hoạt động gần nhất (mới nhất lên đầu).</summary>
    [HttpGet("activity")]
    public async Task<ActionResult<ActivityEventsResponse>> Activity(
        [FromQuery] int? take)
    {
        var takeCount = Math.Clamp(take ?? 50, 1, MaxActivityTake);
        using var db = _factory.CreateDbContext();
        var items = await db.AuditLogs
            .OrderByDescending(log => log.Timestamp)
            .ThenByDescending(log => log.Id)
            .Take(takeCount)
            .ToListAsync();

        return Ok(new ActivityEventsResponse(items
            .Select(log => new ActivityEventResponse(
                log.Id, log.Actor, log.Action, log.EntityType, log.EntityId, log.Details, log.Timestamp))
            .ToList()));
    }

    /// <summary>
    /// Heartbeat presence: ai online ở trang nào + ai đang mở bảng sửa dòng nào.
    /// rowNumber trống nhưng có sessionId → coi người dùng vừa đóng bảng sửa.
    /// Nhịp client: 8 giây khi đang sửa dòng, 20 giây khi chỉ đứng xem.
    /// </summary>
    [HttpPost("presence")]
    public async Task<ActionResult<PresencePingResponse>> Presence(
        [FromBody] PresencePingRequest? request)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        var actor = identity?.DisplayName ?? (request?.Actor ?? string.Empty).Trim();
        var where = (request?.Where ?? string.Empty).Trim();
        if (actor.Length > MaxActor)
        {
            actor = actor[..MaxActor];
        }

        var response = _presence.Ping(
            actor,
            where,
            request?.SessionId,
            request?.RowNumber,
            DateTimeOffset.UtcNow);

        return Ok(response);
    }

    /// <summary>
    /// Hành động đáng chú ý làm phía client (F3): gán giấy tờ hàng loạt, áp cho
    /// dòng còn thiếu… Ghi vào AuditLogs (feed) + TeamAudit tự broadcast chip
    /// realtime qua hub cho đồng đội đang mở app.
    /// </summary>
    [HttpPost("action")]
    public async Task<IActionResult> AnnounceAction([FromBody] TeamActionRequest? request)
    {
        var verb = (request?.Verb ?? string.Empty).Trim();
        if (verb.Length == 0)
        {
            return Problem(title: "Thiếu tên hành động.", statusCode: 400);
        }

        var actor = await TeamAudit.ActorFromAsync(HttpContext);
        var detail = string.IsNullOrWhiteSpace(request?.Detail) ? null : request!.Detail!.Trim();
        if (detail is { Length: > 500 })
        {
            detail = detail[..500];
        }

        _audit.Write(actor, verb, "SummarySession", null, detail);
        return NoContent();
    }

    /// <summary>
    /// Thông báo của người đang đăng nhập (G1): mới nhất trước + số chưa đọc
    /// cho badge bell. Chưa đăng nhập → 401 (thông báo là của từng tài khoản).
    /// </summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<NotificationsResponse>> Notifications(
        [FromQuery] int? take)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized();
        }

        var takeCount = Math.Clamp(take ?? 40, 1, 100);
        await using var db = await _factory.CreateDbContextAsync();

        var items = await db.Notifications
            .AsNoTracking()
            .Where(item => item.ForUser == identity.DisplayName)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(takeCount)
            .ToListAsync();

        var unread = await db.Notifications
            .Where(item => item.ForUser == identity.DisplayName && !item.IsRead)
            .CountAsync();

        return Ok(new NotificationsResponse(items
            .Select(item => new NotificationItemResponse(
                item.Id, item.Kind, item.Title, item.Body, item.Link, item.CreatedAt, item.IsRead))
            .ToList(),
            unread));
    }

    /// <summary>Đánh dấu đã đọc: { all: true } hoặc { ids: [...] } (G1).</summary>
    [HttpPost("notifications/read")]
    public async Task<IActionResult> MarkNotificationsRead(
        [FromBody] NotificationsReadRequest? request)
    {
        var identity = await StpTeamIdentity.FromAsync(HttpContext);
        if (identity is null)
        {
            return Unauthorized();
        }

        await using var db = await _factory.CreateDbContextAsync();
        var query = db.Notifications
            .Where(item => item.ForUser == identity.DisplayName && !item.IsRead);

        if (request is { All: true })
        {
            // Đánh dấu tất cả trong 1 lượt — số dòng nhỏ nên Update chấp nhận được.
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsRead, true)
                .SetProperty(item => item.ReadAt, DateTimeOffset.UtcNow));
        }
        else if (request?.Ids is { Count: > 0 })
        {
            var ids = request.Ids.ToHashSet();
            var targets = await db.Notifications
                .Where(item => item.ForUser == identity.DisplayName
                               && !item.IsRead
                               && ids.Contains(item.Id))
                .ToListAsync();
            foreach (var item in targets)
            {
                item.IsRead = true;
                item.ReadAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
