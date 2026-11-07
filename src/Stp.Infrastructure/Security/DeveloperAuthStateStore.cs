using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stp.Core.Security;

namespace Stp.Infrastructure.Security;

/// <summary>Nội dung state file cục bộ của developer auth.</summary>
public sealed class DeveloperAuthState
{
    public string? PasswordHash { get; set; }

    /// <summary>Khóa JWT dạng Base64.</summary>
    public string? JwtKey { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Đọc/ghi state file và các hàm băm mật khẩu dùng chung giữa lúc cấu hình JWT
/// trong Program.cs và service xác thực.
/// </summary>
public static class DeveloperAuthStateStore
{
    public const string DefaultRelativePath = "data/runtime/developer-auth.json";

    private const int Pbkdf2Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string HashPrefix = "PBKDF2-SHA256";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ResolveStatePath(DeveloperAuthOptions options, string contentRoot)
    {
        var configured = options.StateFilePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(
                Path.IsPathRooted(configured)
                    ? configured
                    : Path.Combine(contentRoot, configured));
        }

        return Path.GetFullPath(Path.Combine(contentRoot, DefaultRelativePath));
    }

    public static DeveloperAuthState? TryLoad(string statePath)
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return null;
            }

            var json = File.ReadAllText(statePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<DeveloperAuthState>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string statePath, DeveloperAuthState state)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var temporary = statePath + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, statePath, overwrite: true);
    }

    public static string HashPassword(string password, int iterations = Pbkdf2Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return string.Join(
            '$',
            HashPrefix,
            iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(string password, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        var parts = encoded.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], HashPrefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var iterations)
            || iterations < 10_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Trả về khóa ký JWT ổn định: ưu tiên appsettings, sau đó state file,
    /// cuối cùng sinh mới và lưu lại để token còn hiệu lực sau khi restart.
    /// </summary>
    public static byte[] ResolveSigningKey(DeveloperAuthOptions options, string statePath)
    {
        if (!string.IsNullOrWhiteSpace(options.JwtKey))
        {
            return NormalizeKey(options.JwtKey);
        }

        var state = TryLoad(statePath);
        if (!string.IsNullOrWhiteSpace(state?.JwtKey))
        {
            try
            {
                var bytes = Convert.FromBase64String(state.JwtKey);
                if (bytes.Length >= 32)
                {
                    return bytes;
                }
            }
            catch (FormatException)
            {
                // State file hỏng; sinh khóa mới bên dưới.
            }
        }

        var key = RandomNumberGenerator.GetBytes(48);
        state ??= new DeveloperAuthState();
        state.JwtKey = Convert.ToBase64String(key);
        state.CreatedAt ??= DateTimeOffset.UtcNow;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        Save(statePath, state);
        return key;
    }

    public static byte[] NormalizeKey(string rawKey)
    {
        // Bất kỳ chuỗi cấu hình nào cũng được chuẩn hóa thành 32 byte ngẫu nhiên hiệu dụng.
        return rawKey.StartsWith("base64:", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(rawKey["base64:".Length..])
            : SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
    }

    public static bool IsStrongPassword(string password, out string? error)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
        {
            error = "Mật khẩu phải có ít nhất 10 ký tự.";
            return false;
        }

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        if (!hasLetter || !hasDigit)
        {
            error = "Mật khẩu phải có ít nhất một chữ cái và một chữ số.";
            return false;
        }

        error = null;
        return true;
    }
}
