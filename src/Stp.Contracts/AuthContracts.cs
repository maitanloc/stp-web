namespace Stp.Contracts;

/// <summary>Đăng nhập tài khoản nhóm.</summary>
public sealed record LoginRequest(string Username, string Password);

/// <summary>Danh tính người đã đăng nhập — client dùng cho chat, presence, phân quyền.</summary>
public sealed record AuthIdentityResponse(Guid Id, string Username, string DisplayName, string Role);

/// <summary>Một tài khoản cho trang quản trị (không bao giờ chứa mật khẩu).</summary>
public sealed record UserAccountResponse(
    Guid Id,
    string Username,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserAccountListResponse(IReadOnlyList<UserAccountResponse> Items);

/// <summary>Quản trị tạo tài khoản mới cho thành viên.</summary>
public sealed record CreateUserRequest(string Username, string DisplayName, string Password, string Role);

/// <summary>Quản trị sửa tên hiển thị / quyền / kích hoạt. Trường trống là giữ nguyên.</summary>
public sealed record UpdateUserRequest(string? DisplayName, string? Role, bool? IsActive);

/// <summary>Quản trị đặt lại mật khẩu cho thành viên.</summary>
public sealed record ResetPasswordRequest(string Password);
