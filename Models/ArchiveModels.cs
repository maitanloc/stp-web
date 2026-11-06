using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StpWeb.Models;

/// <summary>
/// Toàn bộ dữ liệu đã import từ một workbook Excel.
/// </summary>
public sealed class ArchiveData
{
    public string? SourceFileName { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
    public List<ArchiveBook> Books { get; set; } = new();

    [JsonIgnore]
    public int TotalDossiers => Books.Sum(b => b.Dossiers.Count);

    [JsonIgnore]
    public int TotalTypes => Books
        .SelectMany(b => b.Dossiers)
        .Select(d => d.Type)
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    [JsonIgnore]
    public int TotalGroups => Books
        .SelectMany(b => b.Dossiers)
        .Select(d => d.Group)
        .Where(g => !string.IsNullOrWhiteSpace(g))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    [JsonIgnore]
    public int TotalPages => Books.SelectMany(b => b.Dossiers).Sum(d => d.TotalPages);

    [JsonIgnore]
    public int TotalPrintReady => Books.SelectMany(b => b.Dossiers).Count(d => d.HasPrintLayout);
}

/// <summary>
/// Một năm lưu trữ. Các sheet in bị chia nhỏ (ví dụ 1999 (1), 1999 (2))
/// được gom chung vào một năm, nhưng vẫn giữ tên các sổ in gốc trong SourceVolumes.
/// </summary>
public sealed class ArchiveBook
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public List<string> SourceVolumes { get; set; } = new();
    public List<Dossier> Dossiers { get; set; } = new();

    [JsonIgnore]
    public int Count => Dossiers.Count;

    [JsonIgnore]
    public int MinNumber => Dossiers.Count == 0 ? 0 : Dossiers.Min(d => d.Number);

    [JsonIgnore]
    public int MaxNumber => Dossiers.Count == 0 ? 0 : Dossiers.Max(d => d.Number);

    [JsonIgnore]
    public int TypeCount => TypeStats.Count;

    [JsonIgnore]
    public bool HasPrintSheets => SourceVolumes.Count > 0;

    [JsonIgnore]
    public IReadOnlyList<DossierTypeStat> TypeStats => Dossiers
        .GroupBy(d => string.IsNullOrWhiteSpace(d.Type) ? "Chưa phân loại" : d.Type.Trim())
        .Select(g => new DossierTypeStat
        {
            Type = g.Key,
            Count = g.Count(),
            MinNumber = g.Count() == 0 ? 0 : g.Min(d => d.Number),
            MaxNumber = g.Count() == 0 ? 0 : g.Max(d => d.Number)
        })
        .OrderBy(s => DossierTypes.Order(s.Type))
        .ThenBy(s => s.Type, StringComparer.OrdinalIgnoreCase)
        .ToList();

    [JsonIgnore]
    public string NumberRange => Count == 0
        ? "không có hồ sơ"
        : $"hồ sơ số {MinNumber} đến {MaxNumber}";

    [JsonIgnore]
    public string SourceVolumesText => SourceVolumes.Count == 0
        ? "Chưa có sheet in gốc"
        : string.Join(", ", SourceVolumes);
}

/// <summary>
/// Một hồ sơ = một trang in "MỤC LỤC VĂN BẢN".
/// </summary>
public sealed class Dossier
{
    /// <summary>Số hồ sơ dạng số, dùng để lọc giống macro (Val của "346/1" = 346).</summary>
    public int Number { get; set; }

    /// <summary>Nhãn số hồ sơ đúng như file Excel, ví dụ "346/1", "350/1", "/A".</summary>
    public string NumberLabel { get; set; } = string.Empty;

    public string Title { get; set; } = "MỤC LỤC VĂN BẢN";

    /// <summary>Nhóm lưu trữ, lấy từ dòng "I. Văn phòng" trong sheet ML.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Loại hồ sơ, lấy từ dòng "* Đăng ký kết hôn" trong sheet ML.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Hộp số (cột A của ML), có thể trống.</summary>
    public string BoxNumber { get; set; } = string.Empty;

    /// <summary>STT/hồ sơ phụ (cột C của ML), có thể là "01", "HS 36"...</summary>
    public string SubNumber { get; set; } = string.Empty;

    /// <summary>Trích yếu gốc trước khi chuyển thành văn bản đầy đủ (cột I của ML).</summary>
    public string OriginalSummary { get; set; } = string.Empty;

    /// <summary>Ghi chú khi số hồ sơ gốc bị trống hoặc không có phần số.</summary>
    public string NumberNote { get; set; } = string.Empty;

    /// <summary>Tên sheet in gốc đã cung cấp dữ liệu cho hồ sơ này (nếu có).</summary>
    public string SourceVolume { get; set; } = string.Empty;

    /// <summary>Hồ sơ có block in tương ứng trong các sheet năm hay không.</summary>
    public bool HasPrintLayout { get; set; }

    /// <summary>Dòng ML là dòng tổng hợp nhiều hồ sơ nhỏ (ví dụ "Từ số 01 - 20").</summary>
    public bool IsAggregate { get; set; }

    public List<DocumentLine> Documents { get; set; } = new();
    public int TotalPages { get; set; }
    public int MlRow { get; set; }

    [JsonIgnore]
    public string NumberDisplay => string.IsNullOrWhiteSpace(NumberLabel)
        ? (Number > 0 ? Number.ToString(CultureInfo.InvariantCulture) : "(không số)")
        : NumberLabel;

    [JsonIgnore]
    public string TypeDisplay => string.IsNullOrWhiteSpace(Type) ? "Chưa phân loại" : Type;

    [JsonIgnore]
    public string TotalPagesText => TotalPages > 0
        ? $"Tổng số tờ: {TotalPages} tờ"
        : "Tổng số tờ: … tờ";

    [JsonIgnore]
    public int PrintTextLength => Documents.Sum(d =>
        (d.TrichYeu?.Length ?? 0)
        + (d.SoKyHieu?.Length ?? 0)
        + (d.Ngay?.Length ?? 0)
        + (d.TacGia?.Length ?? 0)
        + (d.ToSo?.Length ?? 0)
        + (d.GhiChu?.Length ?? 0));

    [JsonIgnore]
    public string PrintFitClass
    {
        get
        {
            if (Documents.Count >= 12 || PrintTextLength >= 1200)
            {
                return "fit-tiny";
            }

            if (Documents.Count >= 8 || PrintTextLength >= 850)
            {
                return "fit-tight";
            }

            if (Documents.Count >= 4 || PrintTextLength >= 550)
            {
                return "fit-compact";
            }

            return string.Empty;
        }
    }
}

/// <summary>
/// Một dòng văn bản trong bảng mục lục, đúng theo các cột của sheet in Excel.
/// </summary>
public sealed class DocumentLine
{
    public int Stt { get; set; } = 1;
    public string SoKyHieu { get; set; } = string.Empty;
    public string Ngay { get; set; } = string.Empty;
    public string TrichYeu { get; set; } = string.Empty;
    public string TacGia { get; set; } = string.Empty;
    public string ToSo { get; set; } = string.Empty;
    public string GhiChu { get; set; } = string.Empty;
}

/// <summary>Thống kê nhanh một loại hồ sơ trong một năm.</summary>
public sealed class DossierTypeStat
{
    public string Type { get; init; } = string.Empty;
    public int Count { get; init; }
    public int MinNumber { get; init; }
    public int MaxNumber { get; init; }

    [JsonIgnore]
    public string Range => Count == 0 ? "—" : $"{MinNumber}–{MaxNumber}";
}

/// <summary>Thứ tự hiển thị thống nhất của 8 loại hồ sơ trong workbook.</summary>
public static class DossierTypes
{
    public static readonly string[] Preferred =
    {
        "Đăng ký kết hôn",
        "Thay đổi tên & khai sinh",
        "Đăng ký nuôi con nuôi",
        "Con ngoài giá thú",
        "Đăng ký khai sinh",
        "Đăng ký khai tử",
        "Xác nhận Quốc tịch",
        "Ghi chú Kết hôn"
    };

    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return string.Empty;
        }

        return Regex.Replace(type.Trim(), @"\s+", " ");
    }

    public static int Order(string? type)
    {
        var normalized = Normalize(type);
        if (normalized.Length == 0)
        {
            return int.MaxValue;
        }

        for (var i = 0; i < Preferred.Length; i++)
        {
            if (string.Equals(Preferred[i], normalized, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return Preferred.Length + 1;
    }
}

public sealed class FilterResult
{
    public required IReadOnlyList<Dossier> Dossiers { get; init; }
    public required int Total { get; init; }
    public required string StatusMessage { get; init; }
    public string? Warning { get; init; }
    public IReadOnlyList<int> NotFound { get; init; } = Array.Empty<int>();
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);
}

public sealed class ImportResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public ArchiveData? Data { get; init; }
    public int BookCount => Data?.Books.Count ?? 0;
    public int YearCount => Data?.Books.Count ?? 0;
    public int TypeCount => Data?.TotalTypes ?? 0;
    public int DossierCount => Data?.TotalDossiers ?? 0;
}
