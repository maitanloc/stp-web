namespace Stp.Core.Domain;

/// <summary>
/// Một loại giấy tờ trong "danh mục học được" — lưu bền trên database để người
/// dùng thêm lần đầu, các lần sau khỏi gõ lại. <see cref="UsageCount"/> tăng mỗi
/// khi mục được dùng, giúp loại hay gặp nhất luôn trồi lên đầu khi gợi ý.
/// Mục bịdisable chỉ ẩn, không xoá để giữ lịch sử.
/// </summary>
public sealed class CatalogItem
{
    public Guid Id { get; set; }

    /// <summary>Khoá chuẩn hoá (bỏ dấu, chữ thường, gộp khoảng trắng) — unique.</summary>
    public string NormalizedKey { get; set; } = string.Empty;

    /// <summary>Cách viết canonical hiển thị cho người dùng.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>User | Learned — nguồn gốc của mục.</summary>
    public string Source { get; set; } = "User";

    /// <summary>Đếm tần suất sưu tầm/nhập liệu; dùng để sắp "hay gặp nhất".</summary>
    public int UsageCount { get; set; }

    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gợi ý ngữ cảnh: ưu tiên cho loại hồ sơ nào (null = mọi loại).</summary>
    public string? DossierTypeHint { get; set; }

    public bool IsDisabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
