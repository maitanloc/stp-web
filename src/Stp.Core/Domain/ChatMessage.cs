namespace Stp.Core.Domain;

/// <summary>
/// Một tin nhắn trong kênh chat tổng của nhóm nội bộ — lắng trên mọi trang
/// của app, lưu DB để vào lại vẫn đọc được lịch sử.
/// Người gửi tự khai tên (không có tài khoản ở giai đoạn này), máy phía client
/// nhớ tên trong localStorage và gửi kèm từng lần chat.
/// </summary>
public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tên người gửi tự khai (tối đa 100 ký tự, không trùng lặp cưỡng bức).</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Nội dung tin nhắn dạng văn bản thuần.</summary>
    public string Body { get; set; } = string.Empty;
}
