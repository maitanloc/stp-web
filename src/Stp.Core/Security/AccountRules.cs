using System.Text.RegularExpressions;

namespace Stp.Core.Security;

/// <summary>
/// Quy tắc dữ liệu tài khoản nhóm — backend và unit test cùng dùng để
/// thông báo lỗi tiếng Việt thống nhất một chỗ duy nhất.
/// </summary>
public static partial class AccountRules
{
    public const string RoleAdmin = "admin";
    public const string RoleMember = "member";

    public const int MinUsername = 3;
    public const int MaxUsername = 50;
    public const int MinPassword = 6;
    public const int MaxPassword = 128;
    public const int MinDisplayName = 2;
    public const int MaxDisplayName = 100;

    /// <summary>Username chỉ chữ/số, dấu chấm, gạch nối — dễ gõ, tránh rắc rối dấu tiếng Việt khi đăng nhập.</summary>
    [GeneratedRegex("^[a-zA-Z0-9._-]+$")]
    private static partial Regex UsernamePattern();

    public static IReadOnlyList<string> ValidateUsername(string? username)
    {
        var errors = new List<string>();
        var trimmed = (username ?? string.Empty).Trim();
        if (trimmed.Length < MinUsername)
        {
            errors.Add($"Tên đăng nhập cần tối thiểu {MinUsername} ký tự.");
        }
        else if (trimmed.Length > MaxUsername)
        {
            errors.Add($"Tên đăng nhập tối đa {MaxUsername} ký tự.");
        }
        else if (!UsernamePattern().IsMatch(trimmed))
        {
            errors.Add("Tên đăng nhập chỉ gồm chữ không dấu, số, dấu chấm, gạch nối (_) hoặc gạch ngang (-).");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidatePassword(string? password)
    {
        var errors = new List<string>();
        var value = password ?? string.Empty;
        if (value.Length < MinPassword)
        {
            errors.Add($"Mật khẩu cần tối thiểu {MinPassword} ký tự.");
        }
        else if (value.Length > MaxPassword)
        {
            errors.Add($"Mật khẩu tối đa {MaxPassword} ký tự.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateDisplayName(string? displayName)
    {
        var errors = new List<string>();
        var trimmed = (displayName ?? string.Empty).Trim();
        if (trimmed.Length < MinDisplayName)
        {
            errors.Add($"Tên hiển thị cần tối thiểu {MinDisplayName} ký tự.");
        }
        else if (trimmed.Length > MaxDisplayName)
        {
            errors.Add($"Tên hiển thị tối đa {MaxDisplayName} ký tự.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateRole(string? role)
    {
        var errors = new List<string>();
        if (role != RoleAdmin && role != RoleMember)
        {
            errors.Add($"Quyền chỉ được là \"{RoleAdmin}\" hoặc \"{RoleMember}\".");
        }

        return errors;
    }
}
