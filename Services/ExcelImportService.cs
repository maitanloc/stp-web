using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using StpWeb.Models;

namespace StpWeb.Services;

/// <summary>
/// Đọc toàn bộ hồ sơ từ sheet ML và dùng các sheet in năm để bổ sung
/// thông tin trình bày (tác giả, ghi chú, tiêu đề block).
///
/// Cấu trúc ML:
/// - Dòng "I. Văn phòng" là nhóm lưu trữ.
/// - Dòng "NĂM 1990" ... "NĂM 2025" là năm.
/// - Dòng "* Đăng ký kết hôn" là loại hồ sơ.
/// - Các dòng còn lại là hồ sơ: B = hồ sơ số, C = STT/hồ sơ phụ,
///   D = trích yếu, E = ngày, F = số ký hiệu, G = tờ số, I = trích yếu gốc.
///
/// Các sheet in năm (K1 = BANG_IN_HANG_LOAT) được quét để lấy block in
/// tương ứng với từng dòng ML. Nhờ vậy hồ sơ có bản in Excel vẫn giữ đúng
/// tác giả/ghi chú của bản in gốc.
/// </summary>
public sealed class ExcelImportService
{
    private static readonly Regex MlReferenceRegex = new(@"ML!\s*\$?B\$?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearMarkerRegex = new(@"^NĂM\s+(\d{4})\s*$", RegexOptions.Compiled);
    private static readonly Regex GroupMarkerRegex = new(@"^[IVXLC]+\.\s+", RegexOptions.Compiled);
    private static readonly Regex YearInSheetNameRegex = new(@"^(\d{4})", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex AnchorRangeRegex = new(@"^\s*(\d+)\s*-\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex LastNumberRegex = new(@"(\d+)\s*$", RegexOptions.Compiled);

    private readonly ILogger<ExcelImportService> _logger;

    public ExcelImportService(ILogger<ExcelImportService> logger)
    {
        _logger = logger;
    }

    public ImportResult Import(Stream stream, string fileName)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);
            var mlSheet = FindMlSheet(workbook);
            var printScan = ScanPrintSheets(workbook);

            List<ArchiveBook> books;
            if (mlSheet is not null)
            {
                books = ParseMlSheet(mlSheet, printScan);
            }
            else
            {
                // Dự phòng: file chỉ có các sheet in, không có ML.
                books = ParsePrintSheetsOnly(workbook, printScan);
            }

            if (books.Count == 0)
            {
                return new ImportResult
                {
                    Success = false,
                    Error = "Không tìm thấy dữ liệu hồ sơ. File cần có sheet ML (khuyến nghị) hoặc các sheet năm với ô K1 = BANG_IN_HANG_LOAT."
                };
            }

            AttachSourceVolumes(books, printScan);
            EnsureUniqueIds(books);

            var data = new ArchiveData
            {
                SourceFileName = Path.GetFileName(fileName),
                ImportedAt = DateTimeOffset.Now,
                Books = books
                    .OrderBy(b => b.Year)
                    .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            _logger.LogInformation(
                "Import xong {File}: {Years} năm, {Types} loại, {Dossiers} hồ sơ",
                fileName, data.Books.Count, data.TotalTypes, data.TotalDossiers);

            return new ImportResult { Success = true, Data = data };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi import file {File}", fileName);
            return new ImportResult
            {
                Success = false,
                Error = "Không đọc được file Excel. Chi tiết: " + ex.Message
            };
        }
    }

    /// <summary>
    /// Quét tất cả sheet in một lần, lưu block theo dòng ML và tên sổ in theo năm.
    /// </summary>
    private static PrintSheetScan ScanPrintSheets(XLWorkbook workbook)
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

    /// <summary>
    /// Import toàn bộ dòng dữ liệu của ML, theo dõi nhóm → năm → loại.
    /// </summary>
    private List<ArchiveBook> ParseMlSheet(IXLWorksheet mlSheet, PrintSheetScan printScan)
    {
        var lastRow = mlSheet.LastRowUsed()?.RowNumber() ?? 0;
        var books = new Dictionary<int, ArchiveBook>();
        var currentGroup = string.Empty;
        var currentYear = 0;
        var currentType = string.Empty;
        var lastNumber = 0;
        var skippedRows = 0;

        for (var row = 2; row <= lastRow; row++)
        {
            var summary = CellText(mlSheet.Cell(row, 4));
            if (summary.Length == 0)
            {
                continue;
            }

            // Thứ tự kiểm tra rất quan trọng: một số dòng tiêu đề (ví dụ
            // "NĂM 2020", "*Ghi chú Kết hôn") vẫn có giá trị ở cột B.
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
                // Dòng tiêu đề cột hoặc dòng rác trước NĂM đầu tiên.
                skippedRows++;
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
                else
                {
                    numberLabel = string.Empty;
                }
            }

            if (!books.TryGetValue(currentYear, out var book))
            {
                book = new ArchiveBook
                {
                    Id = currentYear.ToString(CultureInfo.InvariantCulture),
                    Name = currentYear.ToString(CultureInfo.InvariantCulture),
                    Year = currentYear
                };
                books[currentYear] = book;
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

            book.Dossiers.Add(new Dossier
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

        if (skippedRows > 0)
        {
            _logger.LogDebug("Bỏ qua {Count} dòng tiêu đề/không xác định trong sheet ML", skippedRows);
        }

        return books.Values.OrderBy(b => b.Year).ToList();
    }

    /// <summary>
    /// Dự phòng khi workbook không có sheet ML: đọc trực tiếp các sheet in.
    /// </summary>
    private static List<ArchiveBook> ParsePrintSheetsOnly(XLWorkbook workbook, PrintSheetScan printScan)
    {
        var books = new Dictionary<int, ArchiveBook>();

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

            if (!books.TryGetValue(year, out var book))
            {
                book = new ArchiveBook
                {
                    Id = year.ToString(CultureInfo.InvariantCulture),
                    Name = year.ToString(CultureInfo.InvariantCulture),
                    Year = year
                };
                books[year] = book;
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

                book.Dossiers.Add(new Dossier
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
            if (books.TryGetValue(pair.Key, out var book))
            {
                book.SourceVolumes = pair.Value.ToList();
            }
        }

        return books.Values.OrderBy(b => b.Year).ToList();
    }

    private static void AttachSourceVolumes(List<ArchiveBook> books, PrintSheetScan printScan)
    {
        foreach (var book in books)
        {
            if (printScan.VolumesByYear.TryGetValue(book.Year, out var volumes))
            {
                book.SourceVolumes = volumes
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                book.SourceVolumes = new List<string>();
            }
        }
    }

    private static bool IsPrintSheet(IXLWorksheet worksheet)
    {
        var marker = CellText(worksheet.Cell("K1"));
        if (marker.Equals("BANG_IN_HANG_LOAT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Nhận diện mềm hơn cho file không có K1.
        var title = CellText(worksheet.Cell("D8"));
        var subTitle = CellText(worksheet.Cell("D9"));
        var subTitleFormula = worksheet.Cell("D9").HasFormula ? worksheet.Cell("D9").FormulaA1 : string.Empty;

        return title.StartsWith("MỤC LỤC VĂN BẢN", StringComparison.OrdinalIgnoreCase)
               && (subTitle.Contains("Hồ sơ số", StringComparison.OrdinalIgnoreCase)
                   || subTitleFormula.Contains("Hồ sơ số", StringComparison.OrdinalIgnoreCase));
    }

    private static IXLWorksheet? FindMlSheet(XLWorkbook workbook)
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

    private static int ParseYear(string sheetName)
    {
        var match = YearInSheetNameRegex.Match(sheetName);
        return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : 0;
    }

    private static void EnsureUniqueIds(List<ArchiveBook> books)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var book in books)
        {
            var baseId = string.IsNullOrWhiteSpace(book.Id)
                ? (book.Year > 0 ? book.Year.ToString(CultureInfo.InvariantCulture) : "nam")
                : book.Id;
            var id = baseId;
            var suffix = 2;
            while (!used.Add(id))
            {
                id = $"{baseId}-{suffix++}";
            }

            book.Id = id;
        }
    }

    private static string CellText(IXLCell cell)
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

    /// <summary>
    /// Lấy phần số đầu tiên giống hàm Val trong macro: "346/1" -> 346.
    /// </summary>
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

    private sealed class PrintSheetScan
    {
        public Dictionary<int, PrintBlockInfo> BlocksByMlRow { get; } = new();
        public Dictionary<int, List<string>> VolumesByYear { get; } = new();
    }

    private sealed class PrintBlockInfo
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
}
