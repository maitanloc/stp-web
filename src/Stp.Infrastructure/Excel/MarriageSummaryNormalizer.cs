using System.Text.RegularExpressions;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Chuẩn hoá phần mở đầu của trích yếu hồ sơ đăng ký kết hôn. Chỉ áp dụng cho
/// đúng nhóm đăng ký kết hôn; các nhóm ghi chú/trích lục/cấp giấy được giữ nguyên.
/// </summary>
internal static class MarriageSummaryNormalizer
{
    public const string StandardPrefix =
        "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn với người nước ngoài";

    private static readonly Regex MarriagePrefixPattern = new(
        @"^\s*(?:"
        + @"Hồ\s+sơ\s+(?:về\s+việc\s+)?đăng\s+ký\s+kết\s+hôn"
        + @"(?:\s+(?:có\s+yếu\s+tố\s+nước\s+ngoài|với\s+người\s+nước\s+ngoài))?"
        + @"|Quyết\s+định\s+về\s+việc\s+cho\s+phép\s+công\s+(?:nhân|dân)\s+Việt\s+Nam"
        + @"\s+đăng\s+ký\s+kết\s+hôn\s+với\s+người\s+nước\s+ngoài"
        + @")",
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

    /// <summary>True khi câu đã đúng chuẩn và không cần ghi lại.</summary>
    public static bool NeedsNormalization(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var match = MarriagePrefixPattern.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        return !string.Equals(NormalizeCore(trimmed, match), trimmed, StringComparison.Ordinal);
    }

    /// <summary>Trả về câu đã chuẩn hoá; giữ nguyên phần sau "của" và danh mục giấy tờ.</summary>
    public static string NormalizeIfNeeded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var trimmed = text.Trim();
        var match = MarriagePrefixPattern.Match(trimmed);
        return match.Success ? NormalizeCore(trimmed, match) : trimmed;
    }

    private static string NormalizeCore(string text, Match match)
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
