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

    /// <summary>Cột "số tờ" (khoảng trang) được nhận diện trong sheet; mặc định G.</summary>
    public string SheetNumberColumn { get; init; } = "G";

    public string? SheetNumberHeader { get; init; }

    /// <summary>Cột "hồ sơ số" dùng để hiển thị; mặc định C.</summary>
    public string DocumentNumberColumn { get; init; } = "C";

    /// <summary>Cột số hồ sơ dùng để lọc/sắp xếp; mặc định B.</summary>
    public string NumberColumn { get; init; } = "B";

    public IReadOnlyList<string> Catalog { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SummaryRow> Rows { get; init; } = Array.Empty<SummaryRow>();

    public IReadOnlyList<SummaryYearCount> Years { get; init; } = Array.Empty<SummaryYearCount>();

    public IReadOnlyList<SummaryTypeCount> Types { get; init; } = Array.Empty<SummaryTypeCount>();

    public int ExistingListCount { get; init; }

    public int BrokenSummaryCount { get; init; }

    public int FormulaSummaryCount { get; init; }

    /// <summary>Số dòng dữ liệu chưa có số tờ (cột G).</summary>
    public int MissingSheetNumberCount { get; init; }

    /// <summary>Số dòng dữ liệu chưa có hồ sơ số (cột C).</summary>
    public int MissingDocumentNumberCount { get; init; }

    /// <summary>Số dòng dữ liệu chưa có số hồ sơ ở cột dùng để lọc.</summary>
    public int MissingNumberCount { get; init; }

    /// <summary>Số dòng đăng ký kết hôn cần chuẩn hoá tiền tố.</summary>
    public int NormalizableCount { get; init; }

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

    /// <summary>Giá trị cột "hồ sơ số" (C) để hiển thị và đối chiếu.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>Giá trị cột "số tờ" (G), thường là khoảng trang như "01-20".</summary>
    public string SheetNumber { get; init; } = string.Empty;

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

    /// <summary>True khi tiền tố đăng ký kết hôn chưa đúng chuẩn và sẽ được sửa khi xuất.</summary>
    public bool NeedsNormalization { get; init; }

    /// <summary>Câu trích yếu sau khi chuẩn hoá tiền tố, dùng để hiển thị trước khi xuất.</summary>
    public string NormalizedSummary { get; init; } = string.Empty;

    /// <summary>Phần trước danh mục giấy tờ sau khi chuẩn hoá tiền tố.</summary>
    public string NormalizedBaseSummary { get; init; } = string.Empty;

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
    int? Number = null,
    int? YearFrom = null,
    int? YearTo = null,
    bool OnlyMissingSheetNumber = false,
    bool OnlyMissingDocumentNumber = false,
    bool OnlyMissingNumber = false,
    bool MissingSheetNumberFirst = false,
    bool OnlyNeedsNormalization = false)
{
    public static SummaryScope All { get; } = new();
}

public sealed record SummaryPage(
    IReadOnlyList<SummaryRow> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Yêu cầu thay đổi một dòng. <see cref="Items"/> dùng để ghép danh mục giấy tờ;
/// <see cref="SummaryText"/> cho phép sửa toàn bộ câu trích yếu; <see cref="SheetNumber"/>
/// dùng để cập nhật cột số tờ; <see cref="DocumentNumber"/> dùng để cập nhật cột hồ sơ số;
/// <see cref="NumberLabel"/> dùng để cập nhật cột số hồ sơ (null = không đổi, chuỗi rỗng = xoá).
/// </summary>
public sealed record SummaryChange(
    int RowNumber,
    IReadOnlyList<string> Items,
    string? SummaryText = null,
    string? SheetNumber = null,
    string? DocumentNumber = null,
    string? NumberLabel = null);

public sealed record SummaryPreviewRow(
    int RowNumber,
    string Summary,
    bool Changed,
    string OriginalSummary = "",
    string? SheetNumber = null,
    string? OriginalSheetNumber = null,
    bool SummaryChanged = false,
    bool SheetNumberChanged = false,
    string? DocumentNumber = null,
    string? OriginalDocumentNumber = null,
    bool DocumentNumberChanged = false,
    string? NumberLabel = null,
    string? OriginalNumberLabel = null,
    bool NumberLabelChanged = false);

public sealed record SummaryExportResult(
    byte[] Content,
    string FileName,
    string ContentType,
    int ChangedRows,
    int NormalizedRows = 0);

/// <summary>Kết quả xem trước bản xuất lọc: số hồ sơ trong phạm vi và các thay đổi được áp dụng.</summary>
public sealed record SummaryFilteredPreview(
    int TotalRows,
    int ChangedRows,
    int IgnoredChanges,
    IReadOnlyList<SummaryPreviewRow> Items,
    int NormalizedRows = 0,
    bool Truncated = false);

public sealed record SummaryFilteredExportResult(
    byte[] Content,
    string FileName,
    string ContentType,
    int ChangedRows,
    int TotalRows,
    int IgnoredChanges,
    int NormalizedRows = 0);

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
        IReadOnlyList<SummaryChange> changes,
        IReadOnlyList<int>? onlyRowNumbers = null);

    /// <summary>Xem trước bản xuất lọc theo phạm vi hiện tại.</summary>
    SummaryFilteredPreview PreviewFiltered(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        SummaryScope scope);

    /// <summary>Xuất lại đúng workbook gốc, chỉ thay các ô trích yếu đã chọn.</summary>
    Task<SummaryExportResult> ExportAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        CancellationToken cancellationToken = default,
        string? outputFileName = null,
        IReadOnlyList<int>? onlyRowNumbers = null);

    /// <summary>
    /// Xuất một workbook nhẹ: chỉ giữ sheet trích yếu và các hồ sơ trong phạm vi,
    /// áp dụng các thay đổi thuộc phạm vi, xoá mọi sheet và hồ sơ còn lại.
    /// </summary>
    Task<SummaryFilteredExportResult> ExportFilteredAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        SummaryScope scope,
        CancellationToken cancellationToken = default,
        string? outputFileName = null);

    bool DeleteSession(Guid sessionId);

    /// <summary>Báo cáo dữ liệu thiếu theo từng năm trong phiên hiện tại.</summary>
    IReadOnlyList<SummaryYearReport> GetReport(Guid sessionId);
}

/// <summary>Thống kê dữ liệu thiếu của một năm.</summary>
public sealed record SummaryYearReport(
    int Year,
    int Total,
    int MissingSheetNumber,
    int MissingDocumentNumber,
    int MissingNumber,
    int NeedsNormalization);
