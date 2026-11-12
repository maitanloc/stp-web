using Microsoft.AspNetCore.SignalR;
using Stp.Api.Services;
using Stp.Contracts;
using Stp.Core.Summary;

namespace Stp.Api.Hubs;

/// <summary>
/// Hub realtime của nền tảng nhóm (F1): client gọi Ping thay cho POST
/// /api/team/presence — server lưu TTL như REST rồi broadcast "presence"
/// cho MỌI client đang nối, nên ai mở app cũng thấy ai online/đang sửa dòng
/// ngay lập tức thay vì chờ nhịp poll 8–20 giây. Poll REST vẫn chạy song
/// song như phương án dự phòng khi hub mất nối.
/// </summary>
public sealed class TeamHub : Hub
{
    private readonly TeamPresence _presence;
    private readonly ISummaryToolService _summary;
    private readonly ILogger<TeamHub> _logger;

    public TeamHub(
        TeamPresence presence,
        ISummaryToolService summary,
        ILogger<TeamHub> logger)
    {
        _presence = presence;
        _summary = summary;
        _logger = logger;
    }

    /// <summary>
    /// Ping presence qua hub: cập nhật TTL, trả trạng thái cho người gọi và
    /// broadcast cho mọi client cùng lúc. actor rỗng → chỉ đọc trạng thái.
    /// </summary>
    public PresencePingResponse Ping(
        string? actor,
        string? where,
        Guid? sessionId,
        int? rowNumber)
    {
        try
        {
            var response = _presence.Ping(
                actor,
                where,
                sessionId,
                rowNumber,
                DateTimeOffset.UtcNow);

            // Broadcast fire-and-forget: lỗi gửi tới một client không rớt ping.
            _ = Clients.All.SendAsync("presence", response);

            return response;
        }
        catch (Exception caught)
        {
            _logger.LogWarning(caught, "Ping qua hub gặp lỗi — bỏ qua nhịp này");
            return new PresencePingResponse(
                Array.Empty<PresenceEntryResponse>(),
                Array.Empty<PresenceRowMarkResponse>());
        }
    }

    /// <summary>
    /// "Ai đang gõ" (G3): client gọi khi người dùng gõ chat (throttle 2s phía
    /// client), broadcast cho cả nhóm. Không lưu gì — tín hiệu thoáng qua.
    /// </summary>
    public void Typing(string? actor)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100)
        {
            name = name[..100];
        }

        _ = Clients.Others.SendAsync("typing", new { actor = name });
    }

    /// <summary>
    /// Bảng sống kho hồ sơ (Trụ D — J1): ai vừa đổi bộ lọc trên lưới — cả
    /// phòng thấy chip "X đang lọc: năm Y · loại Z" và bấm "Áp dụng theo" để
    /// nhìn cùng một bảng. Không lưu gì, chỉ rebroadcast.
    /// </summary>
    public void GridFilter(
        string? actor,
        Guid? workbookId,
        int? year,
        string? type,
        string? search)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100)
        {
            name = name[..100];
        }

        _ = Clients.Others.SendAsync("gridFilter", new
        {
            actor = name,
            workbookId,
            year,
            type,
            search,
        });
    }

    /// <summary>
    /// Bảng sống kho hồ sơ (Trụ D — J1): con trỏ cộng tác viên — ai đang trỏ
    /// vào dòng nào (throttle 700ms phía client), TTL ngắn phía người nhận.
    /// </summary>
    public void GridCursor(string? actor, Guid? workbookId, Guid? dossierId)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100)
        {
            name = name[..100];
        }

        _ = Clients.Others.SendAsync("gridCursor", new { actor = name, workbookId, dossierId });
    }

    /// <summary>
    /// Tham gia phòng làm việc chung trên file trích yếu (Google Sheets realtime).
    /// </summary>
    public async Task JoinSummarySession(Guid sessionId, string? actor)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100) name = name[..100];

        await Groups.AddToGroupAsync(Context.ConnectionId, $"summary:{sessionId}");
        _ = Clients.OthersInGroup($"summary:{sessionId}").SendAsync("summaryMemberJoined", new
        {
            sessionId,
            actor = name
        });
    }

    /// <summary>
    /// Rời phòng làm việc chung trên file trích yếu.
    /// </summary>
    public async Task LeaveSummarySession(Guid sessionId, string? actor)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100) name = name[..100];

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"summary:{sessionId}");
        _ = Clients.OthersInGroup($"summary:{sessionId}").SendAsync("summaryMemberLeft", new
        {
            sessionId,
            actor = name
        });
    }

    /// <summary>
    /// Con trỏ ô realtime (Google Sheets cell cursor): ai đang click vào ô nào
    /// (số tờ, trích yếu, số hồ sơ) — đồng đội thấy viền màu + tag tên.
    /// </summary>
    public void SummaryCellFocus(Guid sessionId, int rowNumber, string column, string? actor)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100) name = name[..100];

        _ = Clients.OthersInGroup($"summary:{sessionId}").SendAsync("summaryCellFocus", new
        {
            sessionId,
            rowNumber,
            column,
            actor = name
        });
    }

    /// <summary>
    /// Gõ chữ realtime (streaming typing): khi đang gõ vào ô trích yếu hoặc số tờ,
    /// đồng đội thấy chữ nhảy theo tức thì.
    /// </summary>
    public void SummaryCellTyping(Guid sessionId, int rowNumber, string column, string text, string? actor)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100) name = name[..100];

        _ = Clients.OthersInGroup($"summary:{sessionId}").SendAsync("summaryCellTyping", new
        {
            sessionId,
            rowNumber,
            column,
            text,
            actor = name
        });
    }

    /// <summary>
    /// Hoàn tất sửa ô (bấm chip, Enter, bấm C như dòng trên): lưu vào Shared Edits
    /// trên server và broadcast ngay cho cả phòng cùng thấy kết quả.
    /// </summary>
    public void SummaryCellCommit(Guid sessionId, SummaryChangeRequest request, string? actor)
    {
        var name = string.IsNullOrWhiteSpace(actor) ? "Ai đó" : actor.Trim();
        if (name.Length > 100) name = name[..100];

        try
        {
            var change = new SummaryChange(
                request.RowNumber,
                request.Items ?? Array.Empty<string>(),
                request.SummaryText,
                request.SheetNumber,
                request.DocumentNumber,
                request.NumberLabel);

            _summary.RecordEdit(sessionId, change);

            _ = Clients.OthersInGroup($"summary:{sessionId}").SendAsync("summaryCellChanged", new
            {
                sessionId,
                rowNumber = request.RowNumber,
                items = request.Items ?? Array.Empty<string>(),
                summaryText = request.SummaryText,
                sheetNumber = request.SheetNumber,
                documentNumber = request.DocumentNumber,
                numberLabel = request.NumberLabel,
                actor = name,
                timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi commit ô qua hub cho phiên {SessionId}", sessionId);
        }
    }
}
