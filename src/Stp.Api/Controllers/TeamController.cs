using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
/// Người dùng tự khai tên phía client (header X-Stp-User / thân request),
/// chưa có hệ thống tài khoản ở giai đoạn này. Endpoint mở như các endpoint
/// công cụ trích yếu khác.
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

    public TeamController(
        IDbContextFactory<StpDbContext> factory,
        TeamPresence presence)
    {
        _factory = factory;
        _presence = presence;
    }

    /// <summary>Đọc tin nhắn mới nhất của kênh chat tổng (tối đa 100, cũ → mới).</summary>
    [HttpGet("chat")]
    public async Task<ActionResult<ChatMessagesResponse>> Chat(
        [FromQuery] int? take)
    {
        var takeCount = Math.Clamp(take ?? MaxChatHistory, 1, MaxChatHistory);
        using var db = _factory.CreateDbContext();
        var items = await db.ChatMessages
            .OrderByDescending(message => message.CreatedAt)
            .Take(takeCount)
            .ToListAsync();

        return Ok(new ChatMessagesResponse(items
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Select(message => new ChatMessageResponse(message.Id, message.Actor, message.Body, message.CreatedAt))
            .ToList()));
    }

    /// <summary>Gửi tin nhắn vào kênh tổng (lưu DB, trả tin đã lưu).</summary>
    [HttpPost("chat")]
    public async Task<ActionResult<ChatMessagesResponse>> PostChat(
        [FromBody] ChatPostRequest? request)
    {
        var actor = (request?.Actor ?? string.Empty).Trim();
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

        using var db = _factory.CreateDbContext();
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        return Ok(new ChatMessagesResponse(
            [new ChatMessageResponse(message.Id, message.Actor, message.Body, message.CreatedAt)]));
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
    public ActionResult<PresencePingResponse> Presence(
        [FromBody] PresencePingRequest? request)
    {
        var actor = (request?.Actor ?? string.Empty).Trim();
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
}
