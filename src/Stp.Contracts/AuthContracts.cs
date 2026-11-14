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

/// <summary>Tự đăng ký tài khoản thành viên (trang "cuốn hồ sơ" khi chưa có phiên).</summary>
public sealed record RegisterRequest(string Username, string DisplayName, string Password);

/// <summary>Một việc bạn vừa làm (lấy từ nhật ký nhóm) — hiện trong thẻ tổng kết sau khi vào hệ thống.</summary>
public sealed record ActivityRecapResponse(
    DateTimeOffset At,
    string Action,
    string Area,
    string Label,
    string? Details);

public sealed record RecapListResponse(IReadOnlyList<ActivityRecapResponse> Items);

/// <summary>Yêu cầu đổi mật khẩu cho tài khoản đang đăng nhập.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Yêu cầu cập nhật thông tin cá nhân (tên hiển thị).</summary>
public sealed record UpdateProfileRequest(string DisplayName);

/// <summary>Thống kê chi tiết tài khoản cho trang cá nhân.</summary>
public sealed record ProfileStatsResponse(
    Guid Id,
    string Username,
    string DisplayName,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    int AvatarVersion,
    int TotalActions,
    int ChatMessagesCount,
    int SummarySessionsCount);
