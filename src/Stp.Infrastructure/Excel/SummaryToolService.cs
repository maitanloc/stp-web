using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Stp.Core.Storage;
using Stp.Core.Summary;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Công cụ bổ sung danh mục giấy tờ vào phần ngoặc cuối cột trích yếu.
/// Đọc workbook bằng ClosedXML nhưng khi xuất chỉ sửa XML của đúng ô cần đổi,
/// giữ nguyên mọi sheet, công thức, định dạng, macro và phần mở rộng khác.
/// </summary>
public sealed class SummaryToolService : ISummaryToolService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private const string MarkerFileName = "summary-session.json";
    private const int MaxPreviewRows = 500;

    private static readonly string[] DefaultCatalog =
    {
        "phiếu đề xuất",
        "giấy biên nhận",
        "đơn xin đăng ký kết hôn",
        "đơn xin đăng ký kết hôn với người nước ngoài",
        "đơn xin xác nhận độc thân",
        "đơn xin chứng nhận độc thân",
        "giấy chứng nhận đăng ký nhân khẩu",
        "CMND",
        "bộ công hàm",
        "chứng chỉ chuyên khoa",
        "sơ yếu lí lịch",
        "giấy khai sinh",
        "giấy chứng nhận khai sinh",
        "khai sinh",
        "đơn xin xác nhận đính hôn",
        "hộ chiếu",
        "Quyết định công nhận thuận tình ly hôn",
        "Đơn xin xác nhận tình trạng hôn nhân",
        "giấy chứng nhận không có trở ngại trong việc kết hôn",
        "phiếu khám sức khỏe",
        "giấy chứng nhận sức khỏe",
        "thị thực",
        "phiếu nhập xuất cảnh",
        "phiếu xuất nhập cảnh",
        "giấy xác nhận độc thân",
        "giấy báo tử",
        "hộ khẩu",
        "sổ hộ khẩu",
        "giấy khai đăng ký kết hôn",
        "giấy thông hành",
        "đơn xin thay đổi cải chính hộ tịch",
        "tờ khai danh dự về khai sinh",
        "chứng nhận hộ tịch",
        "tờ khai tình trạng gia đình",
        "tờ khai sanh danh dự",
        "chứng nhận kết hôn",
        "giấy kết hôn",
        "trích lục kết hôn",
        "giấy phép kết hôn",
        "chứng chỉ nhập tịch",
        "Sổ mua hàng gia đình",
        "tờ khai tuyên thệ",
        "tờ ly hôn",
        "bản tự tường trình",
        "tờ báo cáo hôn thú",
        "lời khai về tình trạng hôn nhân",
        "giấy chứng nhận kết hôn",
        "quyết định",
        "biên bản",
        "biên lai thu lệ phí",
        "tờ khai đề nghị",
        "phiếu hẹn",
        "bản sao hộ khẩu",
        "tờ khai bổ sung",
        "giấy hẹn",
        "lý lịch tư pháp",
        "giấy chứng nhận tạm trú",
        "ghi chú",
        "trích lục ghi chú kết hôn"
    };

    private static readonly string[] DocumentKeywords =
    {
        "giay", "don", "phieu", "chung", "khai", "ho khau", "cmnd", "cong ham",
        "thi thuc", "ho chieu", "so yeu", "trich luc", "quyet dinh", "bien nhan",
        "xac nhan", "chuyen khoa", "kham suc khoe", "dang ky"
    };

    private readonly IFileStorage _storage;
    private readonly ILogger<SummaryToolService> _logger;
    private readonly ConcurrentDictionary<Guid, SummaryWorkbook> _sessions = new();

    public SummaryToolService(IFileStorage storage, ILogger<SummaryToolService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<SummaryWorkbook> CreateSessionAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var stored = await _storage
            .SaveUploadAsync(content, fileName, sessionId, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var session = Parse(sessionId, stored.FullPath, stored.FileName, stored.SizeBytes);
            _sessions[sessionId] = session;

            var directory = _storage.GetJobUploadDirectory(sessionId);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                    Path.Combine(directory, MarkerFileName),
                    "{\"createdAt\":\"" + session.CreatedAt.ToString("O", CultureInfo.InvariantCulture) + "\"}",
                    cancellationToken)
                .ConfigureAwait(false);

            CleanupExpired();
            return session;
        }
        catch (Exception ex) when (ex is not InvalidDataException and not OperationCanceledException)
        {
            _storage.DeleteDirectory(_storage.GetJobUploadDirectory(sessionId));

            if (ex is IOException or UnauthorizedAccessException)
            {
                throw;
            }

            throw new InvalidDataException(
                "Không đọc được workbook. File có thể bị hỏng hoặc không đúng định dạng Excel.",
                ex);
        }
        catch
        {
            _storage.DeleteDirectory(_storage.GetJobUploadDirectory(sessionId));
            throw;
        }
    }

    public SummaryWorkbook? GetSession(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return session;
        }

        return TryRestore(sessionId);
    }

    public IReadOnlyList<SummarySessionInfo> ListSessions()
    {
        _sessions.Values.ToList();

        var uploadsRoot = Path.Combine(_storage.RootPath, "uploads");
        if (!Directory.Exists(uploadsRoot))
        {
            return Array.Empty<SummarySessionInfo>();
        }

        var result = new List<SummarySessionInfo>();

        foreach (var directory in Directory.EnumerateDirectories(uploadsRoot))
        {
            if (!Guid.TryParse(Path.GetFileName(directory), out var sessionId))
            {
                continue;
            }

            var session = GetSession(sessionId);
            if (session is not null)
            {
                result.Add(new SummarySessionInfo(
                    session.SessionId,
                    session.FileName,
                    session.FileSizeBytes,
                    session.CreatedAt,
                    session.Rows.Count));
            }
        }

        return result
            .OrderByDescending(item => item.CreatedAt)
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// Khôi phục phiên từ file upload đã lưu trên đĩa (sống sót F5/mất kết nối/restart API).
    /// </summary>
    private SummaryWorkbook? TryRestore(Guid sessionId)
    {
        try
        {
            var directory = _storage.GetJobUploadDirectory(sessionId);
            var markerPath = Path.Combine(directory, MarkerFileName);
            if (!File.Exists(markerPath))
            {
                return null;
            }

            // Chỉ dọn phiên quá hạn như cũ; phiên còn hạn thì đọc lại từ file.
            var marker = File.ReadAllText(markerPath);
            var createdAt = DateTimeOffset.UtcNow;
            var match = Regex.Match(marker, "\"createdAt\"\\s*:\\s*\"([^\"]+)\"");
            if (match.Success && DateTimeOffset.TryParse(
                    match.Groups[1].Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                createdAt = parsed;
            }

            if (createdAt < DateTimeOffset.UtcNow - SessionLifetime)
            {
                // Quá hạn: xoá cả thư mục và bỏ qua.
                _storage.DeleteDirectory(directory);
                return null;
            }

            var filePath = Directory.EnumerateFiles(directory)
                .FirstOrDefault(path =>
                    path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xltx", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xltm", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            var session = Parse(sessionId, filePath, fileInfo.Name, fileInfo.Length);
            _sessions[sessionId] = session;
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không khôi phục được phiên trích yếu {SessionId}", sessionId);
            return null;
        }
    }

    public SummaryPage GetRows(
        Guid sessionId,
        SummaryScope scope,
        int page,
        int pageSize)
    {
        var session = RequireSession(sessionId);
        var filtered = FilterRows(session.Rows, scope);
        var total = filtered.Count;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new SummaryPage(items, total, page, pageSize);
    }

    public IReadOnlyList<SummaryPreviewRow> Preview(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        IReadOnlyList<int>? onlyRowNumbers = null)
    {
        var session = RequireSession(sessionId);
        var byRow = session.Rows.ToDictionary(row => row.RowNumber);
        var result = new List<SummaryPreviewRow>();

        foreach (var change in changes)
        {
            if (!byRow.TryGetValue(change.RowNumber, out var row))
            {
                throw new ArgumentException(
                    $"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }
        }

        var changeMap = changes
            .GroupBy(change => change.RowNumber)
            .ToDictionary(group => group.Key, group => group.Last());

        var allowed = onlyRowNumbers is null || onlyRowNumbers.Count == 0
            ? null
            : onlyRowNumbers.ToHashSet();

        // Xuất toàn bộ luôn chuẩn hoá mọi dòng đăng ký kết hôn, trừ khi người dùng
        // chỉ định "chỉ xử lý dòng đã chọn" thì chỉ quét các dòng đó.
        foreach (var row in session.Rows)
        {
            if (allowed is not null && !allowed.Contains(row.RowNumber))
            {
                continue;
            }

            changeMap.TryGetValue(row.RowNumber, out var change);
            var plan = CreatePlan(row, change, session.Catalog);
            if (plan.SummaryChanged || plan.SheetNumberChanged || plan.DocumentNumberChanged || plan.NumberLabelChanged)
            {
                result.Add(ToPreview(plan));
                if (result.Count >= MaxPreviewRows)
                {
                    break;
                }
            }
        }

        return result;
    }

    public SummaryFilteredPreview PreviewFiltered(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        SummaryScope scope)
    {
        var session = RequireSession(sessionId);
        var scopedRows = FilterRows(session.Rows, scope);
        var allowed = scopedRows.Select(row => row.RowNumber).ToHashSet();
        var byRow = session.Rows.ToDictionary(row => row.RowNumber);

        foreach (var change in changes)
        {
            if (!byRow.ContainsKey(change.RowNumber))
            {
                throw new ArgumentException(
                    $"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }
        }

        var changeMap = changes
            .GroupBy(change => change.RowNumber)
            .ToDictionary(group => group.Key, group => group.Last());
        var result = new List<SummaryPreviewRow>();
        var ignored = changes.Count(change => !allowed.Contains(change.RowNumber));
        var changedRows = 0;
        var normalizedRows = 0;

        foreach (var row in scopedRows)
        {
            changeMap.TryGetValue(row.RowNumber, out var change);
            var plan = CreatePlan(row, change, session.Catalog);

            if (plan.SummaryChanged || plan.SheetNumberChanged || plan.DocumentNumberChanged || plan.NumberLabelChanged)
            {
                changedRows++;
                if (plan.Normalized)
                {
                    normalizedRows++;
                }

                if (result.Count < MaxPreviewRows)
                {
                    result.Add(ToPreview(plan));
                }
            }
        }

        return new SummaryFilteredPreview(
            scopedRows.Count,
            changedRows,
            ignored,
            result,
            normalizedRows,
            changedRows > result.Count);
    }

    public async Task<SummaryExportResult> ExportAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        CancellationToken cancellationToken = default,
        string? outputFileName = null,
        IReadOnlyList<int>? onlyRowNumbers = null)
    {
        var session = RequireSession(sessionId);
        var byRow = session.Rows.ToDictionary(row => row.RowNumber);

        foreach (var change in changes)
        {
            if (!byRow.ContainsKey(change.RowNumber))
            {
                throw new ArgumentException(
                    $"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }
        }

        var changeMap = changes
            .GroupBy(change => change.RowNumber)
            .ToDictionary(group => group.Key, group => group.Last());
        var patches = new List<SummaryCellPatch>();
        var changedRows = new HashSet<int>();
        var normalizedRows = 0;
        var allowed = onlyRowNumbers is null || onlyRowNumbers.Count == 0
            ? null
            : onlyRowNumbers.ToHashSet();

        // Xuất toàn bộ: chuẩn hoá mọi dòng đăng ký kết hôn thuộc workbook,
        // trừ khi người dùng chọn "chỉ dòng đã chọn" thì chỉ quét các dòng đó.
        foreach (var row in session.Rows)
        {
            if (allowed is not null && !allowed.Contains(row.RowNumber))
            {
                continue;
            }

            changeMap.TryGetValue(row.RowNumber, out var change);
            var plan = CreatePlan(row, change, session.Catalog);

            if (plan.SummaryChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.SummaryColumn,
                    plan.Summary));
                changedRows.Add(row.RowNumber);
            }

            if (plan.SheetNumberChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.SheetNumberColumn,
                    plan.SheetNumber ?? string.Empty));
                changedRows.Add(row.RowNumber);
            }

            if (plan.DocumentNumberChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.DocumentNumberColumn,
                    plan.DocumentNumber ?? string.Empty));
                changedRows.Add(row.RowNumber);
            }

            if (plan.NumberLabelChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.NumberColumn,
                    plan.NumberLabel ?? string.Empty));
                changedRows.Add(row.RowNumber);
            }

            if (plan.Normalized)
            {
                normalizedRows++;
            }
        }

        if (patches.Count == 0)
        {
            throw new InvalidOperationException(
                "Nội dung đã chọn trùng với trích yếu hiện có, không cần xuất file.");
        }

        var outputDirectory = _storage.GetJobOutputDirectory(sessionId);
        Directory.CreateDirectory(outputDirectory);

        var extension = Path.GetExtension(session.FileName);
        var defaultName = Path.GetFileNameWithoutExtension(session.FileName) + "_trichyeu" + extension;
        var outputName = SanitizeOutputFileName(outputFileName, defaultName, extension);
        var outputPath = _storage.GetUniquePath(outputDirectory, outputName);

        try
        {
            SummaryXlsxPatcher.Apply(
                session.FilePath,
                outputPath,
                session.SheetName,
                patches);

            var content = await File
                .ReadAllBytesAsync(outputPath, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Xuất trích yếu {FileName}: {Rows} dòng sửa, {Normalized} dòng chuẩn hoá, {Bytes} byte.",
                session.FileName,
                changedRows.Count,
                normalizedRows,
                content.Length);

            return new SummaryExportResult(
                content,
                Path.GetFileName(outputPath),
                ContentTypeFor(extension),
                changedRows.Count,
                normalizedRows);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    public async Task<SummaryFilteredExportResult> ExportFilteredAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        SummaryScope scope,
        CancellationToken cancellationToken = default,
        string? outputFileName = null)
    {
        var session = RequireSession(sessionId);
        var scopedRows = FilterRows(session.Rows, scope);
        if (scopedRows.Count == 0)
        {
            throw new InvalidOperationException(
                "Phạm vi xuất không có hồ sơ nào. Hãy nới điều kiện lọc trước khi xuất.");
        }

        var allowed = scopedRows.Select(row => row.RowNumber).ToHashSet();
        var byRow = session.Rows.ToDictionary(row => row.RowNumber);

        foreach (var change in changes)
        {
            if (!byRow.ContainsKey(change.RowNumber))
            {
                throw new ArgumentException(
                    $"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }
        }

        var changeMap = changes
            .GroupBy(change => change.RowNumber)
            .ToDictionary(group => group.Key, group => group.Last());
        var patches = new List<SummaryCellPatch>();
        var changedRows = new HashSet<int>();
        var ignored = changes.Count(change => !allowed.Contains(change.RowNumber));
        var normalizedRows = 0;

        foreach (var row in scopedRows)
        {
            changeMap.TryGetValue(row.RowNumber, out var change);
            var plan = CreatePlan(row, change, session.Catalog);

            if (plan.SummaryChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.SummaryColumn,
                    plan.Summary));
                changedRows.Add(row.RowNumber);
            }

            if (plan.SheetNumberChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.SheetNumberColumn,
                    plan.SheetNumber ?? string.Empty));
                changedRows.Add(row.RowNumber);
            }

            if (plan.DocumentNumberChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.DocumentNumberColumn,
                    plan.DocumentNumber ?? string.Empty));
                changedRows.Add(row.RowNumber);
            }

            if (plan.NumberLabelChanged)
            {
                patches.Add(new SummaryCellPatch(
                    row.RowNumber,
                    session.NumberColumn,
                    plan.NumberLabel ?? string.Empty));
                changedRows.Add(row.RowNumber);
            }

            if (plan.Normalized)
            {
                normalizedRows++;
            }
        }

        var outputDirectory = _storage.GetJobOutputDirectory(sessionId);
        Directory.CreateDirectory(outputDirectory);

        var extension = Path.GetExtension(session.FileName);
        var defaultFilteredName = Path.GetFileNameWithoutExtension(session.FileName)
            + "_loc"
            + ScopeSuffix(scope)
            + extension;
        var outputName = SanitizeOutputFileName(outputFileName, defaultFilteredName, extension);
        var outputPath = _storage.GetUniquePath(outputDirectory, outputName);

        try
        {
            SummaryXlsxPatcher.ApplyFiltered(
                session.FilePath,
                outputPath,
                session.SheetName,
                session.HeaderRow,
                allowed,
                patches);

            var content = await File
                .ReadAllBytesAsync(outputPath, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Xuất bản lọc {FileName}: {Rows} hồ sơ, {Count} dòng sửa, {Normalized} dòng chuẩn hoá, {Ignored} thay đổi ngoài phạm vi, {Bytes} byte.",
                session.FileName,
                scopedRows.Count,
                changedRows.Count,
                normalizedRows,
                ignored,
                content.Length);

            return new SummaryFilteredExportResult(
                content,
                Path.GetFileName(outputPath),
                ContentTypeFor(extension),
                changedRows.Count,
                scopedRows.Count,
                ignored,
                normalizedRows);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    public bool DeleteSession(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out _))
        {
            return false;
        }

        _storage.DeleteDirectory(_storage.GetJobUploadDirectory(sessionId));
        return true;
    }

    private SummaryWorkbook Parse(
        Guid sessionId,
        string filePath,
        string fileName,
        long fileSizeBytes)
    {
        using var workbook = new XLWorkbook(filePath);
        var (sheet, headerRow, summaryColumn, summaryHeader) = FindSummarySheet(workbook);

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? summaryColumn;
        var summaryColumnLetter = sheet.Column(summaryColumn).ColumnLetter();

        var numberColumn = DetectNumberColumn(sheet, lastRow);
        var documentNumberColumn = DetectDocumentNumberColumn(sheet, headerRow, lastColumn);
        var sheetNumberColumn = DetectSheetNumberColumn(sheet, headerRow, lastRow, lastColumn);
        var sheetNumberHeader = sheet.Cell(headerRow, sheetNumberColumn).GetString().Trim();
        var headerCatalog = ExtractHeaderCatalog(sheet, headerRow, summaryColumn, lastColumn);
        var rawRows = ReadRawRows(
            sheet,
            headerRow,
            lastRow,
            summaryColumn,
            numberColumn,
            documentNumberColumn,
            sheetNumberColumn);

        var observed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawRows)
        {
            var group = SplitTrailingGroup(raw.Text);
            if (group is null)
            {
                continue;
            }

            var candidateItems = SplitItems(group.Value.Inside);
            if (!LooksLikeDocumentList(candidateItems, headerCatalog))
            {
                continue;
            }

            foreach (var item in candidateItems)
            {
                observed[item] = observed.GetValueOrDefault(item) + 1;
            }
        }

        var catalog = MergeCatalog(headerCatalog, observed, DefaultCatalog);
        var rows = rawRows.Select(raw =>
        {
            var (baseSummary, items, hasDocumentList) = ParseSummary(raw.Text, catalog);
            var isBroken = LooksLikeBrokenSummary(raw.Text);
            var needsNormalization = MarriageSummaryNormalizer.NeedsNormalization(raw.Text);
            var normalizedSummary = needsNormalization
                ? MarriageSummaryNormalizer.NormalizeIfNeeded(raw.Text)
                : raw.Text;
            var normalizedBaseSummary = needsNormalization
                ? NormalizeBaseSummary(normalizedSummary, hasDocumentList)
                : baseSummary;

            return new SummaryRow
            {
                RowNumber = raw.RowNumber,
                Year = raw.Year,
                Number = ParseNumberLabel(raw.NumberLabel),
                NumberLabel = raw.NumberLabel,
                DocumentNumber = raw.DocumentNumber,
                SheetNumber = raw.SheetNumber,
                OriginalSummary = raw.Text,
                BaseSummary = baseSummary,
                Items = items,
                HasDocumentList = hasDocumentList,
                HasFormula = raw.HasFormula,
                IsBrokenSummary = isBroken,
                NeedsNormalization = needsNormalization,
                NormalizedSummary = normalizedSummary,
                NormalizedBaseSummary = normalizedBaseSummary,
                Group = raw.Group,
                Type = raw.Type
            };
        }).ToList();

        _logger.LogInformation(
            "Đọc trích yếu {FileName}: sheet {Sheet}, {Rows} hồ sơ, {Catalog} loại giấy tờ.",
            fileName,
            sheet.Name,
            rows.Count,
            catalog.Count);

        return new SummaryWorkbook
        {
            SessionId = sessionId,
            FilePath = filePath,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            SheetName = sheet.Name,
            HeaderRow = headerRow,
            SummaryColumn = summaryColumnLetter,
            SummaryHeader = summaryHeader,
            SheetNumberColumn = sheet.Column(sheetNumberColumn).ColumnLetter(),
            SheetNumberHeader = sheetNumberHeader,
            DocumentNumberColumn = sheet.Column(documentNumberColumn).ColumnLetter(),
            NumberColumn = sheet.Column(numberColumn).ColumnLetter(),
            Catalog = catalog,
            Rows = rows,
            Years = rows
                .Where(row => row.Year.HasValue)
                .GroupBy(row => row.Year!.Value)
                .Select(group => new SummaryYearCount(group.Key, group.Count()))
                .OrderBy(item => item.Year)
                .ToList(),
            Types = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Type))
                .GroupBy(row => row.Type!, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new SummaryTypeCount(group.Key, group.Count()))
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            ExistingListCount = rows.Count(row => row.HasDocumentList),
            BrokenSummaryCount = rows.Count(row => row.IsBrokenSummary),
            FormulaSummaryCount = rows.Count(row => row.HasFormula),
            MissingSheetNumberCount = rows.Count(row => string.IsNullOrWhiteSpace(row.SheetNumber)),
            MissingDocumentNumberCount = rows.Count(row => string.IsNullOrWhiteSpace(row.DocumentNumber)),
            MissingNumberCount = rows.Count(row => string.IsNullOrWhiteSpace(row.NumberLabel)),
            NormalizableCount = rows.Count(row => row.NeedsNormalization)
        };
    }

    private static (IXLWorksheet Sheet, int HeaderRow, int SummaryColumn, string? Header) FindSummarySheet(
        XLWorkbook workbook)
    {
        var sheets = workbook.Worksheets.ToList();
        var preferred = sheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, "ML", StringComparison.OrdinalIgnoreCase));

        var candidates = preferred is null
            ? sheets
            : new[] { preferred }.Concat(sheets.Where(sheet => !ReferenceEquals(sheet, preferred)));

        foreach (var sheet in candidates)
        {
            var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 0, 20);
            var lastColumn = Math.Min(sheet.LastColumnUsed()?.ColumnNumber() ?? 0, 80);

            for (var row = 1; row <= lastRow; row++)
            {
                for (var column = 1; column <= lastColumn; column++)
                {
                    var text = sheet.Cell(row, column).GetString();
                    if (IsSummaryHeader(text))
                    {
                        return (sheet, row, column, text.Trim());
                    }
                }
            }
        }

        if (preferred is not null)
        {
            return (preferred, 1, 4, preferred.Cell(1, 4).GetString());
        }

        throw new InvalidDataException(
            "Không tìm thấy sheet ML hoặc cột có tiêu đề 'Trích yếu nội dung' trong workbook.");
    }

    private static int DetectNumberColumn(IXLWorksheet sheet, int lastRow)
    {
        var bCount = 0;
        var cCount = 0;
        var sampleEnd = Math.Min(lastRow, 400);

        for (var row = 2; row <= sampleEnd; row++)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Cell(row, 2).GetString()))
            {
                bCount++;
            }

            if (!string.IsNullOrWhiteSpace(sheet.Cell(row, 3).GetString()))
            {
                cCount++;
            }
        }

        return bCount >= cCount ? 2 : 3;
    }

    private static int DetectDocumentNumberColumn(
        IXLWorksheet sheet,
        int headerRow,
        int lastColumn)
    {
        for (var column = 1; column <= lastColumn; column++)
        {
            var header = NormalizeKey(sheet.Cell(headerRow, column).GetString());
            if (header.Contains("ho so so", StringComparison.Ordinal)
                || header.Contains("so ho so", StringComparison.Ordinal))
            {
                return column;
            }
        }

        return Math.Min(3, Math.Max(1, lastColumn));
    }

    /// <summary>
    /// Nhận diện cột "số tờ" bằng dữ liệu thực tế (khoảng trang dạng 01-20),
    /// không chỉ dựa vào tiêu đề vì nhiều file đặt tiêu đề lệch cột.
    /// </summary>
    private static int DetectSheetNumberColumn(
        IXLWorksheet sheet,
        int headerRow,
        int lastRow,
        int lastColumn)
    {
        var sampleEnd = Math.Min(lastRow, headerRow + 600);
        var bestColumn = 0;
        var bestScore = int.MinValue;

        for (var column = 1; column <= lastColumn; column++)
        {
            var rangeCount = 0;
            var textCount = 0;

            for (var row = headerRow + 1; row <= sampleEnd; row++)
            {
                var value = sheet.Cell(row, column).GetString().Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                if (Regex.IsMatch(value, @"\d\s*[-–—]\s*\d", RegexOptions.CultureInvariant))
                {
                    rangeCount++;
                }
                else if (value.Any(char.IsLetter) || value.Contains('/'))
                {
                    textCount++;
                }
            }

            var header = NormalizeKey(sheet.Cell(headerRow, column).GetString());
            var headerScore = header.Contains("so to", StringComparison.Ordinal)
                              || header.Contains("so trang", StringComparison.Ordinal)
                ? 1
                : 0;

            if (rangeCount == 0 && headerScore == 0)
            {
                continue;
            }

            var score = (rangeCount * 10) + (headerScore * 5) - (textCount * 3);
            if (score > bestScore)
            {
                bestScore = score;
                bestColumn = column;
            }
        }

        if (bestColumn > 0)
        {
            return bestColumn;
        }

        return lastColumn >= 7 ? 7 : Math.Max(1, lastColumn);
    }

    private static List<string> ExtractHeaderCatalog(
        IXLWorksheet sheet,
        int headerRow,
        int summaryColumn,
        int lastColumn)
    {
        var best = new List<string>();
        var bestScore = -1;
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? headerRow, headerRow + 6);

        for (var row = headerRow; row <= lastRow; row++)
        {
            var values = new List<string>();
            var keywordHits = 0;
            for (var column = summaryColumn + 1; column <= lastColumn; column++)
            {
                var cell = sheet.Cell(row, column);
                if (cell.IsEmpty() || cell.HasFormula)
                {
                    continue;
                }

                var text = cell.GetString().Trim();
                if (!LooksLikeCatalogValue(text))
                {
                    continue;
                }

                values.Add(text);
                if (ContainsDocumentKeyword(NormalizeKey(text)))
                {
                    keywordHits++;
                }
            }

            // Ưu tiên hàng có nhiều từ khoá giấy tờ thật (bỏ hàng dữ liệu toàn TRUE/FALSE).
            var score = keywordHits * 100 + values.Count;
            if (keywordHits > 0 && score > bestScore)
            {
                bestScore = score;
                best = values;
            }
        }

        return best;
    }

    private static bool LooksLikeCatalogValue(string text)
    {
        if (text.Length is < 2 or > 180)
        {
            return false;
        }

        if (text.StartsWith('=') || text.StartsWith('*') || text.StartsWith('#'))
        {
            return false;
        }

        // Không phải tên giấy tờ: giá trị boolean/thùng, ngày tháng, số, quyết định.
        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || text.Equals("FALSE", StringComparison.OrdinalIgnoreCase)
            || text.Equals("0", StringComparison.Ordinal)
            || text.Equals("1", StringComparison.Ordinal))
        {
            return false;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || DateTime.TryParse(text, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out _))
        {
            return false;
        }

        if (Regex.IsMatch(text, @"^\d+([./-]\d+)*$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (Regex.IsMatch(text, @"Q[DĐ]\b", RegexOptions.IgnoreCase)
            || text.Contains("/UBT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(text, @"^NĂM\s+\d{4}", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"^[IVXLC]+\.\s")
            || text.Contains("Trích yếu", StringComparison.CurrentCultureIgnoreCase)
            || text.Contains("Chọn", StringComparison.CurrentCultureIgnoreCase)
            || text.Contains("STT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static List<RawRow> ReadRawRows(
        IXLWorksheet sheet,
        int headerRow,
        int lastRow,
        int summaryColumn,
        int numberColumn,
        int documentNumberColumn,
        int sheetNumberColumn)
    {
        var rows = new List<RawRow>();
        string? currentGroup = null;
        string? currentType = null;
        int? currentYear = null;

        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var numberLabel = sheet.Cell(rowNumber, numberColumn).GetString().Trim();
            var documentNumber = sheet.Cell(rowNumber, documentNumberColumn).GetString().Trim();
            var sheetNumber = sheet.Cell(rowNumber, sheetNumberColumn).GetString().Trim();
            var summaryCell = sheet.Cell(rowNumber, summaryColumn);
            var text = summaryCell.GetString().Trim();
            var hasFormula = summaryCell.HasFormula;

            if (hasFormula && text.Length == 0)
            {
                text = "=" + summaryCell.FormulaA1;
            }

            if (numberLabel.Length == 0)
            {
                if (text.Length == 0)
                {
                    // Dòng chỉ còn dữ liệu ở cột hồ sơ số/số tờ vẫn được giữ
                    // để người dùng bổ sung trích yếu hoặc số còn thiếu.
                    if (documentNumber.Length == 0 && sheetNumber.Length == 0)
                    {
                        continue;
                    }
                }
                else
                {
                    var yearMatch = Regex.Match(text, @"^NĂM\s+(\d{4})", RegexOptions.IgnoreCase);
                    if (yearMatch.Success
                        && int.TryParse(yearMatch.Groups[1].Value, out var headerYear))
                    {
                        currentYear = headerYear;
                        continue;
                    }

                    if (Regex.IsMatch(text, @"^[IVXLC]+\.\s"))
                    {
                        currentGroup = text;
                        continue;
                    }

                    if (text.StartsWith('*'))
                    {
                        currentType = text.TrimStart('*', ' ').Trim();
                        continue;
                    }

                    if (LooksLikeColumnHeader(text))
                    {
                        continue;
                    }
                }
            }

            var year = ReadYear(sheet.Cell(rowNumber, 5)) ?? currentYear;
            if (year.HasValue)
            {
                currentYear = year;
            }

            rows.Add(new RawRow(
                rowNumber,
                year,
                numberLabel,
                documentNumber,
                sheetNumber,
                text,
                hasFormula,
                currentGroup,
                currentType));
        }

        return rows;
    }

    private static bool LooksLikeColumnHeader(string text)
    {
        var key = NormalizeKey(text);
        return key is "hop so"
            or "ho so so"
            or "so ho so"
            or "trich yeu noi dung"
            or "thoi gian tai lieu"
            or "so to"
            or "so trang"
            or "ghi chu"
            or "so ky hieu"
            or "stt";
    }

    private static int? ReadYear(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime().Year;
        }

        if (cell.DataType == XLDataType.Number)
        {
            var serial = cell.GetDouble();
            if (serial is > 1 and < 2_958_466)
            {
                return DateTime.FromOADate(serial).Year;
            }
        }

        var text = cell.GetString().Trim();
        if (text.Length >= 4
            && int.TryParse(text.AsSpan(0, 4), out var year)
            && year is >= 1900 and <= 2200)
        {
            return year;
        }

        if (DateTime.TryParse(
                text,
                CultureInfo.GetCultureInfo("vi-VN"),
                DateTimeStyles.None,
                out var date))
        {
            return date.Year;
        }

        return null;
    }

    private static List<string> MergeCatalog(
        IReadOnlyList<string> headerCatalog,
        IReadOnlyDictionary<string, int> observed,
        IReadOnlyList<string> defaults)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string value)
        {
            var trimmed = Regex.Replace(value.Trim(), @"\s+", " ");
            var key = NormalizeKey(trimmed);
            if (key.Length == 0 || !seen.Add(key))
            {
                return;
            }

            result.Add(trimmed);
        }

        foreach (var item in headerCatalog)
        {
            Add(item);
        }

        foreach (var item in observed
                     .OrderByDescending(pair => pair.Value)
                     .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            Add(item.Key);
        }

        foreach (var item in defaults)
        {
            Add(item);
        }

        return result.Take(80).ToList();
    }

    private static (string Base, List<string> Items, bool HasList) ParseSummary(
        string text,
        IReadOnlyList<string> catalog)
    {
        var trimmed = text.TrimEnd();
        var group = SplitTrailingGroup(trimmed);
        if (group is null)
        {
            return (trimmed, new List<string>(), false);
        }

        var items = SplitItems(group.Value.Inside);
        if (!LooksLikeDocumentList(items, catalog))
        {
            return (trimmed, new List<string>(), false);
        }

        return (trimmed[..group.Value.OpenIndex].TrimEnd(), items, true);
    }

    /// <summary>
    /// Bỏ danh mục giấy tờ ở cuối câu đã chuẩn hoá để lấy phần tiền tố. Cách này
    /// giữ đúng các trường hợp câu gốc kết thúc bằng "của" ngay trước danh mục.
    /// </summary>
    private static string NormalizeBaseSummary(string normalizedSummary, bool hasDocumentList)
    {
        if (!hasDocumentList)
        {
            return normalizedSummary;
        }

        var trimmed = normalizedSummary.TrimEnd();
        var group = SplitTrailingGroup(trimmed);
        return group is null
            ? normalizedSummary
            : trimmed[..group.Value.OpenIndex].TrimEnd();
    }

    private static (int OpenIndex, string Inside)? SplitTrailingGroup(string text)
    {
        if (!text.EndsWith(')'))
        {
            return null;
        }

        var depth = 0;
        var openIndex = -1;

        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (text[index] == ')')
            {
                depth++;
            }
            else if (text[index] == '(')
            {
                depth--;
                if (depth == 0)
                {
                    openIndex = index;
                    break;
                }
            }
        }

        if (openIndex < 0)
        {
            return null;
        }

        var inside = text[(openIndex + 1)..^1];
        if (inside.Contains('(') || inside.Contains(')'))
        {
            return null;
        }

        return (openIndex, inside);
    }

    private static List<string> SplitItems(string inside) =>
        inside
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => Regex.Replace(item, @"\s+", " "))
            .Where(item => item.Length > 0)
            .ToList();

    private static bool LooksLikeDocumentList(
        IReadOnlyList<string> items,
        IReadOnlyList<string> catalog)
    {
        if (items.Count == 0)
        {
            return false;
        }

        var keys = items.Select(NormalizeKey).ToHashSet(StringComparer.Ordinal);
        if (catalog.Any(item => keys.Contains(NormalizeKey(item))))
        {
            return true;
        }

        if (items.Any(ContainsDocumentKeyword))
        {
            return true;
        }

        return items.Count >= 2 && items.All(item => !LooksLikeProperName(item));
    }

    private static bool ContainsDocumentKeyword(string item)
    {
        var key = NormalizeKey(item);
        return DocumentKeywords.Any(keyword => key.Contains(keyword, StringComparison.Ordinal));
    }

    private static bool LooksLikeProperName(string item)
    {
        var words = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        return words.All(word => char.IsUpper(word[0]));
    }

    private static bool IsSummaryHeader(string text) =>
        NormalizeKey(text).Contains("trich yeu", StringComparison.Ordinal);

    private static bool LooksLikeBrokenSummary(string text) =>
        text.Contains("#REF!", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("=\"", StringComparison.Ordinal);

    private static string NormalizeKey(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private static string BuildSummary(
        SummaryRow row,
        IReadOnlyList<string> requestedItems,
        IReadOnlyList<string> catalog)
    {
        var items = NormalizeItems(requestedItems, catalog);

        if (items.Count == 0)
        {
            return row.HasDocumentList ? row.BaseSummary : row.OriginalSummary;
        }

        var baseText = row.BaseSummary.TrimEnd();
        var list = string.Join(", ", items);

        if (baseText.Length == 0)
        {
            return "(" + list + ")";
        }

        // Giữ đúng kiểu nối của dòng gốc: có dòng đặt " (" và có dòng đặt "(".
        var separator = " ";
        if (row.HasDocumentList)
        {
            var original = row.OriginalSummary.TrimEnd();
            var openIndex = original.LastIndexOf('(');
            if (openIndex > 0 && !char.IsWhiteSpace(original[openIndex - 1]))
            {
                separator = string.Empty;
            }
        }

        return baseText + separator + "(" + list + ")";
    }

    private static SummaryPlan CreatePlan(
        SummaryRow row,
        SummaryChange? change,
        IReadOnlyList<string> catalog)
    {
        string summary;
        if (change?.SummaryText is not null)
        {
            summary = change.SummaryText.Trim();
        }
        else if (change is not null
                 && change.Items.Count == 0
                 && (change.SheetNumber is not null || change.DocumentNumber is not null || change.NumberLabel is not null))
        {
            // Chỉ cập nhật số tờ: giữ nguyên trích yếu, tránh vô tình xoá danh mục giấy tờ.
            summary = row.OriginalSummary;
        }
        else if (change is not null)
        {
            summary = BuildSummary(row, change.Items, catalog);
        }
        else
        {
            summary = row.OriginalSummary;
        }

        var normalized = MarriageSummaryNormalizer.NeedsNormalization(summary);
        summary = MarriageSummaryNormalizer.NormalizeIfNeeded(summary);
        var summaryChanged = !string.Equals(
            summary,
            row.OriginalSummary,
            StringComparison.Ordinal);

        var sheetNumber = row.SheetNumber;
        var sheetNumberChanged = false;
        if (change?.SheetNumber is not null)
        {
            sheetNumber = change.SheetNumber.Trim();
            sheetNumberChanged = !string.Equals(
                sheetNumber,
                row.SheetNumber,
                StringComparison.Ordinal);
        }

        var documentNumber = row.DocumentNumber;
        var documentNumberChanged = false;
        if (change?.DocumentNumber is not null)
        {
            documentNumber = change.DocumentNumber.Trim();
            documentNumberChanged = !string.Equals(
                documentNumber,
                row.DocumentNumber,
                StringComparison.Ordinal);
        }

        var numberLabel = row.NumberLabel;
        var numberLabelChanged = false;
        if (change?.NumberLabel is not null)
        {
            numberLabel = change.NumberLabel.Trim();
            numberLabelChanged = !string.Equals(
                numberLabel,
                row.NumberLabel,
                StringComparison.Ordinal);
        }

        return new SummaryPlan(
            row,
            summary,
            sheetNumber,
            summaryChanged,
            sheetNumberChanged,
            normalized && summaryChanged,
            documentNumber,
            documentNumberChanged,
            numberLabel,
            numberLabelChanged);
    }

    private static SummaryPreviewRow ToPreview(SummaryPlan plan) =>
        new(
            plan.Row.RowNumber,
            plan.Summary,
            plan.SummaryChanged || plan.SheetNumberChanged || plan.DocumentNumberChanged || plan.NumberLabelChanged,
            plan.Row.OriginalSummary,
            plan.SheetNumber,
            plan.Row.SheetNumber,
            plan.SummaryChanged,
            plan.SheetNumberChanged,
            plan.DocumentNumber,
            plan.Row.DocumentNumber,
            plan.DocumentNumberChanged,
            plan.NumberLabel,
            plan.Row.NumberLabel,
            plan.NumberLabelChanged);

    private static List<string> NormalizeItems(
        IReadOnlyList<string> requestedItems,
        IReadOnlyList<string> catalog)
    {
        var catalogOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < catalog.Count; index++)
        {
            catalogOrder.TryAdd(NormalizeKey(catalog[index]), index);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<(string Value, int Index, int Order)>();

        foreach (var requested in requestedItems)
        {
            var value = Regex.Replace(requested.Trim(), @"\s+", " ");
            if (value.Length == 0)
            {
                continue;
            }

            var key = NormalizeKey(value);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            unique.Add((value, unique.Count, catalogOrder.GetValueOrDefault(key, int.MaxValue)));
        }

        return unique
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Value)
            .ToList();
    }

    private static List<SummaryRow> FilterRows(
        IReadOnlyList<SummaryRow> rows,
        SummaryScope scope)
    {
        IEnumerable<SummaryRow> query = rows;

        if (scope.Year.HasValue)
        {
            query = query.Where(row => row.Year == scope.Year.Value);
        }

        if (scope.YearFrom.HasValue)
        {
            query = query.Where(row => row.Year.HasValue && row.Year.Value >= scope.YearFrom.Value);
        }

        if (scope.YearTo.HasValue)
        {
            query = query.Where(row => row.Year.HasValue && row.Year.Value <= scope.YearTo.Value);
        }


        if (scope.OnlyMissingSheetNumber)
        {
            query = query.Where(row => string.IsNullOrWhiteSpace(row.SheetNumber));
        }

        if (scope.OnlyMissingDocumentNumber)
        {
            query = query.Where(row => string.IsNullOrWhiteSpace(row.DocumentNumber));
        }

        if (scope.OnlyMissingNumber)
        {
            query = query.Where(row => string.IsNullOrWhiteSpace(row.NumberLabel));
        }

        if (scope.OnlyNeedsNormalization)
        {
            query = query.Where(row => row.NeedsNormalization);
        }

        if (!string.IsNullOrWhiteSpace(scope.Type))
        {
            query = query.Where(row =>
                string.Equals(row.Type, scope.Type.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        if (scope.OnlyExisting)
        {
            query = query.Where(row => row.HasDocumentList);
        }

        if (!string.IsNullOrWhiteSpace(scope.Search))
        {
            var term = scope.Search.Trim();
            query = query.Where(row =>
                row.NumberLabel.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.DocumentNumber.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.SheetNumber.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.OriginalSummary.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.NormalizedSummary.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || row.Items.Any(item =>
                    item.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                || (row.Type?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || (row.Group?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        switch (scope.NumberMode)
        {
            case SummaryNumberMode.Range:
                if (scope.NumberFrom.HasValue)
                {
                    query = query.Where(row =>
                        row.Number.HasValue && row.Number.Value >= scope.NumberFrom.Value);
                }

                if (scope.NumberTo.HasValue)
                {
                    query = query.Where(row =>
                        row.Number.HasValue && row.Number.Value <= scope.NumberTo.Value);
                }

                break;

            case SummaryNumberMode.Single:
                if (scope.Number.HasValue)
                {
                    query = query.Where(row => row.Number == scope.Number.Value);
                }

                break;
        }

        var result = query.ToList();
        if (scope.MissingSheetNumberFirst)
        {
            result = result
                .OrderBy(row => string.IsNullOrWhiteSpace(row.SheetNumber) ? 0 : 1)
                .ThenBy(row => row.RowNumber)
                .ToList();
        }

        return result;
    }

    private static int? ParseNumberLabel(string label)
    {
        var match = Regex.Match(label, @"^\s*(\d{1,9})", RegexOptions.CultureInvariant);
        return match.Success
            && int.TryParse(
                match.Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number)
            ? number
            : null;
    }

    private static string ScopeSuffix(SummaryScope scope)
    {
        var parts = new List<string>();

        if (scope.Year.HasValue)
        {
            parts.Add(scope.Year.Value.ToString(CultureInfo.InvariantCulture));
        }

        switch (scope.NumberMode)
        {
            case SummaryNumberMode.Range:
                if (scope.NumberFrom.HasValue && scope.NumberTo.HasValue)
                {
                    parts.Add(
                        scope.NumberFrom.Value.ToString(CultureInfo.InvariantCulture)
                        + "-"
                        + scope.NumberTo.Value.ToString(CultureInfo.InvariantCulture));
                }
                else if (scope.NumberFrom.HasValue)
                {
                    parts.Add("tu" + scope.NumberFrom.Value.ToString(CultureInfo.InvariantCulture));
                }
                else if (scope.NumberTo.HasValue)
                {
                    parts.Add("den" + scope.NumberTo.Value.ToString(CultureInfo.InvariantCulture));
                }

                break;

            case SummaryNumberMode.Single:
                if (scope.Number.HasValue)
                {
                    parts.Add("so" + scope.Number.Value.ToString(CultureInfo.InvariantCulture));
                }

                break;
        }

        return parts.Count == 0 ? string.Empty : "_" + string.Join("_", parts);
    }

    private SummaryWorkbook RequireSession(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session)
            ? session
            : throw new KeyNotFoundException(
                "Phiên làm việc không tồn tại hoặc đã hết hạn. Hãy tải lại workbook.");

    private void CleanupExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - SessionLifetime;

        foreach (var pair in _sessions)
        {
            if (pair.Value.CreatedAt < cutoff)
            {
                DeleteSession(pair.Key);
            }
        }

        var uploads = Path.Combine(_storage.RootPath, "uploads");
        if (!Directory.Exists(uploads))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(uploads))
        {
            var marker = Path.Combine(directory, MarkerFileName);
            if (!File.Exists(marker))
            {
                continue;
            }

            try
            {
                if (File.GetLastWriteTimeUtc(marker) < cutoff.UtcDateTime)
                {
                    _storage.DeleteDirectory(directory);
                }
            }
            catch (IOException)
            {
                // Thư mục đang được dùng; lần dọn sau sẽ xử lý.
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Không chặn kết quả nếu file tạm chưa xoá được.
        }
    }

    private static string ContentTypeFor(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
            ".xltx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.template",
            ".xltm" => "application/vnd.ms-excel.template.macroEnabled.12",
            _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

    private sealed record RawRow(
        int RowNumber,
        int? Year,
        string NumberLabel,
        string DocumentNumber,
        string SheetNumber,
        string Text,
        bool HasFormula,
        string? Group,
        string? Type);

    public IReadOnlyList<SummaryYearReport> GetReport(Guid sessionId)
    {
        var session = RequireSession(sessionId);
        return session.Rows
            .Where(row => row.Year.HasValue)
            .GroupBy(row => row.Year!.Value)
            .OrderBy(group => group.Key)
            .Select(group => new SummaryYearReport(
                group.Key,
                group.Count(),
                group.Count(row => string.IsNullOrWhiteSpace(row.SheetNumber)),
                group.Count(row => string.IsNullOrWhiteSpace(row.DocumentNumber)),
                group.Count(row => string.IsNullOrWhiteSpace(row.NumberLabel)),
                group.Count(row => row.NeedsNormalization)))
            .ToList();
    }

    /// <summary>Chuẩn hoá tên file xuất do người dùng đặt: bỏ đường dẫn, giữ đuôi gốc, bỏ ký tự nguy hiểm.</summary>
    private static string SanitizeOutputFileName(string? requested, string fallback, string extension)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return fallback;
        }

        var name = Path.GetFileName(requested.Trim());
        name = Regex.Replace(name, @"[\\/:*?""<>|]", "_").Trim();
        if (name.Length == 0)
        {
            return fallback;
        }

        // Giữ đuôi gốc nếu người dùng quên nhập hoặc nhập đuôi khác.
        var ext = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(ext) || !ext.Equals(extension, StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name) + extension;
        }

        if (name.Length > 120)
        {
            name = name[..(120 - extension.Length)] + extension;
        }

        return name.Length == 0 ? fallback : name;
    }

    private sealed record SummaryPlan(
        SummaryRow Row,
        string Summary,
        string? SheetNumber,
        bool SummaryChanged,
        bool SheetNumberChanged,
        bool Normalized,
        string? DocumentNumber = null,
        bool DocumentNumberChanged = false,
        string? NumberLabel = null,
        bool NumberLabelChanged = false);
}
