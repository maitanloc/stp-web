namespace Stp.Core.Summary;

public sealed record CatalogPresetModel(
    Guid Id,
    string Name,
    string? DossierType,
    IReadOnlyList<string> Items,
    bool IsDefault,
    int Order,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Quản lý các bộ mẫu giấy tờ do Admin cấu hình (presets) cho các loại hồ sơ khác nhau.
/// Lưu trữ bền vững để cả phòng dùng chung và áp dụng nhanh 1-chạm.
/// </summary>
public interface ICatalogPresetService
{
    IReadOnlyList<CatalogPresetModel> GetPresets();
    CatalogPresetModel UpsertPreset(Guid? id, string name, string? dossierType, IReadOnlyList<string> items, bool isDefault, int? order);
    bool DeletePreset(Guid id);
}

