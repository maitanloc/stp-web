using System.Collections.Concurrent;
using Stp.Contracts;

namespace Stp.Api.Services;

/// <summary>
/// Trạng thái "ai đang ở đâu / đang sửa dòng nào" — chỉ giữ trong bộ nhớ của
/// một process API vì đây là tín hiệu tức thời: restart app thì dấu tự tan,
/// không có gì cần lưu. Client ping nhịp 8–20 giây; mục quá hạn bị dọn lúc
/// mỗi lần ping. TTL dài hơn nhịp ping để mạng chập chờn qua tunnel không
/// làm bật tắt linh tinh.
/// </summary>
public sealed class TeamPresence
{
    public static readonly TimeSpan OnlineTtl = TimeSpan.FromSeconds(75);
    public static readonly TimeSpan RowMarkTtl = TimeSpan.FromSeconds(65);

    private readonly ConcurrentDictionary<(string Actor, string Where), DateTimeOffset> _online = new();
    private readonly ConcurrentDictionary<(Guid SessionId, int RowNumber, string Actor), DateTimeOffset> _rowMarks = new();

    /// <summary>
    /// Ghi nhận ping và trả trạng thái nhóm: ai online + ai đang sửa dòng nào
    /// (toàn bộ phiên given trong request nếu có sessionId).
    /// actor rỗng → chỉ trả trạng thái, không liệt kê vào online.
    /// </summary>
    public PresencePingResponse Ping(
        string? actor,
        string? where,
        Guid? sessionId,
        int? rowNumber,
        DateTimeOffset now)
    {
        PruneExpired(now);

        if (!string.IsNullOrWhiteSpace(actor) && !string.IsNullOrWhiteSpace(where))
        {
            _online[(actor.Trim(), where.Trim())] = now;
        }

        if (!string.IsNullOrWhiteSpace(actor) && sessionId.HasValue && rowNumber.HasValue)
        {
            _rowMarks[(sessionId.Value, rowNumber.Value, actor.Trim())] = now;
        }
        else if (!string.IsNullOrWhiteSpace(actor) && sessionId.HasValue)
        {
            // Người đóng bảng sửa (rowNumber trống) → gỡ dấu dòng của chính mình.
            foreach (var key in _rowMarks.Keys.Where(key =>
                         key.SessionId == sessionId.Value && key.Actor == actor.Trim()))
            {
                _rowMarks.TryRemove(key, out _);
            }
        }

        var online = _online
            .Select(pair => (pair.Key.Actor, pair.Key.Where, LastSeen: pair.Value))
            .GroupBy(entry => entry.Actor, StringComparer.Ordinal)
            .Select(group => new PresenceEntryResponse(
                group.Key,
                group.OrderByDescending(entry => entry.LastSeen).First().Where,
                Math.Max(0, (int)(now - group.Max(entry => entry.LastSeen)).TotalSeconds)))
            .OrderBy(entry => entry.Actor, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var marks = _rowMarks
            .Select(pair => new PresenceRowMarkResponse(
                pair.Key.SessionId,
                pair.Key.RowNumber,
                pair.Key.Actor,
                Math.Max(0, (int)(now - pair.Value).TotalSeconds)))
            .Where(mark => sessionId == null || mark.SessionId == sessionId.Value)
            .OrderBy(mark => mark.SessionId)
            .ThenBy(mark => mark.RowNumber)
            .ThenBy(mark => mark.Actor, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PresencePingResponse(online, marks);
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var pair in _online)
        {
            if (now - pair.Value > OnlineTtl)
            {
                _online.TryRemove(pair.Key, out _);
            }
        }

        foreach (var pair in _rowMarks)
        {
            if (now - pair.Value > RowMarkTtl)
            {
                _rowMarks.TryRemove(pair.Key, out _);
            }
        }
    }
}
