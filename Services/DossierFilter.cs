using System.Globalization;
using System.Text.RegularExpressions;
using StpWeb.Models;

namespace StpWeb.Services;

/// <summary>
/// Mô phỏng đúng logic lọc của macro VBA trong file Excel:
/// - Tất cả hồ sơ
/// - Từ số - đến số (tự đảo nếu nhập ngược)
/// - Các số tùy chọn: 85, 90, 100-105 (hỗ trợ dấu gạch en/em, dấu ;, khoảng trắng)
/// Nếu không có hồ sơ nào khớp, tự hiện lại tất cả và báo lỗi như macro.
///
/// Bộ lọc có thể thu hẹp trước theo loại hồ sơ (Type) và nhóm (Group),
/// sau đó mới áp dụng cách chọn số.
/// </summary>
public sealed class DossierFilter
{
    private static readonly Regex RangeTokenRegex = new(@"^(\d+)\s*-\s*(\d+)$", RegexOptions.Compiled);
    private static readonly Regex SingleTokenRegex = new(@"^\d+$", RegexOptions.Compiled);

    public FilterResult Apply(
        ArchiveBook book,
        string? mode,
        int? from,
        int? to,
        string? custom,
        string? type = null,
        string? group = null)
    {
        var scoped = book.Dossiers.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(type))
        {
            scoped = scoped.Where(d => string.Equals(d.Type, type.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            scoped = scoped.Where(d => string.Equals(d.Group, group.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var all = scoped.ToList();
        var normalizedMode = (mode ?? "all").Trim().ToLowerInvariant();
        var scopeText = BuildScopeText(type, group);

        if (all.Count == 0)
        {
            return new FilterResult
            {
                Dossiers = all,
                Total = 0,
                StatusMessage = $"Năm {book.Name} không có hồ sơ{scopeText}.",
                Warning = string.IsNullOrWhiteSpace(scopeText)
                    ? "⚠ Năm này chưa có hồ sơ nào."
                    : $"⚠ Không có hồ sơ{scopeText} trong năm {book.Name}."
            };
        }

        return normalizedMode switch
        {
            "range" => ApplyRange(book, all, from, to, scopeText),
            "custom" => ApplyCustom(book, all, custom, scopeText),
            _ => ApplyAll(book, all, scopeText)
        };
    }

    private static string BuildScopeText(string? type, string? group)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(type))
        {
            parts.Add($"loại “{type.Trim()}”");
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            parts.Add($"nhóm “{group.Trim()}”");
        }

        return parts.Count == 0 ? string.Empty : " · " + string.Join(" · ", parts);
    }

    private static string AllStatus(ArchiveBook book, IReadOnlyList<Dossier> all, string scopeText)
    {
        var range = all.Count == 0
            ? "không có hồ sơ"
            : $"hồ sơ số từ {all.Min(d => d.Number)} đến {all.Max(d => d.Number)}";
        return $"Đang hiện {all.Count}/{all.Count} hồ sơ{scopeText} (tất cả) · Năm {book.Name} có {book.Count} hồ sơ · {range}";
    }

    private static FilterResult ApplyAll(ArchiveBook book, IReadOnlyList<Dossier> all, string scopeText)
    {
        return new FilterResult
        {
            Dossiers = all,
            Total = all.Count,
            StatusMessage = AllStatus(book, all, scopeText)
        };
    }

    private static FilterResult ApplyRange(
        ArchiveBook book,
        IReadOnlyList<Dossier> all,
        int? from,
        int? to,
        string scopeText)
    {
        if (from is null || to is null)
        {
            return new FilterResult
            {
                Dossiers = all,
                Total = all.Count,
                StatusMessage = AllStatus(book, all, scopeText),
                Warning = "⚠ Hãy nhập số vào cả hai ô “Từ số” và “Đến số”."
            };
        }

        var lo = Math.Min(from.Value, to.Value);
        var hi = Math.Max(from.Value, to.Value);
        var matches = all.Where(d => d.Number >= lo && d.Number <= hi).ToList();

        if (matches.Count == 0)
        {
            return new FilterResult
            {
                Dossiers = all,
                Total = all.Count,
                StatusMessage = AllStatus(book, all, scopeText),
                Warning = $"⚠ Không có hồ sơ nào trong khoảng {lo} – {hi}{scopeText}. Đã hiện lại tất cả."
            };
        }

        return new FilterResult
        {
            Dossiers = matches,
            Total = all.Count,
            StatusMessage = $"Đang chọn từ số {lo} đến số {hi}: {matches.Count} hồ sơ{scopeText} · Năm {book.Name} có {all.Count} hồ sơ trong phạm vi đang xét"
        };
    }

    private static FilterResult ApplyCustom(
        ArchiveBook book,
        IReadOnlyList<Dossier> all,
        string? custom,
        string scopeText)
    {
        var text = (custom ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return new FilterResult
            {
                Dossiers = all,
                Total = all.Count,
                StatusMessage = AllStatus(book, all, scopeText),
                Warning = "⚠ Hãy gõ các số cần in vào ô “Các số cần in”, ví dụ: 85, 90, 100-105."
            };
        }

        text = text
            .Replace('\u2013', '-') // en dash
            .Replace('\u2014', '-') // em dash
            .Replace(';', ',');

        text = Regex.Replace(text, @"\s*-\s*", "-");
        var tokens = text.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var ranges = new List<(int Lo, int Hi)>();
        var singles = new List<int>();

        foreach (var token in tokens)
        {
            var rangeMatch = RangeTokenRegex.Match(token);
            if (rangeMatch.Success
                && int.TryParse(rangeMatch.Groups[1].Value, out var lo)
                && int.TryParse(rangeMatch.Groups[2].Value, out var hi))
            {
                ranges.Add((Math.Min(lo, hi), Math.Max(lo, hi)));
                continue;
            }

            if (SingleTokenRegex.IsMatch(token)
                && int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var single))
            {
                ranges.Add((single, single));
                singles.Add(single);
            }
        }

        if (ranges.Count == 0)
        {
            return new FilterResult
            {
                Dossiers = all,
                Total = all.Count,
                StatusMessage = AllStatus(book, all, scopeText),
                Warning = "⚠ Hãy gõ các số cần in vào ô “Các số cần in”, ví dụ: 85, 90, 100-105."
            };
        }

        var matches = all
            .Where(d => ranges.Any(r => d.Number >= r.Lo && d.Number <= r.Hi))
            .ToList();

        var existingNumbers = all.Select(d => d.Number).ToHashSet();
        var notFound = singles
            .Where(n => !existingNumbers.Contains(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (matches.Count == 0)
        {
            var warning = $"⚠ Không có hồ sơ nào khớp{scopeText} – đã hiện lại tất cả.";
            if (notFound.Count > 0)
            {
                warning += " ⚠ Không tìm thấy hồ sơ số: " + string.Join(", ", notFound);
            }

            return new FilterResult
            {
                Dossiers = all,
                Total = all.Count,
                StatusMessage = AllStatus(book, all, scopeText),
                Warning = warning,
                NotFound = notFound
            };
        }

        return new FilterResult
        {
            Dossiers = matches,
            Total = all.Count,
            StatusMessage = $"Đang chọn: {matches.Count} hồ sơ chỉ định{scopeText} (Theo chuỗi: {text}) · Năm {book.Name} có {all.Count} hồ sơ trong phạm vi đang xét",
            Warning = notFound.Count > 0
                ? "⚠ Không tìm thấy hồ sơ số: " + string.Join(", ", notFound)
                : null,
            NotFound = notFound
        };
    }
}
