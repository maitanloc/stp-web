using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Stp.Infrastructure.Excel;

/// <summary>Một ô trích yếu cần thay bằng chuỗi mới.</summary>
internal sealed record SummaryCellPatch(int RowNumber, string Text);

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

    private static readonly Regex RowTagPattern =
        new(
            @"<(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*:)?row\b(?<attrs>[^>]*?)(?<self>/?)>",
            RegexOptions.Singleline | RegexOptions.Compiled);

    public static void Apply(
        string sourcePath,
        string outputPath,
        string sheetName,
        string columnLetter,
        IReadOnlyList<SummaryCellPatch> patches)
    {
        if (patches.Count == 0)
        {
            throw new ArgumentException("Không có ô nào cần cập nhật.", nameof(patches));
        }

        using var source = ZipFile.OpenRead(sourcePath);

        var worksheetPart = ResolveWorksheetPart(source, sheetName);
        var workbookEntry = source.GetEntry(WorkbookPart)
            ?? throw new InvalidDataException("Workbook thiếu phần xl/workbook.xml.");
        var worksheetEntry = source.GetEntry(worksheetPart)
            ?? throw new InvalidDataException($"Workbook thiếu phần {worksheetPart}.");

        var workbookXml = ReadEntryText(workbookEntry);
        var worksheetXml = ReadEntryText(worksheetEntry);

        var updatedWorkbook = EnableFullCalculation(workbookXml);
        var updatedWorksheet = PatchWorksheet(worksheetXml, columnLetter, patches);

        var workbookBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(updatedWorkbook);
        var worksheetBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(updatedWorksheet);

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

            if (string.Equals(entry.FullName, worksheetPart, StringComparison.OrdinalIgnoreCase))
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

        return NormalizePartPath(target);
    }

    private static string NormalizePartPath(string target)
    {
        var path = target.Replace('\\', '/');
        if (path.StartsWith('/'))
        {
            path = path[1..];
        }
        else if (!path.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            path = "xl/" + path;
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
        string columnLetter,
        IReadOnlyList<SummaryCellPatch> patches)
    {
        var column = columnLetter.Trim().ToUpperInvariant();
        if (column.Length == 0 || column.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException($"Cột trích yếu '{columnLetter}' không hợp lệ.", nameof(columnLetter));
        }

        var updates = patches
            .GroupBy(patch => patch.RowNumber)
            .ToDictionary(group => group.Key, group => group.Last().Text);

        var rowTags = FindRows(worksheetXml);
        var prefix = rowTags.Count > 0
            ? rowTags[0].Prefix
            : DetectElementPrefix(worksheetXml);

        // Sửa từ dòng cuối lên để các vị trí phía trước không bị lệch.
        foreach (var rowNumber in updates.Keys.OrderByDescending(value => value))
        {
            var text = updates[rowNumber];
            var row = rowTags.FirstOrDefault(tag => tag.RowNumber == rowNumber);

            if (row is not null)
            {
                worksheetXml = PatchCellInRow(worksheetXml, rowTags, row, column, prefix, text);
                continue;
            }

            worksheetXml = InsertRow(
                worksheetXml,
                rowTags,
                rowNumber,
                BuildCell(rowNumber, column, null, prefix, text),
                prefix);
            rowTags = FindRows(worksheetXml);
        }

        return worksheetXml;
    }

    private static string PatchCellInRow(
        string worksheetXml,
        IReadOnlyList<RowTag> rowTags,
        RowTag row,
        string column,
        string prefix,
        string text)
    {
        if (row.SelfClosing)
        {
            var openTag = row.Tag[..^2] + ">";
            var cell = BuildCell(row.RowNumber, column, null, prefix, text);
            return string.Concat(
                worksheetXml[..row.Start],
                openTag,
                cell,
                $"</{prefix}row>",
                worksheetXml[row.ContentStart..]);
        }

        var rowEnd = rowTags
            .Where(tag => tag.Start > row.Start)
            .Select(tag => tag.Start)
            .DefaultIfEmpty(worksheetXml.Length)
            .First();
        rowEnd = Math.Min(rowEnd, worksheetXml.Length);

        var segment = worksheetXml[row.ContentStart..rowEnd];
        var cellPattern =
            $@"<{Regex.Escape(prefix)}c\b[^>]*?\br=""{Regex.Escape(column)}{row.RowNumber}""(?=[\s/>])[^>]*?(?:/>|>.*?</{Regex.Escape(prefix)}c>)";
        var cellMatch = Regex.Match(segment, cellPattern, RegexOptions.Singleline);

        if (cellMatch.Success)
        {
            var openingTagEnd = cellMatch.Value.IndexOf('>');
            var openingTag = openingTagEnd >= 0
                ? cellMatch.Value[..(openingTagEnd + 1)]
                : cellMatch.Value;
            var style = Regex.Match(openingTag, @"\bs=""([^""]*)""");
            var styleAttribute = style.Success ? $" s=\"{style.Groups[1].Value}\"" : string.Empty;
            var newCell = BuildCell(row.RowNumber, column, styleAttribute, prefix, text);

            var absoluteStart = row.ContentStart + cellMatch.Index;
            return string.Concat(
                worksheetXml[..absoluteStart],
                newCell,
                worksheetXml[(absoluteStart + cellMatch.Length)..]);
        }

        // Ô chưa tồn tại: chèn đúng thứ tự cột trong dòng.
        var rowClose = segment.LastIndexOf($"</{prefix}row>", StringComparison.Ordinal);
        if (rowClose < 0)
        {
            throw new InvalidDataException(
                $"Không đọc được dòng {row.RowNumber} trong worksheet.");
        }

        var insertAt = rowClose;
        var targetIndex = ColumnIndex(column);
        foreach (Match candidate in Regex.Matches(
                     segment,
                     $@"<{Regex.Escape(prefix)}c\b[^>]*?\br=""(?<col>[A-Z]+)(?<row>\d+)""",
                     RegexOptions.Singleline))
        {
            if (!int.TryParse(candidate.Groups["row"].Value, out var candidateRow)
                || candidateRow != row.RowNumber)
            {
                continue;
            }

            if (ColumnIndex(candidate.Groups["col"].Value) > targetIndex)
            {
                insertAt = candidate.Index;
                break;
            }
        }

        var insertedCell = BuildCell(row.RowNumber, column, null, prefix, text);
        return string.Concat(
            worksheetXml[..(row.ContentStart + insertAt)],
            insertedCell,
            worksheetXml[(row.ContentStart + insertAt)..]);
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

    private sealed record RowTag(
        int RowNumber,
        int Start,
        int ContentStart,
        string Tag,
        string Prefix,
        bool SelfClosing);
}
