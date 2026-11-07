using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Stp.Core.Domain;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Đọc cấu trúc ML và các sheet in của workbook. Logic được port từ bản Razor Pages
/// đã kiểm chứng trên DK_KET_HON.xlsx (22.567 hồ sơ, 36 năm).
/// </summary>
internal static class ExcelArchiveParser
{
    private static readonly Regex MlReferenceRegex = new(@"ML!\s*\$?B\$?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearMarkerRegex = new(@"^NĂM\s+(\d{4})\s*$", RegexOptions.Compiled);
    private static readonly Regex GroupMarkerRegex = new(@"^[IVXLC]+\.\s+", RegexOptions.Compiled);
    private static readonly Regex YearInSheetNameRegex = new(@"^(\d{4})", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex AnchorRangeRegex = new(@"^\s*(\d+)\s*-\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex LastNumberRegex = new(@"(\d+)\s*$", RegexOptions.Compiled);

    public static IXLWorksheet? FindMlSheet(XLWorkbook workbook)
    {
        var exact = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, "ML", StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return workbook.Worksheets.FirstOrDefault(w =>
            CellText(w.Cell("A1")).Equals("Hộp số", StringComparison.OrdinalIgnoreCase)
            && CellText(w.Cell("D1")).Contains("Trích yếu nội dung", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPrintSheet(IXLWorksheet worksheet)
    {
        var marker = CellText(worksheet.Cell("K1"));
        if (marker.Equals("BANG_IN_HANG_LOAT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var title = CellText(worksheet.Cell("D8"));
        var subTitle = CellText(worksheet.Cell("D9"));
        var subTitleFormula = worksheet.Cell("D9").HasFormula ? worksheet.Cell("D9").FormulaA1 : string.Empty;

        return title.StartsWith("MỤC LỤC VĂN BẢN", StringComparison.OrdinalIgnoreCase)
               && (subTitle.Contains("Hồ sơ số", StringComparison.OrdinalIgnoreCase)
                   || subTitleFormula.Contains("Hồ sơ số", StringComparison.OrdinalIgnoreCase));
    }

    public static int ParseYear(string sheetName)
    {
        var match = YearInSheetNameRegex.Match(sheetName);
        return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : 0;
    }

    public static bool HasVbaProject(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            return archive.Entries.Any(e =>
                string.Equals(e.FullName, "xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static ArchiveSnapshot ReadSnapshot(XLWorkbook workbook, string fileName, WorkbookKind kind)
    {
        var mlSheet = FindMlSheet(workbook);
        var printScan = ScanPrintSheets(workbook);

        List<ArchiveYearData> years;
        if (mlSheet is not null)
        {
            years = ParseMlSheet(mlSheet, printScan);
        }
        else
        {
            years = ParsePrintSheetsOnly(workbook, printScan);
        }

        return new ArchiveSnapshot
        {
            SourceFileName = Path.GetFileName(fileName),
            ImportedAt = DateTimeOffset.UtcNow,
            Kind = kind,
            Years = years.OrderBy(y => y.Year).ToList()
        };
    }

    public static PrintSheetScan ScanPrintSheets(XLWorkbook workbook)
    {
        var scan = new PrintSheetScan();

        foreach (var worksheet in workbook.Worksheets)
        {
            if (!IsPrintSheet(worksheet))
            {
                continue;
            }

            var year = ParseYear(worksheet.Name);
            if (year > 0)
            {
                if (!scan.VolumesByYear.TryGetValue(year, out var volumes))
                {
                    volumes = new List<string>();
                    scan.VolumesByYear[year] = volumes;
                }

                if (!volumes.Contains(worksheet.Name, StringComparer.OrdinalIgnoreCase))
                {
                    volumes.Add(worksheet.Name);
                }
            }

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow < 11)
            {
                continue;
            }

            var blockCount = (lastRow - 8) / 9 + 1;
            for (var k = 0; k < blockCount; k++)
            {
                var blockStart = 8 + (9 * k);
                var numberCell = worksheet.Cell(blockStart + 1, 4);
                var formula = numberCell.HasFormula ? numberCell.FormulaA1 : string.Empty;
                var mlMatch = MlReferenceRegex.Match(formula);
                if (!mlMatch.Success || !int.TryParse(mlMatch.Groups[1].Value, out var mlRow) || mlRow <= 0)
                {
                    continue;
                }

                if (scan.BlocksByMlRow.ContainsKey(mlRow))
                {
                    continue;
                }

                var dataRow = blockStart + 3;
                scan.BlocksByMlRow[mlRow] = new PrintBlockInfo
                {
                    MlRow = mlRow,
                    VolumeName = worksheet.Name,
                    Title = CellText(worksheet.Cell(blockStart, 4)),
                    Stt = ReadInt(worksheet.Cell(dataRow, 1)),
                    SoKyHieu = CellText(worksheet.Cell(dataRow, 2)),
                    Ngay = CellText(worksheet.Cell(dataRow, 3)),
                    TrichYeu = CellText(worksheet.Cell(dataRow, 4)),
                    TacGia = CellText(worksheet.Cell(dataRow, 5)),
                    ToSo = CellText(worksheet.Cell(dataRow, 6)),
                    GhiChu = CellText(worksheet.Cell(dataRow, 7))
                };
            }
        }

        return scan;
    }

    private static List<ArchiveYearData> ParseMlSheet(IXLWorksheet mlSheet, PrintSheetScan printScan)
    {
        var lastRow = mlSheet.LastRowUsed()?.RowNumber() ?? 0;
        var years = new Dictionary<int, ArchiveYearData>();
        var currentGroup = string.Empty;
        var currentYear = 0;
        var currentType = string.Empty;
        var lastNumber = 0;

        for (var row = 2; row <= lastRow; row++)
        {
            var summary = CellText(mlSheet.Cell(row, 4));
            if (summary.Length == 0)
            {
                continue;
            }

            if (GroupMarkerRegex.IsMatch(summary))
            {
                currentGroup = summary;
                currentYear = 0;
                currentType = string.Empty;
                lastNumber = 0;
                continue;
            }

            var yearMatch = YearMarkerRegex.Match(summary);
            if (yearMatch.Success)
            {
                currentYear = int.Parse(yearMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                currentType = string.Empty;
                lastNumber = 0;
                continue;
            }

            if (summary.StartsWith("*", StringComparison.Ordinal))
            {
                currentType = DossierTypes.Normalize(summary.TrimStart('*'));
                lastNumber = 0;
                continue;
            }

            if (currentYear <= 0)
            {
                continue;
            }

            var type = currentType.Length > 0 ? currentType : "Chưa phân loại";
            var numberLabel = CellText(mlSheet.Cell(row, 2));
            var number = ParseDossierNumber(numberLabel);
            var numberNote = string.Empty;

            if (number <= 0)
            {
                if (lastNumber > 0)
                {
                    number = lastNumber + 1;
                    numberNote = string.IsNullOrWhiteSpace(numberLabel)
                        ? $"Số hồ sơ gốc để trống; đã dùng số {number} theo thứ tự để lọc và sắp xếp."
                        : $"Nhãn gốc là “{numberLabel}” và không có phần số; tạm gán số {number} để lọc/sắp xếp (nhãn in giữ nguyên).";
                }
                else
                {
                    numberNote = $"Không đọc được số hồ sơ từ nhãn “{numberLabel}”.";
                }
            }

            if (string.IsNullOrWhiteSpace(numberLabel))
            {
                if (number > 0)
                {
                    numberLabel = number.ToString(CultureInfo.InvariantCulture);
                    numberNote = $"Số hồ sơ gốc để trống; đã dùng số {number} theo thứ tự để lọc và sắp xếp.";
                }
            }

            if (!years.TryGetValue(currentYear, out var yearData))
            {
                yearData = new ArchiveYearData
                {
                    Year = currentYear,
                    SheetName = currentYear.ToString(CultureInfo.InvariantCulture),
                    SourceSheets = printScan.VolumesByYear.TryGetValue(currentYear, out var volumes)
                        ? volumes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList()
                        : new List<string>()
                };
                years[currentYear] = yearData;
            }

            printScan.BlocksByMlRow.TryGetValue(row, out var printBlock);

            var soKyHieu = CellText(mlSheet.Cell(row, 6));
            var ngay = CellText(mlSheet.Cell(row, 5));
            var trichYeu = summary;
            var toSo = CellText(mlSheet.Cell(row, 7));
            var subNumber = CellText(mlSheet.Cell(row, 3));
            var boxNumber = CellText(mlSheet.Cell(row, 1));
            var originalSummary = CellText(mlSheet.Cell(row, 9));

            if (printBlock is not null)
            {
                if (string.IsNullOrWhiteSpace(soKyHieu)) soKyHieu = printBlock.SoKyHieu;
                if (string.IsNullOrWhiteSpace(ngay)) ngay = printBlock.Ngay;
                if (string.IsNullOrWhiteSpace(trichYeu)) trichYeu = printBlock.TrichYeu;
                if (string.IsNullOrWhiteSpace(toSo)) toSo = printBlock.ToSo;
            }

            var tacGia = printBlock?.TacGia ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tacGia))
            {
                tacGia = "Ủy ban nhân dân tỉnh Đồng Nai";
            }

            var ghiChu = printBlock?.GhiChu ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ghiChu))
            {
                ghiChu = CellText(mlSheet.Cell(row, 8));
            }

            var stt = printBlock is { Stt: > 0 } ? printBlock.Stt : 1;
            var title = printBlock is not null && !string.IsNullOrWhiteSpace(printBlock.Title)
                ? printBlock.Title
                : "MỤC LỤC VĂN BẢN";

            var isAggregate = subNumber.StartsWith("HS", StringComparison.OrdinalIgnoreCase)
                              && string.IsNullOrWhiteSpace(soKyHieu);

            yearData.Dossiers.Add(new DossierRecord
            {
                Number = number,
                NumberLabel = numberLabel,
                Title = title,
                Group = string.IsNullOrWhiteSpace(currentGroup) ? "Chưa phân nhóm" : currentGroup,
                Type = type,
                BoxNumber = boxNumber,
                SubNumber = subNumber,
                OriginalSummary = originalSummary,
                NumberNote = numberNote,
                SourceVolume = printBlock?.VolumeName ?? string.Empty,
                HasPrintLayout = printBlock is not null,
                IsAggregate = isAggregate,
                TotalPages = ExtractTotalPages(toSo),
                MlRow = row,
                Documents =
                {
                    new DocumentLine
                    {
                        Stt = stt,
                        SoKyHieu = soKyHieu,
                        Ngay = ngay,
                        TrichYeu = trichYeu,
                        TacGia = tacGia,
                        ToSo = toSo,
                        GhiChu = ghiChu
                    }
                }
            });

            if (number > 0)
            {
                lastNumber = number;
            }
        }

        return years.Values.OrderBy(y => y.Year).ToList();
    }

    private static List<ArchiveYearData> ParsePrintSheetsOnly(XLWorkbook workbook, PrintSheetScan printScan)
    {
        var years = new Dictionary<int, ArchiveYearData>();

        foreach (var worksheet in workbook.Worksheets)
        {
            if (!IsPrintSheet(worksheet))
            {
                continue;
            }

            var year = ParseYear(worksheet.Name);
            if (year <= 0)
            {
                continue;
            }

            if (!years.TryGetValue(year, out var yearData))
            {
                yearData = new ArchiveYearData
                {
                    Year = year,
                    SheetName = year.ToString(CultureInfo.InvariantCulture)
                };
                years[year] = yearData;
            }

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow < 11)
            {
                continue;
            }

            var blockCount = (lastRow - 8) / 9 + 1;
            for (var k = 0; k < blockCount; k++)
            {
                var blockStart = 8 + (9 * k);
                var numberLabel = ExtractNumberLabel(CellText(worksheet.Cell(blockStart + 1, 4)));
                var number = ParseDossierNumber(numberLabel);
                if (number <= 0)
                {
                    continue;
                }

                var dataRow = blockStart + 3;
                var toSo = CellText(worksheet.Cell(dataRow, 6));
                var tacGia = CellText(worksheet.Cell(dataRow, 5));
                if (string.IsNullOrWhiteSpace(tacGia))
                {
                    tacGia = "Ủy ban nhân dân tỉnh Đồng Nai";
                }

                yearData.Dossiers.Add(new DossierRecord
                {
                    Number = number,
                    NumberLabel = numberLabel,
                    Title = CellText(worksheet.Cell(blockStart, 4)),
                    Group = "Chưa phân nhóm",
                    Type = "Chưa phân loại",
                    HasPrintLayout = true,
                    SourceVolume = worksheet.Name,
                    TotalPages = ExtractTotalPages(toSo),
                    MlRow = k + 1,
                    Documents =
                    {
                        new DocumentLine
                        {
                            Stt = ReadInt(worksheet.Cell(dataRow, 1)),
                            SoKyHieu = CellText(worksheet.Cell(dataRow, 2)),
                            Ngay = CellText(worksheet.Cell(dataRow, 3)),
                            TrichYeu = CellText(worksheet.Cell(dataRow, 4)),
                            TacGia = tacGia,
                            ToSo = toSo,
                            GhiChu = CellText(worksheet.Cell(dataRow, 7))
                        }
                    }
                });
            }
        }

        foreach (var pair in printScan.VolumesByYear)
        {
            if (years.TryGetValue(pair.Key, out var yearData))
            {
                yearData.SourceSheets.AddRange(pair.Value);
            }
        }

        return years.Values.OrderBy(y => y.Year).ToList();
    }

    public static string CellText(IXLCell cell)
    {
        try
        {
            if (cell.IsEmpty())
            {
                return string.Empty;
            }

            if (cell.DataType == XLDataType.DateTime)
            {
                return cell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }

            if (cell.DataType == XLDataType.Number)
            {
                var number = cell.GetDouble();
                var format = cell.Style.DateFormat.Format ?? string.Empty;
                if (LooksLikeDateFormat(format) && number > 0 && number < 2958466)
                {
                    return DateTime.FromOADate(number).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                }

                return Math.Abs(number - Math.Truncate(number)) < 0.0000001
                    ? ((long)number).ToString(CultureInfo.InvariantCulture)
                    : number.ToString("0.##", CultureInfo.InvariantCulture);
            }

            return cell.GetString().Trim();
        }
        catch
        {
            try
            {
                return cell.GetString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static bool LooksLikeDateFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        var cleaned = Regex.Replace(format, @"""[^""]*""|\[[^\]]*\]", string.Empty);
        return cleaned.IndexOfAny(new[] { 'd', 'm', 'y', 'D', 'M', 'Y' }) >= 0;
    }

    private static int ReadInt(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return 0;
        }

        if (cell.DataType == XLDataType.Number)
        {
            return (int)Math.Round(cell.GetDouble(), MidpointRounding.AwayFromZero);
        }

        var text = CellText(cell);
        var match = NumberRegex.Match(text);
        return match.Success && int.TryParse(match.Value, out var value) ? value : 0;
    }

    private static string ExtractNumberLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var separator = text.LastIndexOf(':');
        return (separator >= 0 ? text[(separator + 1)..] : text).Trim();
    }

    private static int ParseDossierNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = NumberRegex.Match(text);
        return match.Success
               && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static int ExtractTotalPages(string toSo)
    {
        if (string.IsNullOrWhiteSpace(toSo))
        {
            return 0;
        }

        var anchored = AnchorRangeRegex.Match(toSo);
        if (anchored.Success && int.TryParse(anchored.Groups[2].Value, out var total))
        {
            return total;
        }

        var last = LastNumberRegex.Match(toSo);
        return last.Success && int.TryParse(last.Groups[1].Value, out var value) ? value : 0;
    }
}

internal sealed class PrintSheetScan
{
    public Dictionary<int, PrintBlockInfo> BlocksByMlRow { get; } = new();

    public Dictionary<int, List<string>> VolumesByYear { get; } = new();
}

internal sealed class PrintBlockInfo
{
    public int MlRow { get; init; }

    public string VolumeName { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public int Stt { get; init; }

    public string SoKyHieu { get; init; } = string.Empty;

    public string Ngay { get; init; } = string.Empty;

    public string TrichYeu { get; init; } = string.Empty;

    public string TacGia { get; init; } = string.Empty;

    public string ToSo { get; init; } = string.Empty;

    public string GhiChu { get; init; } = string.Empty;
}
