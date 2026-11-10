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
    /// Đếm tần suất từng loại giấy tờ trong toàn bộ trích yếu đã đồng bộ,
    /// nạp những mục đạt <paramref name="minCount"/> lần vào CatalogItems.
    /// Idempotent: mục đã có chỉ cập nhật UsageCount khi lớn hơn.
    /// </summary>
    public static (int Added, int Updated) SeedFromArchive(
        IDbContextFactory<StpDbContext> factory,
        int minCount = 50)
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

        var added = 0;
        var updated = 0;
        using var save = factory.CreateDbContext();
        foreach (var (key, (usage, display)) in counts)
        {
            if (usage < minCount)
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
