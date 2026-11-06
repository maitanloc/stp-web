using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StpWeb.Models;
using StpWeb.Services;

namespace StpWeb.Pages;

/// <summary>
/// Trang xem chi tiết một hồ sơ: đầy đủ thông tin ML, block in, dòng văn bản,
/// checklist kiểm tra nhanh và điều hướng sang hồ sơ trước/sau.
/// </summary>
public sealed class DossierModel : PageModel
{
    private readonly ArchiveStore _store;

    public DossierModel(ArchiveStore store)
    {
        _store = store;
    }

    [BindProperty(SupportsGet = true)]
    public string? BookId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? MlRow { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Number { get; set; }

    public ArchiveBook? Book { get; private set; }
    public Dossier? SelectedDossier { get; private set; }
    public DocumentLine? MainDocument => SelectedDossier?.Documents.FirstOrDefault();

    public Dossier? Previous { get; private set; }
    public Dossier? Next { get; private set; }
    public Dossier? PreviousSameType { get; private set; }
    public Dossier? NextSameType { get; private set; }

    public int Position { get; private set; }
    public int BookCount { get; private set; }
    public int TypePosition { get; private set; }
    public int TypeCount { get; private set; }
    public int BookTypeCount { get; private set; }
    public int WarningCount => Checks.Count(c => string.Equals(c.Status, "warn", StringComparison.OrdinalIgnoreCase));

    public List<CheckItem> Checks { get; private set; } = new();
    public bool NoData { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var data = await _store.GetAsync(cancellationToken);
        if (data.Books.Count == 0)
        {
            NoData = true;
            return;
        }

        ArchiveBook? book = null;
        Dossier? dossier = null;

        if (MlRow is > 0)
        {
            book = data.Books.FirstOrDefault(b => b.Dossiers.Any(d => d.MlRow == MlRow.Value));
            dossier = book?.Dossiers.FirstOrDefault(d => d.MlRow == MlRow.Value);
        }
        else if (Number is > 0)
        {
            if (!string.IsNullOrWhiteSpace(BookId))
            {
                book = data.Books.FirstOrDefault(b =>
                    string.Equals(b.Id, BookId, StringComparison.OrdinalIgnoreCase));
                dossier = book?.Dossiers.FirstOrDefault(d => d.Number == Number.Value);
            }

            if (dossier is null)
            {
                book = data.Books.FirstOrDefault(b => b.Dossiers.Any(d => d.Number == Number.Value));
                dossier = book?.Dossiers.FirstOrDefault(d => d.Number == Number.Value);
            }
        }
        else
        {
            book = data.Books.FirstOrDefault(b =>
                       string.Equals(b.Id, BookId, StringComparison.OrdinalIgnoreCase))
                   ?? data.Books.First();
            dossier = book.Dossiers.FirstOrDefault();
        }

        if (book is null || dossier is null)
        {
            Book = book;
            BookId = book?.Id;
            Error = MlRow is > 0
                ? $"Không tìm thấy hồ sơ ứng với dòng ML {MlRow}."
                : Number is > 0
                    ? $"Không tìm thấy hồ sơ số {Number}."
                    : "Không tìm thấy hồ sơ cần xem.";
            return;
        }

        Book = book;
        BookId = book.Id;
        SelectedDossier = dossier;
        BookTypeCount = book.TypeCount;

        var ordered = book.Dossiers
            .OrderBy(d => d.MlRow)
            .ThenBy(d => d.Number)
            .ToList();

        BookCount = ordered.Count;
        var index = ordered.FindIndex(d => d.MlRow == dossier.MlRow);
        Position = index >= 0 ? index + 1 : 0;
        Previous = index > 0 ? ordered[index - 1] : null;
        Next = index >= 0 && index < ordered.Count - 1 ? ordered[index + 1] : null;

        var sameType = ordered
            .Where(d => string.Equals(d.Type, dossier.Type, StringComparison.OrdinalIgnoreCase))
            .ToList();
        TypeCount = sameType.Count;
        var typeIndex = sameType.FindIndex(d => d.MlRow == dossier.MlRow);
        TypePosition = typeIndex >= 0 ? typeIndex + 1 : 0;
        PreviousSameType = typeIndex > 0 ? sameType[typeIndex - 1] : null;
        NextSameType = typeIndex >= 0 && typeIndex < sameType.Count - 1 ? sameType[typeIndex + 1] : null;

        BuildChecks(dossier);
    }

    private void BuildChecks(Dossier dossier)
    {
        var document = MainDocument;
        var missingDate = document is null || string.IsNullOrWhiteSpace(document.Ngay);
        var missingDocNo = document is null || string.IsNullOrWhiteSpace(document.SoKyHieu);
        var missingPages = document is null || string.IsNullOrWhiteSpace(document.ToSo);
        var hasNumberNote = !string.IsNullOrWhiteSpace(dossier.NumberNote);

        Checks = new List<CheckItem>
        {
            new(
                "Dòng ML gốc",
                dossier.MlRow > 0 ? $"Dòng {dossier.MlRow}" : "Không xác định",
                dossier.MlRow > 0 ? "ok" : "warn",
                "Dùng để in riêng đúng hồ sơ này, kể cả các số đặc biệt."),
            new(
                "Số hồ sơ để lọc",
                dossier.Number > 0 ? dossier.Number.ToString() : "Chưa đọc được",
                dossier.Number > 0 ? "ok" : "warn",
                "Số dạng số dùng cho bộ lọc khoảng và danh sách số, giống hàm Val trong macro."),
            new(
                "Nhãn số hồ sơ in",
                string.IsNullOrWhiteSpace(dossier.NumberLabel) ? "(trống)" : dossier.NumberLabel,
                string.IsNullOrWhiteSpace(dossier.NumberLabel) ? "warn" : "ok",
                "Giữ nguyên đúng như cột B của sheet ML, ví dụ 346/1, 350/1, /A."),
            new(
                "Loại hồ sơ",
                dossier.TypeDisplay,
                string.IsNullOrWhiteSpace(dossier.Type) ? "warn" : "ok",
                "Lấy từ dòng tiêu đề * … trong sheet ML."),
            new(
                "Nhóm lưu trữ",
                string.IsNullOrWhiteSpace(dossier.Group) ? "(trống)" : dossier.Group,
                string.IsNullOrWhiteSpace(dossier.Group) ? "warn" : "ok",
                "Lấy từ dòng I. Văn phòng trong sheet ML."),
            new(
                "Block in Excel",
                dossier.HasPrintLayout
                    ? $"Có · {dossier.SourceVolume}"
                    : "Không có – chỉ dữ liệu ML",
                dossier.HasPrintLayout ? "ok" : "info",
                "Hồ sơ chỉ có trong ML vẫn in được; tác giả mặc định là Ủy ban nhân dân tỉnh Đồng Nai."),
            new(
                "Ngày văn bản",
                missingDate ? "Thiếu" : document!.Ngay,
                missingDate ? "warn" : "ok",
                "Cột E của sheet ML / cột ngày của block in."),
            new(
                "Số ký hiệu",
                missingDocNo ? "Thiếu" : document!.SoKyHieu,
                missingDocNo ? "warn" : "ok",
                "Cột F của sheet ML / cột số ký hiệu của block in."),
            new(
                "Tờ số",
                missingPages ? "Thiếu" : document!.ToSo,
                missingPages ? "warn" : "ok",
                "Cột G của sheet ML. Bản in sẽ hiện … tờ nếu không xác định được."),
            new(
                "Tổng số tờ",
                dossier.TotalPages > 0 ? $"{dossier.TotalPages} tờ" : "Không xác định",
                dossier.TotalPages > 0 ? "ok" : "warn",
                "Lấy số cuối của khoảng tờ số, ví dụ 01-35 → 35 tờ."),
            new(
                "Dòng tổng hợp",
                dossier.IsAggregate ? "Có – cần kiểm tra" : "Không",
                dossier.IsAggregate ? "warn" : "ok",
                "Dòng ML gom nhiều hồ sơ nhỏ, không phải một hồ sơ đơn lẻ."),
            new(
                "Ghi chú số hồ sơ",
                hasNumberNote ? dossier.NumberNote : "Không có",
                hasNumberNote ? "warn" : "ok",
                "Số gốc trống hoặc không có phần số; hệ thống đã suy luận số để lọc/sắp xếp, nhãn in vẫn giữ nguyên.")
        };
    }
}

public sealed record CheckItem(string Label, string Value, string Status, string Hint);
