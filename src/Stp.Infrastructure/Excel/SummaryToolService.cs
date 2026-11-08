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

    private static readonly string[] DefaultCatalog =
    {
        "phiếu đề xuất",
        "giấy biên nhận",
        "đơn xin đăng ký kết hôn với người nước ngoài",
        "đơn xin chứng nhận độc thân",
        "giấy chứng nhận đăng ký nhân khẩu",
        "CMND",
        "bộ công hàm",
        "chứng chỉ chuyên khoa",
        "sơ yếu lí lịch",
        "giấy khai sinh",
        "đơn xin xác nhận đính hôn",
        "hộ chiếu",
        "khai sinh",
        "Đơn xin xác nhận tình trạng hôn nhân",
        "giấy chứng nhận không có trở ngại trong việc kết hôn",
        "phiếu khám sức khỏe",
        "thị thực",
        "phiếu xuất nhập cảnh",
        "giấy xác nhận độc thân"
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

    public SummaryWorkbook? GetSession(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

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
        IReadOnlyList<SummaryChange> changes)
    {
        var session = RequireSession(sessionId);
        var byRow = session.Rows.ToDictionary(row => row.RowNumber);
        var result = new List<SummaryPreviewRow>(changes.Count);

        foreach (var change in changes)
        {
            if (!byRow.TryGetValue(change.RowNumber, out var row))
            {
                throw new ArgumentException($"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }

            var summary = BuildSummary(row, change.Items, session.Catalog);
            result.Add(new SummaryPreviewRow(
                row.RowNumber,
                summary,
                !string.Equals(summary, row.OriginalSummary, StringComparison.Ordinal)));
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
        var result = new List<SummaryPreviewRow>();
        var ignored = 0;

        foreach (var change in changes)
        {
            if (!byRow.TryGetValue(change.RowNumber, out var row))
            {
                throw new ArgumentException(
                    $"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }

            if (!allowed.Contains(change.RowNumber))
            {
                ignored++;
                continue;
            }

            var summary = BuildSummary(row, change.Items, session.Catalog);
            if (!string.Equals(summary, row.OriginalSummary, StringComparison.Ordinal))
            {
                result.Add(new SummaryPreviewRow(row.RowNumber, summary, true));
            }
        }

        return new SummaryFilteredPreview(
            scopedRows.Count,
            result.Count,
            ignored,
            result);
    }

    public async Task<SummaryExportResult> ExportAsync(
        Guid sessionId,
        IReadOnlyList<SummaryChange> changes,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession(sessionId);
        if (changes.Count == 0)
        {
            throw new InvalidOperationException("Chưa có thay đổi nào để xuất.");
        }

        var byRow = session.Rows.ToDictionary(row => row.RowNumber);
        var patches = new List<SummaryCellPatch>(changes.Count);

        foreach (var change in changes)
        {
            if (!byRow.TryGetValue(change.RowNumber, out var row))
            {
                throw new ArgumentException($"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }

            var summary = BuildSummary(row, change.Items, session.Catalog);
            if (!string.Equals(summary, row.OriginalSummary, StringComparison.Ordinal))
            {
                patches.Add(new SummaryCellPatch(row.RowNumber, summary));
            }
        }

        if (patches.Count == 0)
        {
            throw new InvalidOperationException("Nội dung đã chọn trùng với trích yếu hiện có, không cần xuất file.");
        }

        var outputDirectory = _storage.GetJobOutputDirectory(sessionId);
        Directory.CreateDirectory(outputDirectory);

        var extension = Path.GetExtension(session.FileName);
        var outputName = Path.GetFileNameWithoutExtension(session.FileName) + "_trichyeu" + extension;
        var outputPath = _storage.GetUniquePath(outputDirectory, outputName);

        try
        {
            SummaryXlsxPatcher.Apply(
                session.FilePath,
                outputPath,
                session.SheetName,
                session.SummaryColumn,
                patches);

            var content = await File
                .ReadAllBytesAsync(outputPath, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Xuất trích yếu {FileName}: {Count} dòng, {Bytes} byte.",
                session.FileName,
                patches.Count,
                content.Length);

            return new SummaryExportResult(
                content,
                Path.GetFileName(outputPath),
                ContentTypeFor(extension),
                patches.Count);
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
        CancellationToken cancellationToken = default)
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
        var patches = new List<SummaryCellPatch>(changes.Count);
        var ignored = 0;

        foreach (var change in changes)
        {
            if (!byRow.TryGetValue(change.RowNumber, out var row))
            {
                throw new ArgumentException(
                    $"Không tìm thấy dòng {change.RowNumber} trong sheet {session.SheetName}.");
            }

            if (!allowed.Contains(change.RowNumber))
            {
                ignored++;
                continue;
            }

            var summary = BuildSummary(row, change.Items, session.Catalog);
            if (!string.Equals(summary, row.OriginalSummary, StringComparison.Ordinal))
            {
                patches.Add(new SummaryCellPatch(row.RowNumber, summary));
            }
        }

        var outputDirectory = _storage.GetJobOutputDirectory(sessionId);
        Directory.CreateDirectory(outputDirectory);

        var extension = Path.GetExtension(session.FileName);
        var outputName = Path.GetFileNameWithoutExtension(session.FileName)
            + "_loc"
            + ScopeSuffix(scope)
            + extension;
        var outputPath = _storage.GetUniquePath(outputDirectory, outputName);

        try
        {
            SummaryXlsxPatcher.ApplyFiltered(
                session.FilePath,
                outputPath,
                session.SheetName,
                session.SummaryColumn,
                session.HeaderRow,
                allowed,
                patches);

            var content = await File
                .ReadAllBytesAsync(outputPath, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Xuất bản lọc {FileName}: {Rows} hồ sơ, {Count} dòng sửa, {Ignored} thay đổi ngoài phạm vi, {Bytes} byte.",
                session.FileName,
                scopedRows.Count,
                patches.Count,
                ignored,
                content.Length);

            return new SummaryFilteredExportResult(
                content,
                Path.GetFileName(outputPath),
                ContentTypeFor(extension),
                patches.Count,
                scopedRows.Count,
                ignored);
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
        var headerCatalog = ExtractHeaderCatalog(sheet, headerRow, summaryColumn, lastColumn);
        var rawRows = ReadRawRows(sheet, headerRow, lastRow, summaryColumn, numberColumn);

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

            return new SummaryRow
            {
                RowNumber = raw.RowNumber,
                Year = raw.Year,
                Number = ParseNumberLabel(raw.NumberLabel),
                NumberLabel = raw.NumberLabel,
                OriginalSummary = raw.Text,
                BaseSummary = baseSummary,
                Items = items,
                HasDocumentList = hasDocumentList,
                HasFormula = raw.HasFormula,
                IsBrokenSummary = isBroken,
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
            FormulaSummaryCount = rows.Count(row => row.HasFormula)
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

    private static List<string> ExtractHeaderCatalog(
        IXLWorksheet sheet,
        int headerRow,
        int summaryColumn,
        int lastColumn)
    {
        var best = new List<string>();
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? headerRow, headerRow + 6);

        for (var row = headerRow; row <= lastRow; row++)
        {
            var values = new List<string>();
            for (var column = summaryColumn + 1; column <= lastColumn; column++)
            {
                var cell = sheet.Cell(row, column);
                if (cell.IsEmpty() || cell.HasFormula)
                {
                    continue;
                }

                var text = cell.GetString().Trim();
                if (LooksLikeCatalogValue(text))
                {
                    values.Add(text);
                }
            }

            if (values.Count > best.Count)
            {
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
        int numberColumn)
    {
        var rows = new List<RawRow>();
        string? currentGroup = null;
        string? currentType = null;
        int? currentYear = null;

        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var numberLabel = sheet.Cell(rowNumber, numberColumn).GetString().Trim();
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
                    continue;
                }

                var yearMatch = Regex.Match(text, @"^NĂM\s+(\d{4})", RegexOptions.IgnoreCase);
                if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var headerYear))
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

                continue;
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
                text,
                hasFormula,
                currentGroup,
                currentType));
        }

        return rows;
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
                || row.OriginalSummary.Contains(term, StringComparison.CurrentCultureIgnoreCase)
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

        return query.ToList();
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
        string Text,
        bool HasFormula,
        string? Group,
        string? Type);
}
