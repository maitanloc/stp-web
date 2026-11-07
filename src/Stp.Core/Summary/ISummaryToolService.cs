namespace Stp.Core.Summary;

/// <summary>
/// Một workbook đang được mở trong công cụ bổ sung trích yếu. Dữ liệu được giữ
/// trong bộ nhớ để lọc/phân trang nhanh; file gốc vẫn nằm trong thư mục upload
/// để xuất lại nguyên vẹn.
/// </summary>
public sealed class SummaryWorkbook
{
    public Guid SessionId { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public string SheetName { get; init; } = string.Empty;

    public string SummaryColumn { get; init; } = "D";

    public string? SummaryHeader { get; init; }

    public IReadOnlyList<string> Catalog { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SummaryRow> Rows { get; init; } = Array.Empty<SummaryRow>();

    public IReadOnlyList<SummaryYearCount> Years { get; init; } = Array.Empty<SummaryYearCount>();

    public IReadOnlyList<SummaryTypeCount> Types { get; init; } = Array.Empty<SummaryTypeCount>();

    public int ExistingListCount { get; init; }

    public int BrokenSummaryCount { get; init; }

    public int FormulaSummaryCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Một dòng hồ sơ trong sheet ML.</summary>
public sealed class SummaryRow
{
    public int RowNumber { get; init; }

    public int? Year { get; init; }

    public string NumberLabel { get; init; } = string.Empty;

    /// <summary>Toàn bộ nội dung ô trích yếu ban đầu.</summary>
    public string OriginalSummary { get; init; } = string.Empty;

    /// <summary>Phần trước danh mục giấy tờ trong ngoặc (không thay đổi khi xuất).</summary>
    public string BaseSummary { get; init; } = string.Empty;

    /// <summary>Các mục đang có trong ngoặc; giữ nguyên thứ tự và chữ gốc.</summary>
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();

    /// <summary>True khi ngoặc cuối được nhận diện là danh mục giấy tờ.</summary>
    public bool HasDocumentList { get; init; }

    public bool HasFormula { get; init; }

    /// <summary>True khi ô chứa công thức lỗi hoặc chuỗi bắt đầu bằng ="..." và #REF!.</summary>
    public bool IsBrokenSummary { get; init; }

    public string? Group { get; init; }

    public string? Type { get; init; }
}

public sealed record SummaryYearCount(int Year, int Count);

public sealed record SummaryTypeCount(string Name, int Count);

public sealed record SummaryPage(
    IReadOnlyList<SummaryRow> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>Yêu cầu thay đổi danh mục giấy tờ của một dòng.</summary>
public sealed record SummaryChange(int RowNumber, IReadOnlyList<string> Items);

public sealed record SummaryPreviewRow(int RowNumber, string Summary, bool Changed);

public sealed record SummaryExportResult(
    byte[] Content,
    string FileName,
    string ContentType,
    int ChangedRows);

public interface ISummaryToolService
{
    /// <summary>Lưu file upload, đọc sheet trích yếu và tạo phiên làm việc.</summary>
    Task<SummaryWorkbook> CreateSessionAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);

    SummaryWorkbook? GetSession(Guid sessionId);

    SummaryPage GetRows(
        Guid sessionId,
        int? year,
        string? type,
        string? search,
        bool onlyExisting,
        int page,
        int pageSize);

    IReadOnlyList<SummaryPreviewRow> Preview(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes);

    /// <summary>Xuất lại đúng workbook gốc, chỉ thay các ô trích yếu đã chọn.</summary>
    Task<SummaryExportResult> ExportAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        CancellationToken cancellationToken = default);

    bool DeleteSession(Guid sessionId);
}
