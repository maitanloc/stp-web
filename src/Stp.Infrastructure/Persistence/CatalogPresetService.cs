using System.Text.Json;
using Microsoft.Extensions.Logging;
using Stp.Core.Storage;
using Stp.Core.Summary;

namespace Stp.Infrastructure.Persistence;

public sealed class CatalogPresetService : ICatalogPresetService
{
    private const string PresetsFileName = "catalog-presets.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IFileStorage _storage;
    private readonly ILogger<CatalogPresetService> _logger;
    private readonly object _lock = new();

    public CatalogPresetService(IFileStorage storage, ILogger<CatalogPresetService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    private string PresetsPath => Path.Combine(_storage.RootPath, PresetsFileName);

    public IReadOnlyList<CatalogPresetModel> GetPresets()
    {
        lock (_lock)
        {
            var presets = LoadPresetsInternal();
            if (presets.Count == 0)
            {
                presets = CreateDefaultPresets();
                SavePresetsInternal(presets);
            }

            return presets
                .OrderBy(p => p.Order)
                .ThenBy(p => p.Name)
                .ToList();
        }
    }

    public CatalogPresetModel UpsertPreset(
        Guid? id,
        string name,
        string? dossierType,
        IReadOnlyList<string> items,
        bool isDefault,
        int? order)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "Bộ mẫu mới" : name.Trim();
        var cleanItems = (items ?? Array.Empty<string>())
            .Select(i => i.Trim().ToLowerInvariant())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_lock)
        {
            var presets = LoadPresetsInternal();
            if (presets.Count == 0)
            {
                presets = CreateDefaultPresets();
            }

            var now = DateTimeOffset.UtcNow;
            CatalogPresetModel result;

            var existingIndex = id.HasValue ? presets.FindIndex(p => p.Id == id.Value) : -1;
            if (existingIndex >= 0)
            {
                var existing = presets[existingIndex];
                result = new CatalogPresetModel(
                    existing.Id,
                    cleanName,
                    string.IsNullOrWhiteSpace(dossierType) ? null : dossierType.Trim(),
                    cleanItems,
                    isDefault,
                    order ?? existing.Order,
                    existing.CreatedAt,
                    now);
                presets[existingIndex] = result;
            }
            else
            {
                var nextOrder = order ?? (presets.Count > 0 ? presets.Max(p => p.Order) + 1 : 1);
                result = new CatalogPresetModel(
                    id ?? Guid.NewGuid(),
                    cleanName,
                    string.IsNullOrWhiteSpace(dossierType) ? null : dossierType.Trim(),
                    cleanItems,
                    isDefault,
                    nextOrder,
                    now,
                    now);
                presets.Add(result);
            }

            SavePresetsInternal(presets);
            return result;
        }
    }

    public bool DeletePreset(Guid id)
    {
        lock (_lock)
        {
            var presets = LoadPresetsInternal();
            var count = presets.RemoveAll(p => p.Id == id);
            if (count > 0)
            {
                SavePresetsInternal(presets);
                return true;
            }

            return false;
        }
    }

    private List<CatalogPresetModel> LoadPresetsInternal()
    {
        try
        {
            var path = PresetsPath;
            if (!File.Exists(path))
            {
                return new List<CatalogPresetModel>();
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<CatalogPresetModel>>(json, JsonOptions);
            return loaded ?? new List<CatalogPresetModel>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi đọc {Path} — khởi tạo lại danh sách rỗng", PresetsPath);
            return new List<CatalogPresetModel>();
        }
    }

    private void SavePresetsInternal(List<CatalogPresetModel> presets)
    {
        try
        {
            Directory.CreateDirectory(_storage.RootPath);
            var json = JsonSerializer.Serialize(presets, JsonOptions);
            File.WriteAllText(PresetsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi ghi {Path}", PresetsPath);
        }
    }

    private static List<CatalogPresetModel> CreateDefaultPresets()
    {
        var now = DateTimeOffset.UtcNow;
        return new List<CatalogPresetModel>
        {
            new(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Bộ 7 chuẩn (Đăng ký kết hôn)",
                "Đăng ký kết hôn",
                new[]
                {
                    "giấy khai sinh",
                    "sơ yếu lý lịch",
                    "bản công hàm",
                    "phiếu đề xuất",
                    "hộ khẩu",
                    "chứng minh nhân dân",
                    "giấy xác nhận tình trạng hôn nhân"
                },
                IsDefault: true,
                Order: 1,
                CreatedAt: now,
                UpdatedAt: now),
            new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "Bộ hồ sơ Khai sinh",
                "Đăng ký khai sinh",
                new[]
                {
                    "giấy chứng sinh",
                    "giấy chứng nhận kết hôn của cha mẹ",
                    "căn cước công dân của cha mẹ",
                    "sổ hộ khẩu"
                },
                IsDefault: false,
                Order: 2,
                CreatedAt: now,
                UpdatedAt: now),
            new(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Bộ hồ sơ Khai tử",
                "Đăng ký khai tử",
                new[]
                {
                    "giấy báo tử",
                    "căn cước công dân của người chết",
                    "sổ hộ khẩu"
                },
                IsDefault: false,
                Order: 3,
                CreatedAt: now,
                UpdatedAt: now),
            new(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "Bộ hồ sơ Nhận cha mẹ con",
                "Nhận cha mẹ con",
                new[]
                {
                    "văn bản nhận cha mẹ con",
                    "giấy xét nghiệm adn",
                    "giấy khai sinh của con",
                    "căn cước công dân của cha mẹ"
                },
                IsDefault: false,
                Order: 4,
                CreatedAt: now,
                UpdatedAt: now)
        };
    }
}

