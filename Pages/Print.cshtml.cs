using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StpWeb.Models;
using StpWeb.Services;

namespace StpWeb.Pages;

public class PrintModel : PageModel
{
    private readonly ArchiveStore _store;
    private readonly DossierFilter _filter;

    public PrintModel(ArchiveStore store, DossierFilter filter)
    {
        _store = store;
        _filter = filter;
    }

    [BindProperty(SupportsGet = true)]
    public string? BookId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Mode { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Custom { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Numbers { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? MlRow { get; set; }

    public ArchiveBook? Book { get; private set; }
    public IReadOnlyList<Dossier> Dossiers { get; private set; } = Array.Empty<Dossier>();
    public string? Warning { get; private set; }
    public bool NoData { get; private set; }
    public int ScopeCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var data = await _store.GetAsync(cancellationToken);
        if (data.Books.Count == 0)
        {
            NoData = true;
            return;
        }

        var book = data.Books.FirstOrDefault(b =>
            string.Equals(b.Id, BookId, StringComparison.OrdinalIgnoreCase));

        // Dòng ML là duy nhất trên toàn workbook; nếu bookId bị thiếu hoặc sai
        // vẫn tìm được đúng năm chứa hồ sơ.
        if (MlRow is > 0)
        {
            var owner = data.Books.FirstOrDefault(b => b.Dossiers.Any(d => d.MlRow == MlRow.Value));
            if (owner is not null)
            {
                book = owner;
            }
        }

        Book = book ?? data.Books.First();
        BookId = Book.Id;

        var requestedType = Type?.Trim();
        var hasRequestedType = !string.IsNullOrWhiteSpace(requestedType);
        Type = Book.Dossiers
            .Select(d => d.Type)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .FirstOrDefault(t => string.Equals(t, requestedType, StringComparison.OrdinalIgnoreCase));

        if (hasRequestedType && Type is null)
        {
            Dossiers = Array.Empty<Dossier>();
            Warning = $"⚠ Không tìm thấy loại hồ sơ “{requestedType}” trong năm {Book.Name}.";
            ViewData["FilterStatus"] = Warning;
            return;
        }

        if (MlRow is > 0)
        {
            Dossiers = Book.Dossiers.Where(d => d.MlRow == MlRow.Value).ToList();
            Warning = Dossiers.Count == 0
                ? $"⚠ Không tìm thấy hồ sơ ứng với dòng ML {MlRow}."
                : null;
            ScopeCount = Dossiers.Count;
            ViewData["FilterStatus"] = Dossiers.Count > 0
                ? $"In riêng hồ sơ số {Dossiers[0].NumberDisplay} · dòng ML {MlRow}"
                : Warning;
            return;
        }

        var scoped = string.IsNullOrWhiteSpace(Type)
            ? Book.Dossiers.ToList()
            : Book.Dossiers
                .Where(d => string.Equals(d.Type, Type, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ScopeCount = scoped.Count;

        if (scoped.Count == 0)
        {
            Dossiers = Array.Empty<Dossier>();
            Warning = string.IsNullOrWhiteSpace(Type)
                ? $"⚠ Năm {Book.Name} chưa có hồ sơ nào."
                : $"⚠ Năm {Book.Name} không có hồ sơ loại “{Type}”.";
            ViewData["FilterStatus"] = Warning;
            return;
        }

        var scopeMin = scoped.Min(d => d.Number);
        var scopeMax = scoped.Max(d => d.Number);

        var onlyNumbers = ParseNumbers(Numbers);
        if (onlyNumbers.Count > 0)
        {
            var wanted = onlyNumbers.ToHashSet();
            Dossiers = scoped.Where(d => wanted.Contains(d.Number)).ToList();
            Warning = Dossiers.Count == 0
                ? "⚠ Không tìm thấy hồ sơ nào với các số đã chọn."
                : null;
            ViewData["FilterStatus"] = Dossiers.Count > 0
                ? $"In {Dossiers.Count} hồ sơ theo danh sách số đã chọn."
                : Warning;
            return;
        }

        Mode = (Mode ?? "all").Trim().ToLowerInvariant();
        if (Mode is not ("all" or "range" or "custom"))
        {
            Mode = "all";
        }

        if (Mode == "all")
        {
            From = scopeMin;
            To = scopeMax;
        }
        else
        {
            if (From is null)
            {
                From = scopeMin;
            }

            if (To is null)
            {
                To = Math.Min(scopeMax, scopeMin + 49);
            }
        }

        var result = _filter.Apply(Book, Mode, From, To, Custom, Type);
        Dossiers = result.Dossiers;
        Warning = result.Warning;
        ViewData["FilterStatus"] = result.StatusMessage;
        ViewData["ScopeMin"] = scopeMin;
        ViewData["ScopeMax"] = scopeMax;
    }

    private static List<int> ParseNumbers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<int>();
        }

        return text
            .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part.Trim(), out var value) ? value : 0)
            .Where(value => value > 0)
            .Distinct()
            .ToList();
    }
}
