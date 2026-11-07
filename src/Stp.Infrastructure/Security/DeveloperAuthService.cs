using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Stp.Core.Security;

namespace Stp.Infrastructure.Security;

/// <summary>
/// Xác thực developer cục bộ. Mật khẩu ưu tiên lấy từ appsettings; nếu trống
/// thì dùng hash trong data/runtime/developer-auth.json (tạo ở lần thiết lập đầu).
/// </summary>
public sealed class DeveloperAuthService : IDeveloperAuthService
{
    private const string Username = "developer";
    private const string Role = "Developer";

    private readonly DeveloperAuthOptions _options;
    private readonly string _statePath;
    private readonly ILogger<DeveloperAuthService> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _failedAttempts =
        new(StringComparer.OrdinalIgnoreCase);

    public DeveloperAuthService(
        IOptions<DeveloperAuthOptions> options,
        IHostEnvironment environment,
        ILogger<DeveloperAuthService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _statePath = DeveloperAuthStateStore.ResolveStatePath(
            _options,
            environment.ContentRootPath);
    }

    public string Issuer => _options.Issuer;

    public string Audience => _options.Audience;

    public int TokenLifetimeMinutes =>
        Math.Clamp(_options.TokenLifetimeMinutes, 5, 60 * 24 * 30);

    public DeveloperAuthStatus GetStatus()
    {
        var appSettingsHash = _options.PasswordHash;
        if (!string.IsNullOrWhiteSpace(appSettingsHash))
        {
            return new DeveloperAuthStatus
            {
                SetupRequired = false,
                Configured = true,
                Source = "AppSettings"
            };
        }

        lock (_sync)
        {
            var state = DeveloperAuthStateStore.TryLoad(_statePath);
            var configured = !string.IsNullOrWhiteSpace(state?.PasswordHash);
            return new DeveloperAuthStatus
            {
                SetupRequired = !configured,
                Configured = configured,
                Source = configured ? "LocalFile" : "None"
            };
        }
    }

    public async Task<DeveloperSession> SetupAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!DeveloperAuthStateStore.IsStrongPassword(password, out var error))
        {
            throw new ArgumentException(error ?? "Mật khẩu không hợp lệ.", nameof(password));
        }

        if (!string.IsNullOrWhiteSpace(_options.PasswordHash))
        {
            throw new InvalidOperationException(
                "Mật khẩu developer đã được cấu hình trong appsettings; không thể thiết lập lại từ giao diện.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        DeveloperSession session;
        lock (_sync)
        {
            var state = DeveloperAuthStateStore.TryLoad(_statePath);
            if (!string.IsNullOrWhiteSpace(state?.PasswordHash))
            {
                throw new InvalidOperationException(
                    "Mật khẩu developer đã được thiết lập trên máy này.");
            }

            var signingKey = DeveloperAuthStateStore.ResolveSigningKey(_options, _statePath);
            state ??= new DeveloperAuthState();
            state.PasswordHash = DeveloperAuthStateStore.HashPassword(password);
            state.JwtKey = Convert.ToBase64String(signingKey);
            state.CreatedAt ??= DateTimeOffset.UtcNow;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            DeveloperAuthStateStore.Save(_statePath, state);

            session = CreateSession(signingKey);
        }

        _logger.LogInformation("Đã thiết lập tài khoản developer cục bộ.");
        await Task.CompletedTask.ConfigureAwait(false);
        return session;
    }

    public async Task<DeveloperLoginResult> LoginAsync(
        string password,
        string? clientKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = string.IsNullOrWhiteSpace(clientKey) ? "local" : clientKey;
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            if (IsThrottled(key, now, out var retryAfter))
            {
                return new DeveloperLoginResult(
                    DeveloperLoginStatus.Throttled,
                    RetryAfter: retryAfter);
            }
        }

        var hash = ResolvePasswordHash();
        if (string.IsNullOrWhiteSpace(hash))
        {
            return new DeveloperLoginResult(DeveloperLoginStatus.NotConfigured);
        }

        var verified = await Task.Run(
            () => DeveloperAuthStateStore.VerifyPassword(password ?? string.Empty, hash),
            cancellationToken).ConfigureAwait(false);

        if (!verified)
        {
            lock (_sync)
            {
                RecordFailure(key, now);
            }

            _logger.LogWarning("Đăng nhập developer thất bại từ {Client}.", key);
            return new DeveloperLoginResult(DeveloperLoginStatus.InvalidPassword);
        }

        lock (_sync)
        {
            _failedAttempts.Remove(key);
        }

        var signingKey = GetSigningKey();
        return new DeveloperLoginResult(
            DeveloperLoginStatus.Success,
            CreateSession(signingKey));
    }

    public byte[] GetSigningKey()
    {
        lock (_sync)
        {
            return DeveloperAuthStateStore.ResolveSigningKey(_options, _statePath);
        }
    }

    private string? ResolvePasswordHash()
    {
        if (!string.IsNullOrWhiteSpace(_options.PasswordHash))
        {
            return _options.PasswordHash;
        }

        lock (_sync)
        {
            return DeveloperAuthStateStore.TryLoad(_statePath)?.PasswordHash;
        }
    }

    private DeveloperSession CreateSession(byte[] signingKey)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(TokenLifetimeMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(signingKey),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Username),
            new(JwtRegisteredClaimNames.Name, "Developer"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("role", Role)
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new DeveloperSession(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            "Developer");
    }

    private bool IsThrottled(string key, DateTimeOffset now, out TimeSpan? retryAfter)
    {
        retryAfter = null;
        if (!_failedAttempts.TryGetValue(key, out var attempts) || attempts.Count == 0)
        {
            return false;
        }

        var window = TimeSpan.FromMinutes(
            Math.Clamp(_options.FailedAttemptWindowMinutes, 1, 1440));
        var threshold = now - window;

        while (attempts.Count > 0 && attempts.Peek() < threshold)
        {
            attempts.Dequeue();
        }

        var maxAttempts = Math.Clamp(_options.MaxFailedAttempts, 1, 100);
        if (attempts.Count < maxAttempts)
        {
            return false;
        }

        retryAfter = attempts.Peek() + window - now;
        if (retryAfter < TimeSpan.Zero)
        {
            retryAfter = TimeSpan.Zero;
        }

        return true;
    }

    private void RecordFailure(string key, DateTimeOffset now)
    {
        if (!_failedAttempts.TryGetValue(key, out var attempts))
        {
            attempts = new Queue<DateTimeOffset>();
            _failedAttempts[key] = attempts;
        }

        attempts.Enqueue(now);

        // Dọn bớt để dictionary không phình khi bị dò mật khẩu.
        if (_failedAttempts.Count > 1000)
        {
            var stale = _failedAttempts
                .Where(pair => pair.Value.Count == 0
                               || pair.Value.Peek() < now.AddHours(-1))
                .Select(pair => pair.Key)
                .ToList();

            foreach (var staleKey in stale)
            {
                _failedAttempts.Remove(staleKey);
            }
        }
    }
}
