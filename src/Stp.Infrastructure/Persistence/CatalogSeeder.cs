using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Stp.Core.Domain;
using Stp.Core.Summary;

namespace Stp.Infrastructure.Persistence;

/// <summary>
/// Nạp một lần "danh mục học được" từ kho hồ sơ trong database — tầng 2 của
/// cơ chế đa dạng hoá: item tự đếm tần suất trong 67k+ trích yếu, mức
/// <paramref name="minCount"/> (mặc định 50) nạp thẳng Source=Learned,
/// rác kiểu tên người (count ≤ 2) tự lọc nhờ ngưỡng.
/// </summary>
public static class CatalogSeeder
{
    /// <summary>
    /// Từ khóa nhận diện "thứ đọc ra như một loại giấy tờ" — dùng để lọc nhiễu khi
    /// nạp band hẹp (2–49): tên người ("NGUYEN TRI", "LE PASCAL") không chứa kiểu
    /// từ khóa nào nên bị loại; loại thật thường chứa ít nhất một từ này.
    /// </summary>
    private static readonly string[] LabelKeywords =
    {
        "giay", "don", "phieu", "chung", "khai", "ho khau", "cmnd", "cccd", "cong ham",
        "thi thuc", "ho chieu", "so yeu", "trich luc", "quyet dinh", "bien nhan",
        "xac nhan", "chuyen khoa", "kham suc khoe", "dang ky", "ban sao", "bien ban",
        "bien lai", "thong bao", "hoan tra", "ly lich", "tuong trinh",
        // Bổ sung cho các họ người dùng chỉ định: cam kết, căn cước, công chứng.
        "cam ket", "can cuoc", "cong chung", "to khai", "ban khai", "nuoi con"
    };

    /// <summary>
    /// Đếm tần suất từng loại giấy tờ trong toàn bộ trích yếu đã đồng bộ,
    /// nạp những mục đạt <paramref name="minCount"/> lần vào CatalogItems.
    /// Idempotent: mục đã có chỉ cập nhật UsageCount khi lớn hơn.
    /// Khi có <paramref name="maxCount"/> (band review 2–49), chỉ nạp mục trong
    /// khoảng [minCount, maxCount] và có dấu hiệu là loại giấy tờ (LabelKeywords).
    /// </summary>
    public static (int Added, int Updated) SeedFromArchive(
        IDbContextFactory<StpDbContext> factory,
        int minCount = 50,
        int? maxCount = null)
    {
        var counts = ComputeCounts(factory);

        var added = 0;
        var updated = 0;
        using var save = factory.CreateDbContext();
        foreach (var (key, (usage, display)) in counts)
        {
            if (usage < minCount || (maxCount.HasValue && usage > maxCount.Value))
            {
                continue;
            }

            if (maxCount.HasValue && !LooksLikeDocumentLabel(key))
            {
                continue;
            }

            var item = save.CatalogItems.FirstOrDefault(candidate => candidate.NormalizedKey == key);
            if (item is null)
            {
                save.CatalogItems.Add(new CatalogItem
                {
                    Id = Guid.NewGuid(),
                    NormalizedKey = key,
                    DisplayName = display,
                    Source = "Learned",
                    UsageCount = usage,
                    LastUsedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                added++;
            }
            else if (usage > item.UsageCount)
            {
                item.UsageCount = usage;
                item.LastUsedAt = DateTime.UtcNow;
                updated++;
            }
        }

        save.SaveChanges();
        return (added, updated);
    }

    /// <summary>
    /// Đề xuất nạp cho band review [minCount, maxCount] — chỉ liệt kê (không ghi DB):
    /// mục chưa có trong CatalogItems, đếm từ dữ liệu thật, lọc nhiễu theo từ khóa.
    /// Dùng cho panel dev review; gọi lại seed với maxCount để nạp phần đã duyệt.
    /// </summary>
    public static List<(string DisplayName, int Count)> Suggest(
        IDbContextFactory<StpDbContext> factory,
        int minCount,
        int maxCount,
        int take = 300)
    {
        using var db = factory.CreateDbContext();
        var existingKeys = db.CatalogItems
            .Select(item => item.NormalizedKey)
            .ToHashSet(StringComparer.Ordinal);

        var counts = ComputeCounts(factory);
        var result = counts
            .Where(pair => pair.Value.Usage >= minCount
                           && pair.Value.Usage <= maxCount
                           && LooksLikeDocumentLabel(pair.Key)
                           && !existingKeys.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value.Usage)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(take)
            .Select(pair => (pair.Value.Display, pair.Value.Usage))
            .ToList();

        return result;
    }

    private static Dictionary<string, (int Usage, string Display)> ComputeCounts(
        IDbContextFactory<StpDbContext> factory)
    {
        List<string> summaries;
        using (var db = factory.CreateDbContext())
        {
            summaries = db.DocumentLines
                .Where(line => line.TrichYeu != null && line.TrichYeu != "")
                .Select(line => line.TrichYeu!)
                .ToList();
        }

        var counts = new Dictionary<string, (int Usage, string Display)>(StringComparer.Ordinal);
        foreach (var summary in summaries)
        {
            var group = SplitTrailingGroup(summary);
            if (group is null)
            {
                continue;
            }

            foreach (var raw in SplitItems(group.Value.Inside))
            {
                var display = raw;
                var key = CatalogKey.Normalize(display);
                if (key.Length == 0)
                {
                    continue;
                }

                if (counts.TryGetValue(key, out var current))
                {
                    counts[key] = (current.Usage + 1, current.Display);
                }
                else
                {
                    counts[key] = (1, display);
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// Mục hành nghề "như một loại giấy tờ": chứa ít nhất một từ khóa nhận diện
    /// (giấy, đơn, phiếu, cam kết…). Chặn mục rác dạng tên người, câu rời rạc.
    /// </summary>
    private static bool LooksLikeDocumentLabel(string normalizedKey) =>
        LabelKeywords.Any(keyword => normalizedKey.Contains(keyword, StringComparison.Ordinal));

    /// <summary>
    /// Đoạn ngoặc cuối trong "Câu (giấy tờ, giấy tờ)" — replicate đúng logic
    /// SplitTrailingGroup của SummaryToolService (không nhận ngoặc lồng).
    /// </summary>
    private static (int OpenIndex, string Inside)? SplitTrailingGroup(string text)
    {
        if (!text.EndsWith(')'))
        {
            return null;
        }

        var depth = 0;
        var openIndex = -1;

        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (text[index] == ')')
            {
                depth++;
            }
            else if (text[index] == '(')
            {
                depth--;
                if (depth == 0)
                {
                    openIndex = index;
                    break;
                }
            }
        }

        if (openIndex < 0)
        {
            return null;
        }

        var inside = text[(openIndex + 1)..^1];
        if (inside.Contains('(') || inside.Contains(')'))
        {
            return null;
        }

        return (openIndex, inside);
    }

    private static IEnumerable<string> SplitItems(string inside) =>
        inside
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => Regex.Replace(item, @"\s+", " "))
            .Where(item => item.Length > 0);
}
