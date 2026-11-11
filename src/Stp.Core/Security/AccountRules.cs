using System.Text;
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

    /// <summary>
    /// Chuẩn hoá Unicode NFC cho tên đăng nhập/mật khẩu trước khi kiểm tra và
    /// hash: bàn phím iOS/macOS/bộ gõ có thể xuất dấu ở dạng ghép (e + dấu sắc
    /// thành 2 ký tự) trong khi nhìn không khác dạng dựng sẵn — không chuẩn hoá
    /// thì hai lúc gõ ra hai chuỗi byte khác nhau và đăng nhập tự dưng sai.
    /// </summary>
    public static string NormalizeInput(string value) => value.Normalize(NormalizationForm.FormC);

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
        else if (value.Any(static character => character > 127))
        {
            // Chặn mật khẩu có dấu tiếng Việt ngay từ lúc tạo: bộ gõ Telex/VNI
            // biến chữ thường thành có dấu ngay khi gõ (vd "her" → "hẻ"), và
            // mỗi bàn phím xuất dấu một kiểu — đăng nhập sau này dễ sai không
            // cách nào hiểu được. Mật khẩu chỉ dùng chữ không dấu, số, ký tự
            // đặc biệt; nhập bằng bộ gõ tiếng Anh (thanh En trên taskbar).
            errors.Add("Mật khẩu chỉ dùng chữ không dấu, số và ký tự đặc biệt — tắt bộ gõ tiếng Việt (Telex/VNI) khi nhập mật khẩu.");
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
