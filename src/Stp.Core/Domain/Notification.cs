namespace Stp.Core.Domain;

/// <summary>
/// Thông báo nhóm (G1): một dòng cho mỗi việc người dùng cần nhìn — tin nhắn
/// mới trong kênh chat (kind "chat") hoặc hành động đáng kể của đồng đội
/// xuất/đồng bộ/áp hàng loạt (kind "system"). Ghi tại chỗ sự kiện phát sinh,
/// người nhận tự đánh dấu đã đọc qua API; bell topbar đếm số chưa đọc.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Người nhận (DisplayName của tài khoản nhóm, tối đa 100 ký tự).</summary>
    public string ForUser { get; set; } = string.Empty;

    /// <summary>"system" — hành động của đồng đội; "chat" — tin nhắn mới.</summary>
    public string Kind { get; set; } = "system";

    /// <summary>Tiêu đề ngắn hiển thị trong bell (vd "Bá khí vừa xuất bản lọc").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Chi tiết một dòng, tối đa 400 ký tự.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Deep link trong app (vd "/trich-yeu", "/kho") — bấm thông báo là tới.</summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
