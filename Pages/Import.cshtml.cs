using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StpWeb.Models;
using StpWeb.Services;

namespace StpWeb.Pages;

public class ImportModel : PageModel
{
    private readonly ArchiveStore _store;
    private readonly ExcelImportService _importer;

    public ImportModel(ArchiveStore store, ExcelImportService importer)
    {
        _store = store;
        _importer = importer;
    }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    public ArchiveData Data { get; private set; } = new();
    public string? Error { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await _store.GetAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Upload is null || Upload.Length == 0)
        {
            Error = "Hãy chọn file Excel cần import.";
            Data = await _store.GetAsync(cancellationToken);
            return Page();
        }

        var extension = Path.GetExtension(Upload.FileName);
        if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            Error = "Chỉ hỗ trợ file .xlsx hoặc .xlsm.";
            Data = await _store.GetAsync(cancellationToken);
            return Page();
        }

        await using var memory = new MemoryStream();
        await Upload.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        var result = _importer.Import(memory, Upload.FileName);
        if (!result.Success || result.Data is null)
        {
            Error = result.Error ?? "Import thất bại.";
            Data = await _store.GetAsync(cancellationToken);
            return Page();
        }

        await _store.SaveAsync(result.Data, cancellationToken);

        TempData["ImportSuccess"] =
            $"Đã import {result.YearCount} năm, {result.TypeCount} loại và {result.DossierCount:N0} hồ sơ từ file {Path.GetFileName(Upload.FileName)}.";

        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostClearAsync(CancellationToken cancellationToken)
    {
        await _store.ClearAsync(cancellationToken);
        TempData["ImportSuccess"] = "Đã xóa dữ liệu đã import.";
        return RedirectToPage("/Import");
    }
}
