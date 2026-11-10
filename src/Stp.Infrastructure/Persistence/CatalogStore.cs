using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Stp.Core.Domain;
using Stp.Core.Summary;

namespace Stp.Infrastructure.Persistence;

/// <summary>
/// Lưu bền "danh mục học được" trên SQL Server. Chạy độc lập vòng đời request:
/// được tạo qua <c>IDbContextFactory</c> để dùng được trong singleton
/// <see cref="ISummaryToolService"/> mà không khoá DbContext scoped.
/// </summary>
public sealed class CatalogStore : ICatalogDictionary
{
    private readonly IDbContextFactory<StpDbContext> _factory;

    public CatalogStore(IDbContextFactory<StpDbContext> factory)
    {
        _factory = factory;
    }

    public IReadOnlyList<CatalogEntry> GetActive()
    {
        using var db = _factory.CreateDbContext();
        var items = db.CatalogItems
            .Where(item => !item.IsDisabled)
            .OrderByDescending(item => item.UsageCount)
            .ThenByDescending(item => item.LastUsedAt)
            .ThenBy(item => item.DisplayName)
            .Select(item => new CatalogEntry(
                item.Id,
                item.DisplayName,
                item.NormalizedKey,
                item.Source == "User" ? CatalogEntrySource.User : CatalogEntrySource.Learned,
                item.UsageCount,
                item.LastUsedAt,
                item.DossierTypeHint))
            .Take(2000)
            .ToList();

        return items;
    }

    public CatalogEntry? Find(string normalizedKey)
    {
        var key = CatalogKey.Normalize(normalizedKey);
        if (key.Length == 0)
        {
            return null;
        }

        using var db = _factory.CreateDbContext();
        var item = db.CatalogItems
            .FirstOrDefault(item => item.NormalizedKey == key && !item.IsDisabled);
        return item is null ? null : ToEntry(item);
    }

    public CatalogEntry Upsert(
        string displayName,
        string? dossierTypeHint,
        CatalogEntrySource source,
        int? usageCount = null)
    {
        var display = CatalogKey.NormalizeDisplay(displayName);
        var key = CatalogKey.Normalize(display);
        if (key.Length == 0)
        {
            throw new ArgumentException("Tên loại giấy tờ không được rỗng.", nameof(displayName));
        }

        using var db = _factory.CreateDbContext();
        var item = db.CatalogItems.FirstOrDefault(item => item.NormalizedKey == key);
        if (item is null)
        {
            item = new CatalogItem
            {
                Id = Guid.NewGuid(),
                NormalizedKey = key,
                DisplayName = display,
                Source = SourceName(source),
                UsageCount = usageCount ?? 1,
                LastUsedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                DossierTypeHint = TrimHint(dossierTypeHint)
            };
            db.CatalogItems.Add(item);
        }
        else
        {
            // Mục có sẵn: giữ cách viết canonical cũ, chỉ tăng bộ đếm "hay gặp".
            item.UsageCount = usageCount.HasValue
                ? Math.Max(item.UsageCount, usageCount.Value)
                : item.UsageCount + 1;
            item.LastUsedAt = DateTime.UtcNow;
            item.DossierTypeHint ??= TrimHint(dossierTypeHint);
        }

        db.SaveChanges();
        return ToEntry(item);
    }

    public void Rename(Guid id, string displayName)
    {
        var display = CatalogKey.NormalizeDisplay(displayName);
        var key = CatalogKey.Normalize(display);
        if (key.Length == 0)
        {
            throw new ArgumentException("Tên loại giấy tờ không được rỗng.", nameof(displayName));
        }

        using var db = _factory.CreateDbContext();
        var item = db.CatalogItems.Find(id)
            ?? throw new InvalidOperationException("Không tìm thấy mục danh mục cần đổi tên.");

        var duplicated = db.CatalogItems
            .FirstOrDefault(other => other.NormalizedKey == key && other.Id != id);
        if (duplicated is not null && !duplicated.IsDisabled)
        {
            throw new InvalidOperationException(
                $"Loại giấy tờ \"{duplicated.DisplayName}\" đã tồn tại; gộp lại thay vì tạo mới.");
        }

        item.DisplayName = display;
        item.NormalizedKey = key;
        db.SaveChanges();
    }

    public void SetDisabled(Guid id, bool disabled)
    {
        using var db = _factory.CreateDbContext();
        var item = db.CatalogItems.Find(id)
            ?? throw new InvalidOperationException("Không tìm thấy mục danh mục.");
        item.IsDisabled = disabled;
        db.SaveChanges();
    }

    private static CatalogEntry ToEntry(CatalogItem item) =>
        new(
            item.Id,
            item.DisplayName,
            item.NormalizedKey,
            item.Source == "User" ? CatalogEntrySource.User : CatalogEntrySource.Learned,
            item.UsageCount,
            item.LastUsedAt,
            item.DossierTypeHint);

    private static string SourceName(CatalogEntrySource source) =>
        source == CatalogEntrySource.User ? "User" : "Learned";

    private static string? TrimHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        return Regex.Replace(hint.Trim(), @"\s+", " ");
    }
}
