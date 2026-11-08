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

    /// <summary>Dòng tiêu đề của sheet trích yếu; các dòng phía trên luôn được giữ khi lọc.</summary>
    public int HeaderRow { get; init; } = 1;

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

    /// <summary>Số hồ sơ dạng số lấy từ phần đầu cột B; null nếu nhãn không bắt đầu bằng chữ số.</summary>
    public int? Number { get; init; }

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

public enum SummaryNumberMode
{
    All,
    Range,
    Single
}

/// <summary>
/// Phạm vi hồ sơ dùng chung cho danh sách và bản xuất lọc. Các điều kiện được
/// kết hợp với nhau (AND); phạm vi số hồ sơ chỉ áp dụng cho nhãn bắt đầu bằng số.
/// </summary>
public sealed record SummaryScope(
    int? Year = null,
    string? Type = null,
    string? Search = null,
    bool OnlyExisting = false,
    SummaryNumberMode NumberMode = SummaryNumberMode.All,
    int? NumberFrom = null,
    int? NumberTo = null,
    int? Number = null)
{
    public static SummaryScope All { get; } = new();
}

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

/// <summary>Kết quả xem trước bản xuất lọc: số hồ sơ trong phạm vi và các thay đổi được áp dụng.</summary>
public sealed record SummaryFilteredPreview(
    int TotalRows,
    int ChangedRows,
    int IgnoredChanges,
    IReadOnlyList<SummaryPreviewRow> Items);

public sealed record SummaryFilteredExportResult(
    byte[] Content,
    string FileName,
    string ContentType,
    int ChangedRows,
    int TotalRows,
    int IgnoredChanges);

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
        SummaryScope scope,
        int page,
        int pageSize);

    IReadOnlyList<SummaryPreviewRow> Preview(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes);

    /// <summary>Xem trước bản xuất lọc theo phạm vi hiện tại.</summary>
    SummaryFilteredPreview PreviewFiltered(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        SummaryScope scope);

    /// <summary>Xuất lại đúng workbook gốc, chỉ thay các ô trích yếu đã chọn.</summary>
    Task<SummaryExportResult> ExportAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xuất một workbook nhẹ: chỉ giữ sheet trích yếu và các hồ sơ trong phạm vi,
    /// áp dụng các thay đổi thuộc phạm vi, xoá mọi sheet và hồ sơ còn lại.
    /// </summary>
    Task<SummaryFilteredExportResult> ExportFilteredAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        SummaryScope scope,
        CancellationToken cancellationToken = default);

    bool DeleteSession(Guid sessionId);
}
