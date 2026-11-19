using System.Text.RegularExpressions;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Chuẩn hoá phần mở đầu của trích yếu hồ sơ đăng ký kết hôn và các hồ sơ hành chính tư pháp.
/// Tiêu chuẩn đúng: bắt đầu bằng "Về việc..." (ví dụ: "Về việc đăng ký kết hôn của...").
/// Tuyệt đối không dùng "Hồ sơ về việc..." (sai).
/// </summary>
internal static class MarriageSummaryNormalizer
{
    public const string StandardPrefix = "Về việc đăng ký kết hôn";

    private static readonly Regex MarriagePrefixPattern = new(
        @"^\s*(?:"
        + @"Hồ\s+sơ\s+(?:về\s+việc\s+)?đăng\s+ký\s+kết\s+hôn"
        + @"(?:\s+(?:có\s+yếu\s+tố\s+nước\s+ngoài|với\s+người\s+nước\s+ngoài))?"
        + @"|Quyết\s+định\s+về\s+việc\s+cho\s+phép\s+công\s+(?:nhân|dân)\s+Việt\s+Nam\s+đăng\s+ký\s+kết\s+hôn\s+với\s+người\s+nước\s+ngoài"
        + @"|Về\s+việc\s+đăng\s+ký\s+kết\s+hôn(?:\s+(?:có\s+yếu\s+tố\s+nước\s+ngoài|với\s+người\s+nước\s+ngoài))?"
        + @")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HoSoVeViecPattern = new(
        @"^\s*Hồ\s+sơ\s+về\s+việc\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SeparatorPattern = new(
        @"\s+của\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PersonStartPattern = new(
        @"^(?:bà|ông)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Dòng trích yếu có thuộc nhóm đăng ký kết hôn hay không.</summary>
    public static bool IsMarriageRegistration(string? text) =>
        !string.IsNullOrWhiteSpace(text) && MarriagePrefixPattern.IsMatch(text);

    /// <summary>True khi câu chưa đúng chuẩn và cần chuẩn hoá lại đầu dữ liệu.</summary>
    public static bool NeedsNormalization(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var normalized = NormalizeIfNeeded(trimmed);
        return !string.Equals(normalized, trimmed, StringComparison.Ordinal);
    }

    /// <summary>Trả về câu đã chuẩn hoá; giữ nguyên phần sau "của" và danh mục giấy tờ.</summary>
    public static string NormalizeIfNeeded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var trimmed = text.Trim();

        // 1. Nhóm đăng ký kết hôn: chuẩn hoá về "Về việc đăng ký kết hôn của..."
        var match = MarriagePrefixPattern.Match(trimmed);
        if (match.Success)
        {
            return NormalizeMarriageCore(trimmed, match);
        }

        // 2. Các nhóm hồ sơ khác có chữ "Hồ sơ về việc" -> đổi thành "Về việc"
        if (HoSoVeViecPattern.IsMatch(trimmed))
        {
            return HoSoVeViecPattern.Replace(trimmed, "Về việc", 1);
        }

        return trimmed;
    }

    private static string NormalizeMarriageCore(string text, Match match)
    {
        var separator = SeparatorPattern.Match(text, match.Length);
        if (separator.Success)
        {
            var suffix = text[(separator.Index + separator.Length)..].TrimStart();
            return suffix.Length == 0
                ? StandardPrefix
                : StandardPrefix + " của " + suffix;
        }

        var rest = text[match.Length..].Trim();
        if (rest.Length == 0)
        {
            return StandardPrefix;
        }

        // Một số dòng cũ thiếu chữ "của" ngay trước tên người.
        return PersonStartPattern.IsMatch(rest)
            ? StandardPrefix + " của " + rest
            : StandardPrefix + " " + rest;
    }
}
