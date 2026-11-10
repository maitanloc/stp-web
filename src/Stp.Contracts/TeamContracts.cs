namespace Stp.Contracts;

/// <summary>Trạng thái online + dòng đang sửa của thành viên, trả về khi ping presence.</summary>
public sealed record PresencePingRequest(
    string Actor,
    string Where,
    Guid? SessionId = null,
    int? RowNumber = null);

public sealed record PresenceEntryResponse(
    string Actor,
    string Where,
    int LastSeenSecondsAgo);

public sealed record PresenceRowMarkResponse(
    Guid SessionId,
    int RowNumber,
    string Actor,
    int LastSeenSecondsAgo);

public sealed record PresencePingResponse(
    IReadOnlyList<PresenceEntryResponse> Online,
    IReadOnlyList<PresenceRowMarkResponse> RowMarks);

/// <summary>Tin nhắn kênh chat tổng của nhóm nội bộ.</summary>
public sealed record ChatPostRequest(string Actor, string Body);

public sealed record ChatMessageResponse(Guid Id, string Actor, string Body, DateTimeOffset CreatedAt);

public sealed record ChatMessagesResponse(IReadOnlyList<ChatMessageResponse> Items);

/// <summary>Dòng nhật ký hoạt động — ai vừa làm gì (xuất, đồng bộ…).</summary>
public sealed record ActivityEventResponse(
    Guid Id,
    string Actor,
    string Action,
    string? EntityType,
    string? EntityId,
    string? Details,
    DateTimeOffset Timestamp);

public sealed record ActivityEventsResponse(IReadOnlyList<ActivityEventResponse> Items);
