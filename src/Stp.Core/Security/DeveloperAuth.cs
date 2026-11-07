namespace Stp.Core.Security;

/// <summary>
/// Cấu hình tài khoản developer. Mật khẩu có thể đặt sẵn qua appsettings
/// (ưu tiên cao nhất) hoặc thiết lập lần đầu và lưu trong data/runtime.
/// </summary>
public sealed class DeveloperAuthOptions
{
    public const string SectionName = "DeveloperAuth";

    /// <summary>Chuỗi hash PBKDF2 do công cụ tạo; để trống nếu thiết lập qua giao diện.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Khóa ký JWT. Để trống thì hệ thống tự sinh và lưu vào state file.</summary>
    public string? JwtKey { get; set; }

    public string Issuer { get; set; } = "StpArchive.Developer";

    public string Audience { get; set; } = "StpArchive.DeveloperUi";

    public int TokenLifetimeMinutes { get; set; } = 480;

    /// <summary>Đường dẫn state file; mặc định data/runtime/developer-auth.json.</summary>
    public string? StateFilePath { get; set; }

    public int MaxFailedAttempts { get; set; } = 5;

    public int FailedAttemptWindowMinutes { get; set; } = 10;
}

public sealed class DeveloperAuthStatus
{
    public bool SetupRequired { get; init; }

    public bool Configured { get; init; }

    /// <summary>AppSettings, LocalFile hoặc None.</summary>
    public string Source { get; init; } = "None";
}

public sealed record DeveloperSession(
    string Token,
    DateTimeOffset ExpiresAt,
    string Username);

public enum DeveloperLoginStatus
{
    Success = 0,
    InvalidPassword = 1,
    Throttled = 2,
    NotConfigured = 3
}

public sealed record DeveloperLoginResult(
    DeveloperLoginStatus Status,
    DeveloperSession? Session = null,
    TimeSpan? RetryAfter = null);

/// <summary>Xác thực developer cục bộ: PBKDF2 + JWT, không cần tài khoản Windows.</summary>
public interface IDeveloperAuthService
{
    DeveloperAuthStatus GetStatus();

    Task<DeveloperSession> SetupAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task<DeveloperLoginResult> LoginAsync(
        string password,
        string? clientKey,
        CancellationToken cancellationToken = default);

    /// <summary>Khóa ký/kiểm tra JWT, ổn định sau khi khởi động lại.</summary>
    byte[] GetSigningKey();

    string Issuer { get; }

    string Audience { get; }

    int TokenLifetimeMinutes { get; }
}
