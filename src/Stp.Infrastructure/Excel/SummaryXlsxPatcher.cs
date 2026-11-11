using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Stp.Infrastructure.Excel;

/// <summary>Một ô cần thay bằng chuỗi mới (cột trích yếu hoặc cột số tờ).</summary>
internal sealed record SummaryCellPatch(int RowNumber, string Column, string Text);

/// <summary>
/// Nhóm các ô cần thay theo sheet nguồn. Workbook nhiều sheet (file nhập +
/// sheet năm) thì xuất phải ghi về đúng sheet của từng dòng, các RowNumber
/// ở đây là số dòng Excel thật trong sheet đó.
/// </summary>
internal sealed record SummarySheetPatches(
    string SheetName,
    IReadOnlyList<SummaryCellPatch> Patches);

/// <summary>Một sheet giữ lại khi xuất bản lọc: chỉ giữ dòng tiêu đề + dòng được phép.</summary>
internal sealed record SummaryFilteredSheet(
    string SheetName,
    int HeaderRow,
    IReadOnlySet<int> AllowedRows,
    IReadOnlyList<SummaryCellPatch> Patches);

/// <summary>
/// Ghi lại workbook gốc bằng cách chỉ thay XML của đúng worksheet chứa trích yếu.
/// Mọi phần khác (sharedStrings, styles, drawings, comments, macro, web extension)
/// được sao chép nguyên nội dung sang file mới.
/// </summary>
internal static class SummaryXlsxPatcher
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace OfficeRelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string WorkbookPart = "xl/workbook.xml";
    private const string WorkbookRelsPart = "xl/_rels/workbook.xml.rels";
    private const string ContentTypesPart = "[Content_Types].xml";
    private const string RootRelsPart = "_rels/.rels";

    private const string EmptyRelationshipsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />";

    private static readonly Regex RowTagPattern =
        new(
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?row\b(?<attrs>[^>]*?)(?<self>/?)>",
            RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SharedStringCellPattern =
        new(
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?c\b(?=[^>]*\bt=""s"")(?=[^>]*[^/]>)[^>]*>.*?</(?(prefix)\k<prefix>)c>",
            RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SharedStringValuePattern =
        new(
            @"<(?:[A-Za-z_][A-Za-z0-9_.-]*:)?v\b[^>]*>\s*(?<index>\d+)\s*</(?:[A-Za-z_][A-Za-z0-9_.-]*:)?v>",
            RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SharedStringItemPattern =
        new(
            @"<(?:[A-Za-z_][A-Za-z0-9_.-]*:)?si\b[^>]*/>|<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?si\b[^>]*>.*?</(?(prefix)\k<prefix>)si>",
            RegexOptions.Singleline | RegexOptions.Compiled);

    public static void Apply(
        string sourcePath,
        string outputPath,
        string sheetName,
        IReadOnlyList<SummaryCellPatch> patches)
        => Apply(
            sourcePath,
            outputPath,
            new[] { new SummarySheetPatches(sheetName, patches) });

    /// <summary>
    /// Ghi lại workbook gốc bằng cách chỉ thay XML của các worksheet có ô bị
    /// sửa (một phiên nhiều sheet thì đè từng sheet của dòng tương ứng). Mọi
    /// phần khác (các sheet khác, sharedStrings, styles, drawings, comments,
    /// macro, web extension) được sao chép nguyên nội dung sang file mới — ô
    /// sửa dùng inline string nên không đụng bảng sharedStrings dùng chung.
    /// </summary>
    public static void Apply(
        string sourcePath,
        string outputPath,
        IReadOnlyList<SummarySheetPatches> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new ArgumentException("Không có sheet nào cần cập nhật.", nameof(sheets));
        }

        var totalPatches = sheets.Sum(sheet => sheet.Patches.Count);
        if (totalPatches == 0)
        {
            throw new ArgumentException("Không có ô nào cần cập nhật.", nameof(sheets));
        }

        using var source = ZipFile.OpenRead(sourcePath);

        var workbookEntry = source.GetEntry(WorkbookPart)
            ?? throw new InvalidDataException("Workbook thiếu phần xl/workbook.xml.");
        var workbookXml = ReadEntryText(workbookEntry);
        var updatedWorkbook = EnableFullCalculation(workbookXml);

        // Đọc và vá từng sheet; một sheet tên trùng nhau là lỗi workbook.
        var updatedParts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            if (sheet.Patches.Count == 0)
            {
                continue;
            }

            var worksheetPart = ResolveWorksheetPart(source, sheet.SheetName);
            if (updatedParts.ContainsKey(worksheetPart))
            {
                throw new InvalidDataException(
                    $"Hai sheet cùng trỏ một phần worksheet: '{sheet.SheetName}'.");
            }

            var worksheetEntry = source.GetEntry(worksheetPart)
                ?? throw new InvalidDataException($"Workbook thiếu phần {worksheetPart}.");
            var updatedWorksheet = PatchWorksheet(
                ReadEntryText(worksheetEntry),
                sheet.Patches);
            updatedParts[worksheetPart] = Encode(updatedWorksheet);
        }

        var workbookBytes = Encode(updatedWorkbook);

        using var output = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: false);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        foreach (var entry in source.Entries)
        {
            var target = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);

            if (entry.LastWriteTime.Year is >= 1980 and <= 2107)
            {
                target.LastWriteTime = entry.LastWriteTime;
            }

            target.ExternalAttributes = entry.ExternalAttributes;

            using var destination = target.Open();

            if (updatedParts.TryGetValue(entry.FullName, out var worksheetBytes))
            {
                destination.Write(worksheetBytes);
                continue;
            }

            if (string.Equals(entry.FullName, WorkbookPart, StringComparison.OrdinalIgnoreCase))
            {
                destination.Write(workbookBytes);
                continue;
            }

            using var input = entry.Open();
            input.CopyTo(destination, bufferSize: 81920);
        }
    }

    /// <summary>
    /// Tạo bản xuất nhẹ: chỉ giữ worksheet trích yếu và các dòng trong phạm vi,
    /// thay các ô đã sửa, dựng lại sharedStrings chỉ với chuỗi còn dùng và xoá
    /// mọi sheet/phần tử không còn được tham chiếu.
    /// </summary>
    public static void ApplyFiltered(
        string sourcePath,
        string outputPath,
        string sheetName,
        int headerRow,
        IReadOnlySet<int> allowedRows,
        IReadOnlyList<SummaryCellPatch> patches)
        => ApplyFiltered(
            sourcePath,
            outputPath,
            new[]
            {
                new SummaryFilteredSheet(sheetName, headerRow, allowedRows, patches)
            });

    /// <summary>
    /// Bản xuất nhẹ cho workbook nhiều sheet: mỗi sheet giữ lại dòng tiêu đề +
    /// các dòng được phép của sheet đó; sharedStrings được dựng lại từ bảng gộp
    /// của mọi sheet giữ lại (cùng một bảng dùng chung nên phải đánh lại chỉ số
    /// xuyên sheet). Mọi sheet/phần tử không còn tham chiếu bị xoá khỏi file.
    /// </summary>
    public static void ApplyFiltered(
        string sourcePath,
        string outputPath,
        IReadOnlyList<SummaryFilteredSheet> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new ArgumentException("Không có sheet nào để xuất.", nameof(sheets));
        }

        using var source = ZipFile.OpenRead(sourcePath);

        var workbookEntry = source.GetEntry(WorkbookPart)
            ?? throw new InvalidDataException("Workbook thiếu phần xl/workbook.xml.");
        var workbookRelsEntry = source.GetEntry(WorkbookRelsPart)
            ?? throw new InvalidDataException("Workbook thiếu phần quan hệ xl/_rels/workbook.xml.rels.");
        var contentTypesEntry = source.GetEntry(ContentTypesPart)
            ?? throw new InvalidDataException("Workbook thiếu phần [Content_Types].xml.");
        var rootRelsEntry = source.GetEntry(RootRelsPart)
            ?? throw new InvalidDataException("Workbook thiếu phần quan hệ _rels/.rels.");

        var workbookXml = ReadEntryText(workbookEntry);
        var workbookRelsXml = ReadEntryText(workbookRelsEntry);
        var contentTypesXml = ReadEntryText(contentTypesEntry);
        var rootRelsXml = ReadEntryText(rootRelsEntry);

        var sharedStringsPart = ResolveSharedStringsPart(source, workbookRelsXml);
        var sharedStringsXml = sharedStringsPart is null
            ? null
            : ReadEntryText(source.GetEntry(sharedStringsPart)!);

        var kept = new List<FilteredSheetResult>();
        var sharedOrder = new List<int>();
        var sharedSeen = new HashSet<int>();

        foreach (var sheet in sheets)
        {
            if (sheet.AllowedRows.Count == 0)
            {
                continue;
            }

            var worksheetPart = ResolveWorksheetPart(source, sheet.SheetName);
            if (kept.Any(item => string.Equals(
                    item.WorksheetPart,
                    worksheetPart,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"Hai sheet cùng trỏ một phần worksheet: '{sheet.SheetName}'.");
            }

            var worksheetEntry = source.GetEntry(worksheetPart)
                ?? throw new InvalidDataException($"Workbook thiếu phần {worksheetPart}.");
            var worksheetRelsPart = GetRelsPartName(worksheetPart);
            var worksheetRelsXml = source.GetEntry(worksheetRelsPart) is { } worksheetRelsEntry
                ? ReadEntryText(worksheetRelsEntry)
                : null;
            var annotations = ResolveWorksheetAnnotations(
                source,
                worksheetPart,
                worksheetRelsXml);

            var (filteredWorksheet, sharedRefs) = FilterWorksheet(
                ReadEntryText(worksheetEntry),
                sheet.HeaderRow,
                sheet.AllowedRows,
                sheet.Patches);

            var removedRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
            var updatedCommentsXml = annotations.CommentsPart is null
                ? null
                : FilterComments(
                    ReadEntryText(source.GetEntry(annotations.CommentsPart)!),
                    sheet.HeaderRow,
                    sheet.AllowedRows);

            if (annotations.CommentsPart is not null && updatedCommentsXml is null)
            {
                removedRelationshipIds.Add(annotations.CommentsRelId!);
            }

            var updatedVmlXml = annotations.VmlPart is null
                ? null
                : FilterVml(
                    ReadEntryText(source.GetEntry(annotations.VmlPart)!),
                    sheet.HeaderRow,
                    sheet.AllowedRows);

            if (annotations.VmlPart is not null && updatedVmlXml is null)
            {
                removedRelationshipIds.Add(annotations.VmlRelId!);
                filteredWorksheet = RemoveLegacyDrawing(filteredWorksheet);
            }

            var updatedWorksheetRelsXml = RewriteWorksheetRelationships(
                worksheetRelsXml,
                removedRelationshipIds);

            foreach (var reference in sharedRefs)
            {
                if (sharedSeen.Add(reference))
                {
                    sharedOrder.Add(reference);
                }
            }

            kept.Add(new FilteredSheetResult(
                sheet.SheetName,
                worksheetPart,
                filteredWorksheet,
                updatedWorksheetRelsXml,
                annotations.CommentsPart,
                updatedCommentsXml,
                annotations.VmlPart,
                updatedVmlXml));
        }

        if (kept.Count == 0)
        {
            throw new ArgumentException(
                "Phạm vi xuất không giữ lại dòng nào.",
                nameof(sheets));
        }

        // Đánh lại chỉ số sharedStrings trên một bảng dùng chung: tham chiếu
        // gộp theo thứ tự sheet, mọi sheet giữ lại phải trỏ đúng vị trí mới.
        var sharedMap = new Dictionary<int, int>(sharedOrder.Count);
        for (var index = 0; index < sharedOrder.Count; index++)
        {
            sharedMap[sharedOrder[index]] = index;
        }

        var keepSharedStrings = sharedOrder.Count > 0;
        if (keepSharedStrings && sharedStringsXml is null)
        {
            throw new InvalidDataException(
                "Worksheet còn ô dùng sharedStrings nhưng workbook thiếu phần sharedStrings.xml.");
        }

        var updatedParts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var sharedCount = 0;
        foreach (var item in kept)
        {
            var (reindexedWorksheet, rewritten) = ReindexSharedStrings(
                item.WorksheetXml,
                sharedMap);
            sharedCount += rewritten;
            updatedParts[item.WorksheetPart] = Encode(reindexedWorksheet);

            if (item.WorksheetRelsXml is not null)
            {
                updatedParts[GetRelsPartName(item.WorksheetPart)] = Encode(item.WorksheetRelsXml);
            }

            if (item.CommentsPart is not null && item.CommentsXml is not null)
            {
                updatedParts[item.CommentsPart] = Encode(item.CommentsXml);
            }

            if (item.VmlPart is not null && item.VmlXml is not null)
            {
                updatedParts[item.VmlPart] = Encode(item.VmlXml);
            }
        }

        if (keepSharedStrings)
        {
            updatedParts[sharedStringsPart!] = Encode(
                RebuildSharedStrings(sharedStringsXml!, sharedOrder, sharedCount));
        }

        var updatedWorkbook = RewriteWorkbook(
            workbookXml,
            kept.Select(item => item.SheetName).ToList());
        var updatedWorkbookRels = RewriteWorkbookRelationships(
            workbookRelsXml,
            kept.Select(item => item.WorksheetPart).ToList(),
            keepSharedStrings);
        var updatedRootRels = RewriteRootRelationships(rootRelsXml);

        updatedParts[WorkbookPart] = Encode(updatedWorkbook);
        updatedParts[WorkbookRelsPart] = Encode(updatedWorkbookRels);
        updatedParts[RootRelsPart] = Encode(updatedRootRels);

        var relsOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in kept)
        {
            relsOverrides[item.WorksheetPart] = item.WorksheetRelsXml ?? EmptyRelationshipsXml;
        }

        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ContentTypesPart
        };
        var walkedRels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Đi từ workbook trước để dùng quan hệ đã lược bỏ sheet; nếu đi từ
        // _rels/.rels trước, walker sẽ đọc lại quan hệ gốc và kéo theo mọi sheet.
        AddPart(source, parts, walkedRels, WorkbookPart, updatedWorkbookRels, relsOverrides);
        foreach (var item in kept)
        {
            AddPart(source, parts, walkedRels, item.WorksheetPart, null, relsOverrides);
        }

        AddPart(source, parts, walkedRels, string.Empty, updatedRootRels, relsOverrides);

        foreach (var item in kept)
        {
            if (item.WorksheetRelsXml is null)
            {
                parts.Remove(GetRelsPartName(item.WorksheetPart));
            }
        }

        var updatedContentTypes = RewriteContentTypes(contentTypesXml, parts);
        updatedParts[ContentTypesPart] = Encode(updatedContentTypes);

        using var output = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: false);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        foreach (var entry in source.Entries)
        {
            if (!parts.Contains(entry.FullName))
            {
                continue;
            }

            var target = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            if (entry.LastWriteTime.Year is >= 1980 and <= 2107)
            {
                target.LastWriteTime = entry.LastWriteTime;
            }

            target.ExternalAttributes = entry.ExternalAttributes;

            using var destination = target.Open();

            if (updatedParts.TryGetValue(entry.FullName, out var updatedBytes))
            {
                destination.Write(updatedBytes);
                continue;
            }

            using var input = entry.Open();
            input.CopyTo(destination, bufferSize: 81920);
        }
    }

    private static (string Xml, List<int> SharedRefs) FilterWorksheet(
        string worksheetXml,
        int headerRow,
        IReadOnlySet<int> allowedRows,
        IReadOnlyList<SummaryCellPatch> patches)
    {
        foreach (var patch in patches)
        {
            NormalizeColumn(patch.Column);
        }

        var updates = patches
            .GroupBy(patch => patch.RowNumber)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rowTags = FindRows(worksheetXml);
        if (rowTags.Count == 0)
        {
            return (worksheetXml, new List<int>());
        }

        var builder = new StringBuilder(worksheetXml.Length);
        var cursor = 0;

        foreach (var row in rowTags)
        {
            if (row.Start > cursor)
            {
                builder.Append(worksheetXml, cursor, row.Start - cursor);
            }

            var rowEnd = FindRowEnd(worksheetXml, row);
            if (row.RowNumber <= headerRow || allowedRows.Contains(row.RowNumber))
            {
                var segment = worksheetXml[row.Start..rowEnd];
                if (updates.TryGetValue(row.RowNumber, out var rowPatches))
                {
                    foreach (var patch in rowPatches.OrderByDescending(
                                 patch => ColumnIndex(patch.Column)))
                    {
                        segment = PatchRowSegment(
                            segment,
                            row.RowNumber,
                            NormalizeColumn(patch.Column),
                            row.Prefix,
                            patch.Text);
                    }
                }

                builder.Append(segment);
            }

            cursor = rowEnd;
        }

        if (cursor < worksheetXml.Length)
        {
            builder.Append(worksheetXml, cursor, worksheetXml.Length - cursor);
        }

        var filtered = UpdateDimension(builder.ToString(), headerRow, allowedRows);
        return (filtered, CollectSharedStringReferences(filtered));
    }

    private static string NormalizeColumn(string columnLetter)
    {
        var column = columnLetter.Trim().ToUpperInvariant();
        if (column.Length == 0 || column.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                $"Cột '{columnLetter}' không hợp lệ.",
                nameof(columnLetter));
        }

        return column;
    }

    private static int FindRowEnd(string worksheetXml, RowTag row)
    {
        if (row.SelfClosing)
        {
            return row.ContentStart;
        }

        var closeTag = $"</{row.Prefix}row>";
        var closeIndex = worksheetXml.IndexOf(
            closeTag,
            row.ContentStart,
            StringComparison.Ordinal);

        if (closeIndex < 0)
        {
            throw new InvalidDataException(
                $"Không đọc được thẻ đóng của dòng {row.RowNumber} trong worksheet.");
        }

        return closeIndex + closeTag.Length;
    }

    private static string PatchRowSegment(
        string segment,
        int rowNumber,
        string column,
        string prefix,
        string text)
    {
        var openingTagEnd = segment.IndexOf('>');
        if (openingTagEnd < 0)
        {
            throw new InvalidDataException($"Dòng {rowNumber} trong worksheet không hợp lệ.");
        }

        var openingTag = segment[..(openingTagEnd + 1)];
        if (openingTag.EndsWith("/>", StringComparison.Ordinal))
        {
            openingTag = openingTag[..^2] + ">";
            segment = openingTag + $"</{prefix}row>";
        }

        var cellPattern =
            $@"<{Regex.Escape(prefix)}c\b[^>]*?\br=""{Regex.Escape(column)}{rowNumber}""(?=[\s/>])[^>]*?(?:/>|>.*?</{Regex.Escape(prefix)}c>)";
        var cellMatch = Regex.Match(segment, cellPattern, RegexOptions.Singleline);

        if (cellMatch.Success)
        {
            var cellOpeningTagEnd = cellMatch.Value.IndexOf('>');
            var cellOpeningTag = cellOpeningTagEnd >= 0
                ? cellMatch.Value[..(cellOpeningTagEnd + 1)]
                : cellMatch.Value;
            var style = Regex.Match(cellOpeningTag, @"\bs=""([^""]*)""");
            var styleAttribute = style.Success ? $" s=\"{style.Groups[1].Value}\"" : string.Empty;
            var newCell = BuildCell(rowNumber, column, styleAttribute, prefix, text);
            return string.Concat(
                segment[..cellMatch.Index],
                newCell,
                segment[(cellMatch.Index + cellMatch.Length)..]);
        }

        var closeTag = $"</{prefix}row>";
        var rowClose = segment.LastIndexOf(closeTag, StringComparison.Ordinal);
        if (rowClose < 0)
        {
            throw new InvalidDataException(
                $"Không đọc được dòng {rowNumber} trong worksheet.");
        }

        var insertAt = rowClose;
        var targetIndex = ColumnIndex(column);
        foreach (Match candidate in Regex.Matches(
                     segment,
                     $@"<{Regex.Escape(prefix)}c\b[^>]*?\br=""(?<col>[A-Z]+)(?<row>\d+)""",
                     RegexOptions.Singleline))
        {
            if (!int.TryParse(candidate.Groups["row"].Value, out var candidateRow)
                || candidateRow != rowNumber)
            {
                continue;
            }

            if (ColumnIndex(candidate.Groups["col"].Value) > targetIndex)
            {
                insertAt = candidate.Index;
                break;
            }
        }

        var insertedCell = BuildCell(rowNumber, column, null, prefix, text);
        return string.Concat(
            segment[..insertAt],
            insertedCell,
            segment[insertAt..]);
    }

    private static string UpdateDimension(
        string worksheetXml,
        int headerRow,
        IReadOnlySet<int> allowedRows)
    {
        var maxRow = allowedRows.Count == 0 ? headerRow : Math.Max(headerRow, allowedRows.Max());
        var match = Regex.Match(
            worksheetXml,
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?dimension\b[^>]*\bref=""(?<ref>[^""]+)""",
            RegexOptions.Singleline);

        if (!match.Success)
        {
            return worksheetXml;
        }

        var current = match.Groups["ref"].Value;
        var colon = current.IndexOf(':');
        var lastColumn = colon >= 0
            ? Regex.Match(current[(colon + 1)..], @"^[A-Z]+").Value
            : "A";

        if (lastColumn.Length == 0)
        {
            lastColumn = "A";
        }

        var reference = $"A1:{lastColumn}{maxRow}";
        var valueIndex = match.Groups["ref"].Index;
        return string.Concat(
            worksheetXml[..valueIndex],
            reference,
            worksheetXml[(valueIndex + match.Groups["ref"].Length)..]);
    }

    private static List<int> CollectSharedStringReferences(string worksheetXml)
    {
        var result = new List<int>();

        foreach (Match cell in SharedStringCellPattern.Matches(worksheetXml))
        {
            var value = SharedStringValuePattern.Match(cell.Value);
            if (value.Success
                && int.TryParse(
                    value.Groups["index"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                result.Add(index);
            }
        }

        return result;
    }

    private static (string Xml, int Count) ReindexSharedStrings(
        string worksheetXml,
        IReadOnlyDictionary<int, int> map)
    {
        if (map.Count == 0)
        {
            return (worksheetXml, 0);
        }

        var builder = new StringBuilder(worksheetXml.Length);
        var cursor = 0;
        var count = 0;

        foreach (Match cell in SharedStringCellPattern.Matches(worksheetXml))
        {
            var value = SharedStringValuePattern.Match(cell.Value);
            if (!value.Success
                || !int.TryParse(
                    value.Groups["index"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var oldIndex)
                || !map.TryGetValue(oldIndex, out var newIndex))
            {
                continue;
            }

            var valueIndex = cell.Index + value.Groups["index"].Index;
            builder.Append(worksheetXml, cursor, valueIndex - cursor);
            builder.Append(newIndex.ToString(CultureInfo.InvariantCulture));
            cursor = valueIndex + value.Groups["index"].Length;
            count++;
        }

        builder.Append(worksheetXml, cursor, worksheetXml.Length - cursor);
        return (builder.ToString(), count);
    }

    private static string RebuildSharedStrings(
        string sharedStringsXml,
        IReadOnlyList<int> order,
        int count)
    {
        var rootMatch = Regex.Match(
            sharedStringsXml,
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?sst\b[^>]*>",
            RegexOptions.Singleline);

        if (!rootMatch.Success)
        {
            throw new InvalidDataException("Phần sharedStrings.xml không hợp lệ.");
        }

        var items = SharedStringItemPattern
            .Matches(sharedStringsXml)
            .Cast<Match>()
            .Select(match => match.Value)
            .ToList();

        var prefix = rootMatch.Groups["prefix"].Value;
        var rootTag = rootMatch.Value;
        rootTag = Regex.Replace(rootTag, @"\s+count\s*=\s*""[^""]*""", string.Empty);
        rootTag = Regex.Replace(rootTag, @"\s+uniqueCount\s*=\s*""[^""]*""", string.Empty);

        if (rootTag.EndsWith("/>", StringComparison.Ordinal))
        {
            rootTag = rootTag[..^2] + ">";
        }

        var insertAt = rootTag.Length - 1;
        var newRootTag = string.Concat(
            rootTag[..insertAt],
            " count=\"",
            count.ToString(CultureInfo.InvariantCulture),
            "\" uniqueCount=\"",
            order.Count.ToString(CultureInfo.InvariantCulture),
            "\"",
            rootTag[insertAt..]);

        var builder = new StringBuilder(sharedStringsXml.Length);
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append(newRootTag);

        foreach (var index in order)
        {
            if (index < 0 || index >= items.Count)
            {
                throw new InvalidDataException(
                    $"Chỉ số sharedStrings {index} không tồn tại trong workbook.");
            }

            builder.Append(items[index]);
        }

        builder.Append("</").Append(prefix).Append("sst>");
        return builder.ToString();
    }

    /// <summary>
    /// Giữ lại đúng các sheet được chỉ định (bỏ các sheet khác, definedNames
    /// trỏ tới sheet bị bỏ, external references) và bật tính lại toàn bộ.
    /// </summary>
    private static string RewriteWorkbook(string workbookXml, IReadOnlyCollection<string> keepSheetNames)
    {
        var document = XDocument.Parse(workbookXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidDataException("xl/workbook.xml không có phần tử gốc.");
        var sheets = root.Element(MainNs + "sheets")
            ?? throw new InvalidDataException("xl/workbook.xml thiếu phần sheets.");
        var sheetElements = sheets.Elements(MainNs + "sheet").ToList();
        var keepSet = new HashSet<string>(keepSheetNames, StringComparer.CurrentCultureIgnoreCase);

        var targets = new List<XElement>();
        foreach (var name in keepSheetNames)
        {
            var target = sheetElements.FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("name"),
                name,
                StringComparison.CurrentCultureIgnoreCase));

            if (target is null)
            {
                throw new InvalidDataException($"Không tìm thấy sheet '{name}' trong workbook.");
            }

            targets.Add(target);
        }

        var removedNames = sheetElements
            .Where(element => !targets.Contains(element))
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        foreach (var sheet in sheetElements.Where(element => !targets.Contains(element)))
        {
            sheet.Remove();
        }

        foreach (var target in targets)
        {
            target.SetAttributeValue("state", null);
        }

        var definedNames = root.Element(MainNs + "definedNames");
        if (definedNames is not null)
        {
            foreach (var definedName in definedNames.Elements(MainNs + "definedName").ToList())
            {
                if (removedNames.Any(name => ReferencesSheet(definedName.Value, name)))
                {
                    definedName.Remove();
                }
            }

            if (!definedNames.Elements().Any())
            {
                definedNames.Remove();
            }
        }

        root.Element(MainNs + "externalReferences")?.Remove();
        root.Element(MainNs + "pivotCaches")?.Remove();
        root.Element(MainNs + "connections")?.Remove();

        var workbookView = root
            .Element(MainNs + "bookViews")?
            .Element(MainNs + "workbookView");
        workbookView?.SetAttributeValue("activeTab", "0");

        var calcProperties = root.Element(MainNs + "calcPr");
        if (calcProperties is null)
        {
            calcProperties = new XElement(MainNs + "calcPr");
            sheets.AddAfterSelf(calcProperties);
        }

        calcProperties.SetAttributeValue("fullCalcOnLoad", "1");
        calcProperties.SetAttributeValue("forceFullCalc", "1");

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool ReferencesSheet(string formula, string sheetName)
    {
        if (formula.Contains("'" + sheetName + "'!", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(
            formula,
            $@"(^|[^A-Za-z0-9_]){Regex.Escape(sheetName)}!",
            RegexOptions.IgnoreCase);
    }

    private static string RewriteWorkbookRelationships(
        string relationshipsXml,
        IReadOnlyCollection<string> worksheetParts,
        bool keepSharedStrings)
    {
        var document = XDocument.Parse(relationshipsXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidDataException("xl/_rels/workbook.xml.rels không có phần tử gốc.");
        var keepParts = new HashSet<string>(worksheetParts, StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in root.Elements(PackageRelNs + "Relationship").ToList())
        {
            var type = (string?)relationship.Attribute("Type") ?? string.Empty;
            var target = (string?)relationship.Attribute("Target") ?? string.Empty;
            var mode = (string?)relationship.Attribute("TargetMode");

            if (string.Equals(mode, "External", StringComparison.OrdinalIgnoreCase))
            {
                relationship.Remove();
                continue;
            }

            if (type.EndsWith("/worksheet", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ResolvePartPath(WorkbookPart, target);
                if (!keepParts.Contains(resolved))
                {
                    relationship.Remove();
                }

                continue;
            }

            if (IsRemovedSheetType(type)
                || type.EndsWith("/calcChain", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("/externalLink", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("/externalLinkPath", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("/connections", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("/pivotCacheDefinition", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("/pivotCacheRecords", StringComparison.OrdinalIgnoreCase)
                || type.Contains("/webExtension", StringComparison.OrdinalIgnoreCase)
                || (type.EndsWith("/sharedStrings", StringComparison.OrdinalIgnoreCase)
                    && !keepSharedStrings))
            {
                relationship.Remove();
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool IsRemovedSheetType(string type) =>
        type.EndsWith("/chartsheet", StringComparison.OrdinalIgnoreCase)
        || type.EndsWith("/dialogsheet", StringComparison.OrdinalIgnoreCase)
        || type.EndsWith("/macrosheet", StringComparison.OrdinalIgnoreCase)
        || type.EndsWith("/intlMacrosheet", StringComparison.OrdinalIgnoreCase);

    private static string RewriteRootRelationships(string relationshipsXml)
    {
        var document = XDocument.Parse(relationshipsXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidDataException("_rels/.rels không có phần tử gốc.");

        foreach (var relationship in root.Elements(PackageRelNs + "Relationship").ToList())
        {
            var type = (string?)relationship.Attribute("Type") ?? string.Empty;
            var keep = type.EndsWith("/officeDocument", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("/core-properties", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("/extended-properties", StringComparison.OrdinalIgnoreCase);

            if (!keep)
            {
                relationship.Remove();
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string RewriteContentTypes(
        string contentTypesXml,
        IReadOnlySet<string> parts)
    {
        var document = XDocument.Parse(contentTypesXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidDataException("[Content_Types].xml không có phần tử gốc.");
        var contentTypesNs = XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/content-types");

        foreach (var overrideElement in root.Elements(contentTypesNs + "Override").ToList())
        {
            var partName = ((string?)overrideElement.Attribute("PartName") ?? string.Empty)
                .TrimStart('/');
            if (!parts.Contains(partName))
            {
                overrideElement.Remove();
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static WorksheetAnnotations ResolveWorksheetAnnotations(
        ZipArchive archive,
        string worksheetPart,
        string? worksheetRelsXml)
    {
        if (worksheetRelsXml is null)
        {
            return new WorksheetAnnotations(null, null, null, null);
        }

        var document = XDocument.Parse(worksheetRelsXml, LoadOptions.PreserveWhitespace);
        var relationships = document.Root?
            .Elements(PackageRelNs + "Relationship")
            .ToList() ?? new List<XElement>();

        string? commentsPart = null;
        string? commentsRelId = null;
        string? vmlPart = null;
        string? vmlRelId = null;

        foreach (var relationship in relationships)
        {
            var type = (string?)relationship.Attribute("Type") ?? string.Empty;
            var target = (string?)relationship.Attribute("Target");
            var id = (string?)relationship.Attribute("Id");
            var mode = (string?)relationship.Attribute("TargetMode");

            if (string.IsNullOrWhiteSpace(target)
                || string.IsNullOrWhiteSpace(id)
                || string.Equals(mode, "External", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (type.EndsWith("/comments", StringComparison.OrdinalIgnoreCase))
            {
                var part = ResolvePartPath(worksheetPart, target);
                if (archive.GetEntry(part) is not null)
                {
                    commentsPart = part;
                    commentsRelId = id;
                }
            }
            else if (type.EndsWith("/vmlDrawing", StringComparison.OrdinalIgnoreCase))
            {
                var part = ResolvePartPath(worksheetPart, target);
                if (archive.GetEntry(part) is not null)
                {
                    vmlPart = part;
                    vmlRelId = id;
                }
            }
        }

        return new WorksheetAnnotations(commentsPart, commentsRelId, vmlPart, vmlRelId);
    }

    private static string? FilterComments(
        string commentsXml,
        int headerRow,
        IReadOnlySet<int> allowedRows)
    {
        var document = XDocument.Parse(commentsXml, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            return commentsXml;
        }

        var comments = root
            .Descendants()
            .Where(element => element.Name.LocalName == "comment")
            .ToList();
        var removed = 0;

        foreach (var comment in comments)
        {
            var row = ParseCellRow((string?)comment.Attribute("ref"));
            if (row <= headerRow || allowedRows.Contains(row))
            {
                continue;
            }

            comment.Remove();
            removed++;
        }

        if (removed == 0)
        {
            return commentsXml;
        }

        return root.Descendants().Any(element => element.Name.LocalName == "comment")
            ? document.ToString(SaveOptions.DisableFormatting)
            : null;
    }

    private static string? FilterVml(
        string vmlXml,
        int headerRow,
        IReadOnlySet<int> allowedRows)
    {
        var document = XDocument.Parse(vmlXml, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            return vmlXml;
        }

        var shapes = root
            .Descendants()
            .Where(element => element.Name.LocalName == "shape")
            .ToList();
        var removed = 0;

        foreach (var shape in shapes)
        {
            var clientData = shape
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "ClientData");
            if (clientData is null
                || !string.Equals(
                    (string?)clientData.Attribute("ObjectType"),
                    "Note",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rowElement = clientData
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Row");
            if (rowElement is null
                || !int.TryParse(
                    rowElement.Value.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var zeroBasedRow))
            {
                continue;
            }

            var row = zeroBasedRow + 1;
            if (row <= headerRow || allowedRows.Contains(row))
            {
                continue;
            }

            shape.Remove();
            removed++;
        }

        if (removed == 0)
        {
            return vmlXml;
        }

        return root.Descendants().Any(element => element.Name.LocalName == "shape")
            ? document.ToString(SaveOptions.DisableFormatting)
            : null;
    }

    private static string RemoveLegacyDrawing(string worksheetXml)
    {
        var result = Regex.Replace(
            worksheetXml,
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?legacyDrawing\b[^>]*?/>",
            string.Empty,
            RegexOptions.Singleline);

        return Regex.Replace(
            result,
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?legacyDrawing\b[^>]*>.*?</(?(prefix)\k<prefix>)legacyDrawing>",
            string.Empty,
            RegexOptions.Singleline);
    }

    private static string? RewriteWorksheetRelationships(
        string? relationshipsXml,
        IReadOnlySet<string> removedRelationshipIds)
    {
        if (relationshipsXml is null || removedRelationshipIds.Count == 0)
        {
            return relationshipsXml;
        }

        var document = XDocument.Parse(relationshipsXml, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            return relationshipsXml;
        }

        foreach (var relationship in root.Elements(PackageRelNs + "Relationship").ToList())
        {
            var id = (string?)relationship.Attribute("Id");
            if (id is not null && removedRelationshipIds.Contains(id))
            {
                relationship.Remove();
            }
        }

        return root.Elements(PackageRelNs + "Relationship").Any()
            ? document.ToString(SaveOptions.DisableFormatting)
            : null;
    }

    private static int ParseCellRow(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return 0;
        }

        var match = Regex.Match(
            reference,
            @"^[A-Za-z]+(?<row>\d+)$",
            RegexOptions.CultureInvariant);
        return match.Success
            && int.TryParse(
                match.Groups["row"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var row)
            ? row
            : 0;
    }

    private static void AddPart(
        ZipArchive archive,
        HashSet<string> parts,
        HashSet<string> walkedRels,
        string partName,
        string? relsXmlOverride,
        IReadOnlyDictionary<string, string>? relsOverrides = null)
    {
        parts.Add(partName);

        var relsName = GetRelsPartName(partName);
        if (!walkedRels.Add(relsName))
        {
            return;
        }

        var relsEntry = archive.GetEntry(relsName);
        var relsXml = relsXmlOverride;
        if (relsXml is null
            && relsOverrides is not null
            && relsOverrides.TryGetValue(partName, out var overridden))
        {
            relsXml = overridden;
        }

        if (relsXml is null)
        {
            if (relsEntry is null)
            {
                return;
            }

            relsXml = ReadEntryText(relsEntry);
        }

        parts.Add(relsName);
        var document = XDocument.Parse(relsXml, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            return;
        }

        foreach (var relationship in root.Elements(PackageRelNs + "Relationship"))
        {
            var mode = (string?)relationship.Attribute("TargetMode");
            if (string.Equals(mode, "External", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = (string?)relationship.Attribute("Target");
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var targetPart = ResolvePartPath(partName, target);
            if (archive.GetEntry(targetPart) is not null)
            {
                AddPart(archive, parts, walkedRels, targetPart, null, relsOverrides);
            }
        }
    }

    private static string? ResolveSharedStringsPart(
        ZipArchive archive,
        string workbookRelsXml)
    {
        var document = XDocument.Parse(workbookRelsXml, LoadOptions.PreserveWhitespace);
        var relationship = document.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(element => ((string?)element.Attribute("Type") ?? string.Empty)
                .EndsWith("/sharedStrings", StringComparison.OrdinalIgnoreCase));

        var target = (string?)relationship?.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var partName = ResolvePartPath(WorkbookPart, target);
        return archive.GetEntry(partName) is null ? null : partName;
    }

    private static byte[] Encode(string text) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    private static string ResolveWorksheetPart(ZipArchive archive, string sheetName)
    {
        var workbookEntry = archive.GetEntry(WorkbookPart)
            ?? throw new InvalidDataException("Workbook thiếu phần xl/workbook.xml.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("Workbook thiếu phần quan hệ xl/_rels/workbook.xml.rels.");

        XDocument workbook;
        XDocument relationships;

        using (var stream = workbookEntry.Open())
        {
            workbook = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        using (var stream = relsEntry.Open())
        {
            relationships = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        var sheet = workbook.Root?
            .Element(MainNs + "sheets")?
            .Elements(MainNs + "sheet")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("name"),
                sheetName,
                StringComparison.CurrentCultureIgnoreCase));

        if (sheet is null)
        {
            throw new InvalidDataException($"Không tìm thấy sheet '{sheetName}' trong workbook.");
        }

        var relationshipId = (string?)sheet.Attribute(OfficeRelNs + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            throw new InvalidDataException($"Sheet '{sheetName}' thiếu quan hệ r:id.");
        }

        var relationship = relationships.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("Id"),
                relationshipId,
                StringComparison.Ordinal));

        var target = (string?)relationship?.Attribute("Target");
        var mode = (string?)relationship?.Attribute("TargetMode");
        if (string.IsNullOrWhiteSpace(target)
            || string.Equals(mode, "External", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Không xác định được phần XML của sheet '{sheetName}'.");
        }

        return ResolvePartPath(WorkbookPart, target);
    }

    private static string ResolvePartPath(string sourcePart, string target)
    {
        var normalizedTarget = target.Replace('\\', '/');
        string path;

        if (normalizedTarget.StartsWith('/'))
        {
            path = normalizedTarget[1..];
        }
        else
        {
            var directory = GetPartDirectory(sourcePart);
            path = directory.Length == 0
                ? normalizedTarget
                : directory + "/" + normalizedTarget;
        }

        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string GetPartDirectory(string partName)
    {
        var index = partName.LastIndexOf('/');
        return index < 0 ? string.Empty : partName[..index];
    }

    private static string GetRelsPartName(string partName)
    {
        if (partName.Length == 0)
        {
            return "_rels/.rels";
        }

        var directory = GetPartDirectory(partName);
        var fileName = directory.Length == 0
            ? partName
            : partName[(directory.Length + 1)..];

        return (directory.Length == 0 ? "_rels/" : directory + "/_rels/")
            + fileName
            + ".rels";
    }

    private static string EnableFullCalculation(string workbookXml)
    {
        var document = XDocument.Parse(workbookXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidDataException("xl/workbook.xml không có phần tử gốc.");

        var calcProperties = root.Element(MainNs + "calcPr");
        if (calcProperties is null)
        {
            calcProperties = new XElement(MainNs + "calcPr");
            var sheets = root.Element(MainNs + "sheets");
            if (sheets is not null)
            {
                sheets.AddAfterSelf(calcProperties);
            }
            else
            {
                root.Add(calcProperties);
            }
        }

        calcProperties.SetAttributeValue("fullCalcOnLoad", "1");
        calcProperties.SetAttributeValue("forceFullCalc", "1");

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string PatchWorksheet(
        string worksheetXml,
        IReadOnlyList<SummaryCellPatch> patches)
    {
        if (patches.Count == 0)
        {
            return worksheetXml;
        }

        foreach (var patch in patches)
        {
            NormalizeColumn(patch.Column);
        }

        var byRow = patches
            .GroupBy(patch => patch.RowNumber)
            .OrderByDescending(group => group.Key)
            .ToList();

        var rowTags = FindRows(worksheetXml);
        var prefix = rowTags.Count > 0
            ? rowTags[0].Prefix
            : DetectElementPrefix(worksheetXml);
        var rowMap = rowTags.ToDictionary(tag => tag.RowNumber);

        // Sửa từ dòng cuối lên để vị trí các dòng phía trước không bị lệch.
        foreach (var group in byRow)
        {
            var rowNumber = group.Key;

            if (rowMap.TryGetValue(rowNumber, out var row))
            {
                var rowEnd = FindRowEnd(worksheetXml, row);
                var segment = worksheetXml[row.Start..rowEnd];

                foreach (var patch in group.OrderByDescending(
                             patch => ColumnIndex(patch.Column)))
                {
                    segment = PatchRowSegment(
                        segment,
                        rowNumber,
                        NormalizeColumn(patch.Column),
                        row.Prefix,
                        patch.Text);
                }

                worksheetXml = string.Concat(
                    worksheetXml[..row.Start],
                    segment,
                    worksheetXml[rowEnd..]);
                continue;
            }

            var cells = string.Concat(group
                .OrderBy(patch => ColumnIndex(patch.Column))
                .Select(patch => BuildCell(
                    rowNumber,
                    NormalizeColumn(patch.Column),
                    null,
                    prefix,
                    patch.Text)));
            worksheetXml = InsertRow(
                worksheetXml,
                FindRows(worksheetXml),
                rowNumber,
                cells,
                prefix);
            rowTags = FindRows(worksheetXml);
            rowMap = rowTags.ToDictionary(tag => tag.RowNumber);
        }

        return worksheetXml;
    }

    private static string InsertRow(
        string worksheetXml,
        IReadOnlyList<RowTag> rowTags,
        int rowNumber,
        string cellXml,
        string prefix)
    {
        var cellRow = $"<{prefix}row r=\"{rowNumber}\">{cellXml}</{prefix}row>";
        var sheetData = worksheetXml.IndexOf($"<{prefix}sheetData", StringComparison.Ordinal);
        if (sheetData < 0)
        {
            throw new InvalidDataException("Worksheet không có phần sheetData.");
        }

        var sheetDataTagEnd = worksheetXml.IndexOf('>', sheetData);
        if (sheetDataTagEnd < 0)
        {
            throw new InvalidDataException("Worksheet có sheetData không hợp lệ.");
        }

        if (worksheetXml[sheetDataTagEnd - 1] == '/')
        {
            return string.Concat(
                worksheetXml[..sheetData],
                $"<{prefix}sheetData>",
                cellRow,
                $"</{prefix}sheetData>",
                worksheetXml[(sheetDataTagEnd + 1)..]);
        }

        var nextRow = rowTags.FirstOrDefault(tag => tag.RowNumber > rowNumber);
        if (nextRow is not null)
        {
            return string.Concat(
                worksheetXml[..nextRow.Start],
                cellRow,
                worksheetXml[nextRow.Start..]);
        }

        var sheetDataEnd = worksheetXml.IndexOf(
            $"</{prefix}sheetData>",
            sheetDataTagEnd,
            StringComparison.Ordinal);
        if (sheetDataEnd < 0)
        {
            throw new InvalidDataException("Worksheet thiếu thẻ đóng sheetData.");
        }

        return string.Concat(
            worksheetXml[..sheetDataEnd],
            cellRow,
            worksheetXml[sheetDataEnd..]);
    }

    private static List<RowTag> FindRows(string worksheetXml)
    {
        return RowTagPattern
            .Matches(worksheetXml)
            .Cast<Match>()
            .Select(match => new RowTag(
                ParseRowNumber(match.Groups["attrs"].Value),
                match.Index,
                match.Index + match.Length,
                match.Value,
                match.Groups["prefix"].Value,
                match.Groups["self"].Value == "/"))
            .Where(tag => tag.RowNumber > 0)
            .OrderBy(tag => tag.Start)
            .ToList();
    }

    private static string DetectElementPrefix(string worksheetXml)
    {
        var match = Regex.Match(
            worksheetXml,
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?worksheet\b",
            RegexOptions.None);
        return match.Success ? match.Groups["prefix"].Value : string.Empty;
    }

    private static string BuildCell(
        int rowNumber,
        string column,
        string? styleAttribute,
        string prefix,
        string text)
    {
        if (text.Length > 32767)
        {
            throw new InvalidDataException(
                $"Nội dung trích yếu dòng {rowNumber} vượt quá 32.767 ký tự của Excel.");
        }

        var escaped = EscapeXml(text);
        return $"<{prefix}c r=\"{column}{rowNumber}\"{styleAttribute} t=\"inlineStr\"><{prefix}is><{prefix}t xml:space=\"preserve\">{escaped}</{prefix}t></{prefix}is></{prefix}c>";
    }

    private static int ParseRowNumber(string attributes)
    {
        var match = Regex.Match(attributes, @"\br=""(\d+)""");
        return match.Success && int.TryParse(
            match.Groups[1].Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private static int ColumnIndex(string column)
    {
        var index = 0;
        foreach (var character in column.ToUpperInvariant())
        {
            if (character is < 'A' or > 'Z')
            {
                return int.MaxValue;
            }

            index = (index * 26) + (character - 'A' + 1);
        }

        return index;
    }

    private static string EscapeXml(string text)
    {
        var builder = new StringBuilder(text.Length + 16);

        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '\t':
                case '\n':
                case '\r':
                    builder.Append(character);
                    break;
                default:
                    if (!char.IsControl(character))
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 81920,
            leaveOpen: false);
        return reader.ReadToEnd();
    }

    private sealed record WorksheetAnnotations(
        string? CommentsPart,
        string? CommentsRelId,
        string? VmlPart,
        string? VmlRelId);

    /// <summary>Kết quả lọc một sheet khi xuất bản lọc (chưa đánh lại chỉ số chung).</summary>
    private sealed record FilteredSheetResult(
        string SheetName,
        string WorksheetPart,
        string WorksheetXml,
        string? WorksheetRelsXml,
        string? CommentsPart,
        string? CommentsXml,
        string? VmlPart,
        string? VmlXml);

    private sealed record RowTag(
        int RowNumber,
        int Start,
        int ContentStart,
        string Tag,
        string Prefix,
        bool SelfClosing);
}
