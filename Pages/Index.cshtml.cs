using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StpWeb.Models;
using StpWeb.Services;

namespace StpWeb.Pages;

public class IndexModel : PageModel
{
    private readonly ArchiveStore _store;
    private readonly DossierFilter _filter;

    public IndexModel(ArchiveStore store, DossierFilter filter)
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

    public ArchiveData Data { get; private set; } = new();
    public ArchiveBook? Book { get; private set; }
    public int ScopeCount { get; private set; }
    public int ScopeMin { get; private set; }
    public int ScopeMax { get; private set; }

    public FilterResult Result { get; private set; } = new()
    {
        Dossiers = Array.Empty<Dossier>(),
        Total = 0,
        StatusMessage = "Chưa có dữ liệu."
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await _store.GetAsync(cancellationToken);

        if (Data.Books.Count == 0)
        {
            return;
        }

        Mode = (Mode ?? "all").Trim().ToLowerInvariant();
        if (Mode is not ("all" or "range" or "custom"))
        {
            Mode = "all";
        }

        Book = Data.Books.FirstOrDefault(b => string.Equals(b.Id, BookId, StringComparison.OrdinalIgnoreCase))
               ?? Data.Books.First();

        BookId = Book.Id;

        // Chuẩn hóa lại chữ hoa/thường theo dữ liệu thật và bỏ chọn nếu
        // loại hồ sơ không tồn tại trong năm đang xem.
        var requestedType = Type?.Trim();
        Type = Book.Dossiers
            .Select(d => d.Type)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .FirstOrDefault(t => string.Equals(t, requestedType, StringComparison.OrdinalIgnoreCase));

        var scoped = string.IsNullOrWhiteSpace(Type)
            ? Book.Dossiers
            : Book.Dossiers.Where(d => string.Equals(d.Type, Type, StringComparison.OrdinalIgnoreCase)).ToList();

        ScopeCount = scoped.Count;
        ScopeMin = ScopeCount == 0 ? 0 : scoped.Min(d => d.Number);
        ScopeMax = ScopeCount == 0 ? 0 : scoped.Max(d => d.Number);

        // Khi mở trang hoặc đổi năm/loại: đặt sẵn khoảng 50 số đầu cho tiện.
        // Khi người dùng đã cố tình để trống ô trong chế độ "Từ số - đến số" thì giữ trống để báo lỗi.
        if (Mode == "all" || !Request.Query.ContainsKey(nameof(From)))
        {
            From = ScopeMin;
        }

        if (Mode == "all" || !Request.Query.ContainsKey(nameof(To)))
        {
            To = Math.Min(ScopeMax, ScopeMin + 49);
        }

        Result = _filter.Apply(Book, Mode, From, To, Custom, Type);
    }
}
