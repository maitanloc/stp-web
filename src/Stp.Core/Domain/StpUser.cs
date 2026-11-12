namespace Stp.Core.Domain;

/// <summary>
/// Tài khoản thành viên nhóm nội bộ — mỗi người một tài khoản thật (yêu cầu 11/11/2026).
/// Đăng nhập bằng username + mật khẩu; mật khẩu chỉ lưu dạng hash PBKDF2
/// (PasswordHasher&lt;StpUser&gt;), không bao giờ lưu hay trả về bản gốc.
/// DisplayName là tên hiện trên chat, nhật ký, presence và dấu dòng đang sửa.
/// </summary>
public sealed class StpUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tên đăng nhập — duy nhất, 3–50 ký tự chữ/số/dấu chấm, không dấu tiếng Việt.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Tên hiển thị cho cả nhóm (tiếng Việt tự do, tối đa 100 ký tự).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 do PasswordHasher&lt;StpUser&gt; sinh (bản V3), tối đa 200 ký tự.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Quyền: "admin" quản trị người dùng, "member" dùng app như thường.</summary>
    public string Role { get; set; } = "member";

    /// <summary>Ngừng kích hoạt thì không đăng nhập được nữa — thay cho xoá để giữ lịch sử.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Lần cuối người dùng đọc kênh chat (G3) — server giữ mốc seen để đếm tin
    /// chưa đọc đúng trên mọi máy, thay cho mốc localStorage từng máy.
    /// </summary>
    public DateTimeOffset? LastReadChatAt { get; set; }
}
