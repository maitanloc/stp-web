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

/// <summary>Phản hồi khi tải lên tệp đính kèm kênh chat (ảnh, audio, tài liệu).</summary>
public sealed record ChatUploadResponse(
    string Url,
    string FileName,
    string FileType,
    long SizeBytes);

/// <summary>
/// Page chat (G4): Items cũ → mới; NextCursor = mốc thời gian tin cũ nhất
/// trong page (iso) để tải page cũ hơn bằng ?before=; HasMore cho biết còn
/// tin cũ hơn nữa không.
/// </summary>
public sealed record ChatMessagesResponse(
    IReadOnlyList<ChatMessageResponse> Items,
    string? NextCursor = null,
    bool HasMore = false);

/// <summary>Mốc seen chat server-side (G3) + số tin chưa đọc theo mốc đó.</summary>
public sealed record ChatSeenResponse(
    DateTimeOffset LastReadAt,
    int UnreadCount);

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

/// <summary>
/// Hành động phía client đáng chú ý (F3): bulk áp giấy tờ, gán hàng loạt…
/// server ghi AuditLogs (thành feed) + broadcast chip realtime qua hub.
/// </summary>
public sealed record TeamActionRequest(
    string Verb,
    string? Detail = null);

/// <summary>Một thông báo cho bell topbar (G1).</summary>
public sealed record NotificationItemResponse(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    string? Link,
    DateTimeOffset CreatedAt,
    bool IsRead);

/// <summary>Danh sách thông báo + số chưa đọc (G2).</summary>
public sealed record NotificationsResponse(
    IReadOnlyList<NotificationItemResponse> Items,
    int UnreadCount);

/// <summary>Đánh dấu đã đọc: tất cả, hoặc danh sách id cụ thể.</summary>
public sealed record NotificationsReadRequest(
    bool All = false,
    IReadOnlyList<Guid>? Ids = null);
