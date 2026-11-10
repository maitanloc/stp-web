namespace Stp.Core.Summary;

/// <summary>Nguồn gốc của một mục trong danh mục học được.</summary>
public enum CatalogEntrySource
{
    /// <summary>Người dùng chủ động thêm — không bao giờ bị loại khỏi danh mục.</summary>
    User,

    /// <summary>Tự động harvest từ kho hồ sơ (UsageCount đạt ngưỡng tin cậy).</summary>
    Learned
}

/// <summary>Một mục trong danh mục giấy tờ lưu bền (database).</summary>
public sealed record CatalogEntry(
    Guid Id,
    string DisplayName,
    string NormalizedKey,
    CatalogEntrySource Source,
    int UsageCount,
    DateTime LastUsedAt,
    string? DossierTypeHint);

/// <summary>
/// Kho lưu trữ bền cho danh mục giấy tờ. Được hệ thống "học" theo UsageCount:
/// loại giấy tờ người dùng gặp nhiều tự trồi lên đầu khi gợi ý.
/// </summary>
public interface ICatalogDictionary
{
    /// <summary>Toàn bộ mục đang hoạt động sắp theo UsageCount giảm rồi LastUsedAt giảm.</summary>
    IReadOnlyList<CatalogEntry> GetActive();

    /// <summary>Tìm theo khoá chuẩn hoá — trả về cách viết canonical.</summary>
    CatalogEntry? Find(string normalizedKey);

    /// <summary>Lấy một mục theo id, kể cả khi đã bị ẩn (disable) — phục vụ PATCH response.</summary>
    CatalogEntry? GetById(Guid id);

    /// <summary>
    /// Thêm chưa có (theo khoá chuẩn hoá) hay tăng bộ đếm nếu đã có.
    /// Trả về entry canonical sau cập nhật (có thể là cách viết cũ).
    /// </summary>
    CatalogEntry Upsert(
        string displayName,
        string? dossierTypeHint,
        CatalogEntrySource source,
        int? usageCount = null);

    void Rename(Guid id, string displayName);

    void SetDisabled(Guid id, bool disabled);
}

/// <summary>
/// Meta tần suất + nguồn cho từng mục trong danh mục một phiên, phục vụ UI
/// gợi ý "hay gặp nhất" (badge ×N) và nhóm "Đã lưu của người dùng".
/// Source: "user" | "learned" | "file" (nằm trong file) | "default" (danh mục cố định).
/// </summary>
public sealed record SummaryCatalogMeta(string Name, int UsageCount, string Source);
