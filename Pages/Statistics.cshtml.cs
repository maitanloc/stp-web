using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StpWeb.Models;
using StpWeb.Services;

namespace StpWeb.Pages;

/// <summary>
/// Thống kê toàn bộ dữ liệu theo năm và loại hồ sơ, đồng thời cung cấp
/// công cụ kiểm tra dữ liệu và xuất CSV để đối chiếu với Excel.
/// </summary>
public sealed class StatisticsModel : PageModel
{
    private readonly ArchiveStore _store;

    public StatisticsModel(ArchiveStore store)
    {
        _store = store;
    }

    [BindProperty(SupportsGet = true)]
    public string? BookId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Issue { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNo { get; set; } = 1;

    public ArchiveData Data { get; private set; } = new();
    public ArchiveBook? SelectedBook { get; private set; }
    public List<ArchiveBook> Books => Data.Books;
    public List<string> AllTypes { get; private set; } = new();
    public List<TypeSummary> TypeSummaries { get; private set; } = new();
    public List<TypeMatrixRow> MatrixRows { get; private set; } = new();
    public List<InspectionItem> Rows { get; private set; } = new();

    public Dictionary<string, int> IssueCounts { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SelectedIssue { get; private set; } = "all";
    public int ScopeCount { get; private set; }
    public int MatchingCount { get; private set; }
    public int PageNumber { get; private set; } = 1;
    public int PageCount { get; private set; } = 1;
    public int PageSize => 100;

    public int YearsWithPrintSheets => Data.Books.Count(b => b.HasPrintSheets);
    public int MlOnlyCount => Data.TotalDossiers - Data.TotalPrintReady;
    public int TotalPages => Data.TotalPages;
    public int TotalPrintReady => Data.TotalPrintReady;

    public string SelectedIssueLabel => IssueDefinitions
        .FirstOrDefault(i => string.Equals(i.Key, SelectedIssue, StringComparison.OrdinalIgnoreCase))
        ?.Label ?? "Tất cả hồ sơ";

    public string ScopeLabel
    {
        get
        {
            var parts = new List<string>();
            if (SelectedBook is not null)
            {
                parts.Add($"năm {SelectedBook.Name}");
            }
            else
            {
                parts.Add("toàn bộ dữ liệu");
            }

            if (!string.IsNullOrWhiteSpace(Type))
            {
                parts.Add($"loại “{Type}”");
            }

            return string.Join(" · ", parts);
        }
    }

    public static IReadOnlyList<IssueDefinition> IssueDefinitions { get; } = new List<IssueDefinition>
    {
        new("all", "Tất cả hồ sơ", "Không lọc theo tình trạng dữ liệu."),
        new("missing-date", "Thiếu ngày văn bản", "Cột E (thời gian tài liệu) của dòng ML để trống."),
        new("missing-docno", "Thiếu số ký hiệu", "Cột F (số ký hiệu văn bản) của dòng ML để trống."),
        new("zero-total", "Không xác định tổng tờ", "Cột G trống hoặc không đọc được số tờ cuối, bản in sẽ hiện “… tờ”."),
        new("aggregate", "Dòng tổng hợp", "Dòng ML gom nhiều hồ sơ nhỏ, không phải một hồ sơ đơn lẻ."),
        new("number-note", "Số hồ sơ cần kiểm tra", "Số gốc trống hoặc không có phần số; hệ thống đã suy luận số để lọc/sắp xếp."),
        new("no-print", "Chưa có bản in Excel", "Hồ sơ chỉ có trong sheet ML, chưa có block in ở các sheet năm.")
    };

    public IReadOnlyList<IssueDefinition> Issues => IssueDefinitions;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await _store.GetAsync(cancellationToken);
        if (Data.Books.Count == 0)
        {
            return;
        }

        SelectedBook = Data.Books.FirstOrDefault(b =>
            string.Equals(b.Id, BookId, StringComparison.OrdinalIgnoreCase));
        BookId = SelectedBook?.Id;

        AllTypes = Data.Books
            .SelectMany(b => b.Dossiers)
            .Select(TypeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(DossierTypes.Order)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Type = NormalizeType(Data, Type);
        SelectedIssue = NormalizeIssue(Issue);
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

        BuildOverview();
        BuildInspection();
    }

    /// <summary>
    /// Xuất toàn bộ kết quả kiểm tra đang lọc ra CSV (không giới hạn theo trang).
    /// </summary>
    public async Task<IActionResult> OnGetCsvAsync(CancellationToken cancellationToken)
    {
        var data = await _store.GetAsync(cancellationToken);
        if (data.Books.Count == 0)
        {
            return NotFound();
        }

        var book = data.Books.FirstOrDefault(b =>
            string.Equals(b.Id, BookId, StringComparison.OrdinalIgnoreCase));
        var type = NormalizeType(data, Type);
        var issue = NormalizeIssue(Issue);
        var search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

        var items = BuildBaseItems(data, book, type)
            .Where(i => MatchesIssue(i.Dossier, issue));

        if (search is not null)
        {
            items = items.Where(i => MatchesSearch(i, search));
        }

        var ordered = items
            .OrderBy(i => i.Book.Year)
            .ThenBy(i => DossierTypes.Order(TypeKey(i.Dossier)))
            .ThenBy(i => i.Dossier.Number)
            .ThenBy(i => i.Dossier.MlRow)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Năm,Nhóm,Loại,Hộp số,Hồ sơ số,Số dùng lọc,STT/hồ sơ phụ,Số ký hiệu,Ngày,Trích yếu,Tác giả,Tờ số,Tổng số tờ,Dòng ML,Có bản in,Dòng tổng hợp,Ghi chú số");

        foreach (var item in ordered)
        {
            var dossier = item.Dossier;
            var document = FirstDoc(dossier);
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(item.Book.Year),
                Csv(dossier.Group),
                Csv(TypeKey(dossier)),
                Csv(dossier.BoxNumber),
                Csv(dossier.NumberDisplay),
                Csv(dossier.Number),
                Csv(dossier.SubNumber),
                Csv(document?.SoKyHieu),
                Csv(document?.Ngay),
                Csv(document?.TrichYeu),
                Csv(document?.TacGia),
                Csv(document?.ToSo),
                Csv(dossier.TotalPages),
                Csv(dossier.MlRow),
                Csv(dossier.HasPrintLayout ? "Có" : "Không"),
                Csv(dossier.IsAggregate ? "Có" : "Không"),
                Csv(dossier.NumberNote)
            }));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(builder.ToString()))
            .ToArray();
        var suffix = book is null ? "tat-ca" : book.Year.ToString(CultureInfo.InvariantCulture);
        var fileName = $"thong-ke-ho-so-{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private void BuildOverview()
    {
        AllTypes = Data.Books
            .SelectMany(b => b.Dossiers)
            .Select(TypeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(DossierTypes.Order)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allItems = Data.Books
            .SelectMany(b => b.Dossiers.Select(d => new InspectionItem(b, d)))
            .ToList();

        TypeSummaries = allItems
            .GroupBy(i => TypeKey(i.Dossier), StringComparer.OrdinalIgnoreCase)
            .Select(g => new TypeSummary
            {
                Type = g.Key,
                Count = g.Count(),
                Years = g.Select(i => i.Book.Year).Distinct().Count(),
                Min = g.Min(i => i.Dossier.Number),
                Max = g.Max(i => i.Dossier.Number)
            })
            .OrderBy(s => DossierTypes.Order(s.Type))
            .ThenBy(s => s.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var countsByBook = Data.Books.ToDictionary(
            b => b.Id,
            b => b.Dossiers
                .GroupBy(d => TypeKey(d), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        MatrixRows = AllTypes
            .Select(type =>
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var total = 0;
                foreach (var book in Data.Books)
                {
                    var count = countsByBook[book.Id].GetValueOrDefault(type);
                    counts[book.Id] = count;
                    total += count;
                }

                return new TypeMatrixRow
                {
                    Type = type,
                    Counts = counts,
                    Total = total
                };
            })
            .ToList();
    }

    private void BuildInspection()
    {
        var baseItems = BuildBaseItems(Data, SelectedBook, Type);
        ScopeCount = baseItems.Count;
        IssueCounts = BuildIssueCounts(baseItems);

        var matching = baseItems
            .Where(i => MatchesIssue(i.Dossier, SelectedIssue));

        if (Search is not null)
        {
            matching = matching.Where(i => MatchesSearch(i, Search));
        }

        var ordered = matching
            .OrderBy(i => i.Book.Year)
            .ThenBy(i => DossierTypes.Order(TypeKey(i.Dossier)))
            .ThenBy(i => i.Dossier.Number)
            .ThenBy(i => i.Dossier.MlRow)
            .ToList();

        MatchingCount = ordered.Count;
        PageCount = Math.Max(1, (int)Math.Ceiling(MatchingCount / (double)PageSize));
        PageNumber = Math.Clamp(PageNo, 1, PageCount);

        Rows = ordered
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }

    private static List<InspectionItem> BuildBaseItems(ArchiveData data, ArchiveBook? book, string? type)
    {
        IEnumerable<InspectionItem> items = data.Books
            .SelectMany(b => b.Dossiers.Select(d => new InspectionItem(b, d)));

        if (book is not null)
        {
            items = items.Where(i => string.Equals(i.Book.Id, book.Id, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            items = items.Where(i => string.Equals(TypeKey(i.Dossier), type, StringComparison.OrdinalIgnoreCase));
        }

        return items.ToList();
    }

    private static Dictionary<string, int> BuildIssueCounts(IReadOnlyList<InspectionItem> items)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in IssueDefinitions)
        {
            counts[issue.Key] = string.Equals(issue.Key, "all", StringComparison.OrdinalIgnoreCase)
                ? items.Count
                : items.Count(i => MatchesIssue(i.Dossier, issue.Key));
        }

        return counts;
    }

    private static bool MatchesIssue(Dossier dossier, string issue)
    {
        var document = FirstDoc(dossier);
        return issue switch
        {
            "missing-date" => string.IsNullOrWhiteSpace(document?.Ngay),
            "missing-docno" => string.IsNullOrWhiteSpace(document?.SoKyHieu),
            "zero-total" => dossier.TotalPages <= 0,
            "aggregate" => dossier.IsAggregate,
            "number-note" => !string.IsNullOrWhiteSpace(dossier.NumberNote),
            "no-print" => !dossier.HasPrintLayout,
            _ => true
        };
    }

    private static bool MatchesSearch(InspectionItem item, string query)
    {
        var dossier = item.Dossier;
        var document = FirstDoc(dossier);
        var haystack = string.Join(" ", new object?[]
        {
            item.Book.Year,
            item.Book.Name,
            dossier.NumberDisplay,
            dossier.Number,
            TypeKey(dossier),
            dossier.Group,
            dossier.BoxNumber,
            dossier.SubNumber,
            dossier.NumberNote,
            dossier.OriginalSummary,
            dossier.MlRow,
            document?.SoKyHieu,
            document?.Ngay,
            document?.TrichYeu,
            document?.TacGia,
            document?.ToSo,
            document?.GhiChu
        });

        return Normalize(haystack).Contains(Normalize(query), StringComparison.Ordinal);
    }

    private static string? NormalizeType(ArchiveData data, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        return data.Books
            .SelectMany(b => b.Dossiers)
            .Select(TypeKey)
            .FirstOrDefault(t => string.Equals(t, requested.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeIssue(string? issue)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            return "all";
        }

        return IssueDefinitions.FirstOrDefault(i =>
            string.Equals(i.Key, issue.Trim(), StringComparison.OrdinalIgnoreCase))?.Key ?? "all";
    }

    private static string TypeKey(Dossier dossier) =>
        string.IsNullOrWhiteSpace(dossier.Type) ? "Chưa phân loại" : dossier.Type.Trim();

    private static DocumentLine? FirstDoc(Dossier dossier) =>
        dossier.Documents.Count == 0 ? null : dossier.Documents[0];

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .ToLowerInvariant();
    }

    private static string Csv(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        text = text.Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

public sealed record IssueDefinition(string Key, string Label, string Description);

public sealed record InspectionItem(ArchiveBook Book, Dossier Dossier);

public sealed class TypeSummary
{
    public string Type { get; init; } = string.Empty;
    public int Count { get; init; }
    public int Years { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public string Range => Count == 0 ? "—" : $"{Min}–{Max}";
}

public sealed class TypeMatrixRow
{
    public string Type { get; init; } = string.Empty;
    public int Total { get; init; }
    public Dictionary<string, int> Counts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
