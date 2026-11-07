using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stp.Core.Domain;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Sinh các sheet in năm cho workbook chỉ có ML. Mỗi hồ sơ là một block 9 dòng,
/// ngắt trang sau mỗi block để in A4 dọc đúng một hồ sơ một trang.
///
/// Các ô điều khiển K1:K16, L7:L... và hyperlink E3 giữ tương thích với
/// StpArchiveMacro.bas (Lọc/Xem trước/Chuyển năm).
/// </summary>
public sealed class Type2SheetGenerator : IPrintSheetGenerator
{
    public const string MarkerValue = "BANG_IN_HANG_LOAT";
    public const int FirstBlockRow = 8;
    public const int BlockRows = 9;
    public const int DataOffset = 3;
    public const int LastSpacerOffset = 8;

    private static readonly string[] Headers =
    {
        "STT",
        "Số ký hiệu văn bản",
        "Ngày, tháng, văn bản",
        "Trích yếu nội dung văn bản",
        "Tác giả Văn bản",
        "Tờ số",
        "Ghi chú"
    };

    private static readonly XLColor DarkBlue = XLColor.FromHtml("#1F3864");
    private static readonly XLColor LightBlue = XLColor.FromHtml("#DDEBF7");
    private static readonly XLColor ActionBlue = XLColor.FromHtml("#2F5597");
    private static readonly XLColor NoteGray = XLColor.FromHtml("#404040");
    private static readonly XLColor HeaderGray = XLColor.FromHtml("#F2F2F2");

    private readonly IExcelInspector _inspector;
    private readonly PrintLayoutService _layout;
    private readonly PrintLayoutOptions _options;
    private readonly ILogger<Type2SheetGenerator> _logger;

    public Type2SheetGenerator(
        IExcelInspector inspector,
        PrintLayoutService layout,
        IOptions<PrintLayoutOptions> options,
        ILogger<Type2SheetGenerator> logger)
    {
        _inspector = inspector;
        _layout = layout;
        _options = options.Value;
        _logger = logger;
    }

    public PrintGenerationOutcome Generate(
        PrintGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new ArgumentException("Thiếu đường dẫn file đầu vào.", nameof(request));
        }

        var inputPath = Path.GetFullPath(request.InputPath);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Không tìm thấy file Excel đầu vào.", inputPath);
        }

        var snapshot = _inspector.Read(inputPath);
        if (snapshot.Kind == WorkbookKind.Other)
        {
            return new PrintGenerationOutcome
            {
                Success = false,
                InputPath = inputPath,
                OutputPath = inputPath,
                Warnings = new[] { "File không có sheet ML nên không thể sinh sheet in." }
            };
        }

        var missingYears = snapshot.Years
            .Where(year => year.Dossiers.Count > 0)
            .Where(year => !HasPrintSheet(year))
            .ToList();

        if (snapshot.Kind != WorkbookKind.MlOnly || missingYears.Count == 0)
        {
            return new PrintGenerationOutcome
            {
                Success = true,
                Generated = false,
                InputPath = inputPath,
                OutputPath = inputPath,
                SkippedYears = snapshot.Years.Select(y => y.Year).ToList(),
                Warnings = new[]
                {
                    snapshot.Kind == WorkbookKind.MlOnly
                        ? "Không có năm nào cần sinh sheet in."
                        : "File đã có sheet in; giữ nguyên bố cục hiện có."
                }
            };
        }

        var outputPath = ResolveOutputPath(inputPath, request.OutputPath, request.Overwrite);
        var overwriteInput = string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase);
        var workingPath = overwriteInput
            ? Path.Combine(Path.GetDirectoryName(inputPath)!, $"~stp-{Guid.NewGuid():N}.xlsx")
            : outputPath;

        var warnings = new List<string>();
        var generatedYears = new List<int>();

        try
        {
            using (var workbook = new XLWorkbook(inputPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var mlSheet = ExcelArchiveParser.FindMlSheet(workbook)
                              ?? throw new InvalidDataException("Không tìm thấy sheet ML trong workbook.");

                var existingSheetNames = workbook.Worksheets
                    .Select(ws => ws.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var plannedNames = new Dictionary<int, string>();
                var usedNames = new HashSet<string>(existingSheetNames, StringComparer.OrdinalIgnoreCase);

                foreach (var year in missingYears)
                {
                    plannedNames[year.Year] = MakeUniqueSheetName(
                        year.Year.ToString(CultureInfo.InvariantCulture),
                        usedNames);
                }

                var yearSheetNames = workbook.Worksheets
                    .Where(ExcelArchiveParser.IsPrintSheet)
                    .Select(ws => ws.Name)
                    .Concat(plannedNames.Values)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                EnsureYearSelectorSheet(workbook, yearSheetNames, warnings);

                foreach (var year in missingYears)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sheetName = plannedNames[year.Year];
                    var worksheet = workbook.Worksheets.Add(sheetName);
                    GenerateYearSheet(worksheet, year, mlSheet.Name, yearSheetNames.Count);
                    generatedYears.Add(year.Year);

                    _logger.LogInformation(
                        "Sinh sheet in {Sheet}: {Count} hồ sơ, {Rows} dòng",
                        sheetName,
                        year.Dossiers.Count,
                        LastBlockRow(year.Dossiers.Count));
                }

                mlSheet.SetTabActive();
                workbook.SaveAs(workingPath);
            }

            if (overwriteInput)
            {
                File.Move(workingPath, inputPath, overwrite: true);
            }

            _logger.LogInformation(
                "Đã sinh {Years} sheet in cho {File} -> {Output}",
                generatedYears.Count,
                Path.GetFileName(inputPath),
                outputPath);

            return new PrintGenerationOutcome
            {
                Success = true,
                Generated = generatedYears.Count > 0,
                InputPath = inputPath,
                OutputPath = outputPath,
                GeneratedYears = generatedYears,
                SkippedYears = snapshot.Years
                    .Where(y => !generatedYears.Contains(y.Year))
                    .Select(y => y.Year)
                    .ToList(),
                Warnings = warnings
            };
        }
        catch
        {
            TryDelete(workingPath, inputPath);
            throw;
        }
    }

    private static bool HasPrintSheet(ArchiveYearData year)
    {
        return year.SourceSheets.Count > 0
               || year.Dossiers.Any(d => d.HasPrintLayout);
    }

    private void GenerateYearSheet(
        IXLWorksheet worksheet,
        ArchiveYearData year,
        string mlSheetName,
        int yearSheetCount)
    {
        worksheet.ShowGridLines = false;
        worksheet.Style.Font.FontName = "Times New Roman";
        worksheet.Style.Font.FontSize = 9.5;

        for (var i = 0; i < _options.ColumnWidths.Length && i < 7; i++)
        {
            worksheet.Column(i + 1).Width = _options.ColumnWidths[i];
        }

        worksheet.Column(8).Width = 3;
        worksheet.Column(11).Width = 10;
        worksheet.Column(12).Width = 6;
        worksheet.Columns("H:L").Hide();

        WriteControlPanel(worksheet, year, yearSheetCount);
        WriteBlocks(worksheet, year, mlSheetName);

        var lastBlockEnd = LastBlockRow(year.Dossiers.Count);
        var breaks = new List<int>();
        for (var block = 1; block < year.Dossiers.Count; block++)
        {
            breaks.Add(FirstBlockRow + (BlockRows * block));
        }

        _layout.ApplyWorksheetSetup(worksheet, FirstBlockRow, lastBlockEnd, breaks);
        worksheet.SheetView.FreezeRows(7);
    }

    private void WriteControlPanel(IXLWorksheet worksheet, ArchiveYearData year, int yearSheetCount)
    {
        var title = string.IsNullOrWhiteSpace(year.SheetName)
            ? $"MỤC LỤC HỒ SƠ   ·   NĂM {year.Year}"
            : $"MỤC LỤC HỒ SƠ   ·   NĂM {year.Year}";

        worksheet.Row(1).Height = 34;
        worksheet.Row(2).Height = 18;
        worksheet.Row(3).Height = 38;
        worksheet.Row(4).Height = 17;
        worksheet.Row(5).Height = 32;
        worksheet.Row(6).Height = 26;
        worksheet.Row(7).Height = 8;

        var top = worksheet.Range("A1:G1");
        top.Merge();
        top.Value = title;
        top.Style.Font.FontName = "Calibri";
        top.Style.Font.FontSize = 16;
        top.Style.Font.Bold = true;
        top.Style.Font.FontColor = XLColor.White;
        top.Style.Fill.BackgroundColor = DarkBlue;
        top.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        top.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        StylePanelHeader(worksheet.Range("A2:C2"), "①  CÁCH CHỌN HỒ SƠ");
        StylePanelHeader(worksheet.Range("D2:D2"), "②  HƯỚNG DẪN");
        StylePanelHeader(worksheet.Range("E2:G2"), "③  XEM VÀ IN");

        var mode = worksheet.Range("A3:C3");
        mode.Merge();
        mode.Value = "Tất cả hồ sơ";
        mode.Style.Font.FontName = "Calibri";
        mode.Style.Font.FontSize = 14;
        mode.Style.Font.Bold = true;
        mode.Style.Font.FontColor = DarkBlue;
        mode.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        mode.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ApplyBoxBorder(mode, XLBorderStyleValues.Medium);

        worksheet.Cell("D3").FormulaA1 =
            "IF($A$3=$K$3,\"Nhập số đầu và số cuối vào hai ô bên dưới.\"," +
            "IF($A$3=$K$4,\"Gõ các số cần in vào ô bên dưới, cách nhau bằng dấu phẩy. " +
            "Ví dụ: 85, 90, 100-105\",\"Hiện tất cả hồ sơ của sheet này. Không cần nhập gì thêm.\"))";
        StyleNote(worksheet.Cell("D3"), italic: true, fontSize: 11);

        var preview = worksheet.Range("E3:G3");
        preview.Merge();
        preview.Value = "▶  XEM TRƯỚC KHI IN";
        preview.Style.Font.FontName = "Calibri";
        preview.Style.Font.FontSize = 14;
        preview.Style.Font.Bold = true;
        preview.Style.Font.FontColor = XLColor.White;
        preview.Style.Fill.BackgroundColor = ActionBlue;
        preview.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        preview.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Cell("E3").SetHyperlink(new XLHyperlink(
            $"'{EscapeSheetName(worksheet.Name)}'!E3",
            "Xem các bảng đang hiện trước khi in"));

        StyleInputLabel(worksheet.Range("A4:B4"), "Từ số");
        StyleInputLabel(worksheet.Range("C4:C4"), "Đến số");
        StyleInputLabel(worksheet.Range("D4:D4"), "Các số cần in (ví dụ: 85, 90, 100-105)");
        StyleInputLabel(worksheet.Range("E4:G4"), "Chuyển sang năm khác");

        worksheet.Cell("A5").Style.NumberFormat.Format = "0";
        worksheet.Cell("C5").Style.NumberFormat.Format = "0";
        StyleInputCell(worksheet.Cell("A5"));
        StyleInputCell(worksheet.Cell("C5"));
        StyleInputCell(worksheet.Cell("D5"));

        var yearSelector = worksheet.Range("E5:G5");
        yearSelector.Merge();
        yearSelector.Value = worksheet.Name;
        yearSelector.Style.Font.FontName = "Calibri";
        yearSelector.Style.Font.FontSize = 14;
        yearSelector.Style.Font.Bold = true;
        yearSelector.Style.Font.FontColor = DarkBlue;
        yearSelector.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        yearSelector.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ApplyBoxBorder(yearSelector, XLBorderStyleValues.Medium);

        var status = worksheet.Range("A6:G6");
        status.Merge();
        worksheet.Cell("A6").FormulaA1 =
            "IF($K$9<>\"\",$K$9,\"✔  Đang hiện \"&$K$6&\"/\"&$K$7&\" hồ sơ\"" +
            "&IF($K$6=$K$7,\" (tất cả)\",\" đã chọn\")" +
            "&\"      ·      Sheet này có hồ sơ số từ \"&$K$15&\" đến \"&$K$16)";
        status.Style.Font.FontName = "Calibri";
        status.Style.Font.FontSize = 12;
        status.Style.Font.Bold = true;
        status.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        status.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        WriteHelperCells(worksheet, year, yearSheetCount);
        AddDataValidation(worksheet, yearSheetCount);
    }

    private void WriteHelperCells(IXLWorksheet worksheet, ArchiveYearData year, int yearSheetCount)
    {
        var dossiers = year.Dossiers;
        var lastBlockEnd = LastBlockRow(dossiers.Count);
        var minNumber = dossiers.Count == 0 ? 0 : dossiers.Min(d => d.Number);
        var maxNumber = dossiers.Count == 0 ? 0 : dossiers.Max(d => d.Number);

        worksheet.Cell("K1").Value = MarkerValue;
        worksheet.Cell("K1").Style.Font.FontColor = XLColor.White;

        worksheet.Cell("K2").Value = "Tất cả hồ sơ";
        worksheet.Cell("K3").Value = "Từ số - đến số";
        worksheet.Cell("K4").Value = "Từng số";
        worksheet.Cell("K6").Value = dossiers.Count;
        worksheet.Cell("K7").FormulaA1 =
            $"COUNTIF($D$8:$D${lastBlockEnd},\"Hồ sơ số:*\")";
        worksheet.Cell("K10").Value = "⚠  Hãy nhập số vào cả hai ô “Từ số” và “Đến số”.";
        worksheet.Cell("K11").Value =
            "⚠  Hãy gõ các số cần in vào ô “Các số cần in”, ví dụ: 85, 90, 100-105";
        worksheet.Cell("K12").Value = "⚠  Không tìm thấy hồ sơ số: ";
        worksheet.Cell("K13").Value = "⚠  Không có hồ sơ nào khớp – đã hiện lại tất cả.  ";
        worksheet.Cell("K15").Value = minNumber;
        worksheet.Cell("K16").Value = maxNumber;

        worksheet.Cell("L7").Value = "LOC";
        for (var block = 0; block < dossiers.Count; block++)
        {
            var start = FirstBlockRow + (BlockRows * block);
            for (var row = start; row < start + BlockRows; row++)
            {
                worksheet.Cell(row, 12).Value = 1;
            }
        }

        // Dữ liệu trợ giúp không in ra; giữ nguyên để macro đọc/ghi.
        worksheet.Range("K1:K16").Style.Font.FontName = "Calibri";
        worksheet.Range("K1:K16").Style.Font.FontSize = 9;
        worksheet.Range("L7:L8").Style.Font.FontName = "Calibri";
        worksheet.Range("L7:L8").Style.Font.FontSize = 9;
    }

    private void AddDataValidation(IXLWorksheet worksheet, int yearSheetCount)
    {
        var modeValidation = worksheet.Range("A3").CreateDataValidation();
        modeValidation.List("$K$2:$K$4");
        modeValidation.InCellDropdown = true;
        modeValidation.ShowErrorMessage = true;
        modeValidation.ErrorTitle = "Chọn trong danh sách";
        modeValidation.ErrorMessage = "Hãy bấm mũi tên ▼ và chọn 1 trong 3 cách.";
        modeValidation.ErrorStyle = XLErrorStyle.Stop;

        AddNumberValidation(worksheet.Range("A5"));
        AddNumberValidation(worksheet.Range("C5"));

        if (yearSheetCount > 0)
        {
            var yearValidation = worksheet.Range("E5").CreateDataValidation();
            yearValidation.List($"DS!$A$1:$A${yearSheetCount}");
            yearValidation.InCellDropdown = true;
            yearValidation.ShowErrorMessage = true;
            yearValidation.ErrorTitle = "Chọn trong danh sách";
            yearValidation.ErrorMessage = "Hãy chọn một năm có trong danh sách.";
            yearValidation.ErrorStyle = XLErrorStyle.Stop;
        }
    }

    private static void AddNumberValidation(IXLRange range)
    {
        var validation = range.CreateDataValidation();
        validation.AllowedValues = XLAllowedValues.Decimal;
        validation.Operator = XLOperator.EqualOrGreaterThan;
        validation.MinValue = "0";
        validation.InCellDropdown = true;
        validation.ShowInputMessage = true;
        validation.InputTitle = "Nhập số";
        validation.InputMessage = "Gõ số hồ sơ, ví dụ 85.";
        validation.ShowErrorMessage = true;
        validation.ErrorTitle = "Chỉ nhập số";
        validation.ErrorMessage = "Ô này chỉ nhận số, ví dụ 85.";
        validation.ErrorStyle = XLErrorStyle.Stop;
    }

    private void WriteBlocks(IXLWorksheet worksheet, ArchiveYearData year, string mlSheetName)
    {
        var dossiers = year.Dossiers;
        var escapedMl = EscapeSheetName(mlSheetName);

        for (var index = 0; index < dossiers.Count; index++)
        {
            var dossier = dossiers[index];
            var start = FirstBlockRow + (BlockRows * index);
            var subtitleRow = start + 1;
            var headerRow = start + 2;
            var dataRow = start + DataOffset;
            var totalRow = start + 4;
            var layout = _layout.ResolveBlockLayout(dossier);

            worksheet.Row(start).Height = layout.TitleRowHeight;
            worksheet.Row(subtitleRow).Height = layout.SubtitleRowHeight;
            worksheet.Row(headerRow).Height = layout.HeaderRowHeight;
            worksheet.Row(dataRow).Height = layout.DataRowHeight;
            worksheet.Row(totalRow).Height = layout.TotalRowHeight;
            for (var spacer = 5; spacer <= LastSpacerOffset; spacer++)
            {
                worksheet.Row(start + spacer).Height = layout.SpacerRowHeight;
            }

            var title = worksheet.Cell(start, 4);
            title.Value = string.IsNullOrWhiteSpace(dossier.Title)
                ? _options.Title
                : dossier.Title;
            title.Style.Font.FontName = "Times New Roman";
            title.Style.Font.FontSize = layout.TitleFontSize;
            title.Style.Font.Bold = true;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            title.Style.Alignment.WrapText = true;

            var subtitle = worksheet.Cell(subtitleRow, 4);
            var numberFallback = string.IsNullOrWhiteSpace(dossier.NumberLabel)
                ? dossier.Number.ToString(CultureInfo.InvariantCulture)
                : dossier.NumberLabel.Replace("\"", "\"\"");
            subtitle.FormulaA1 =
                $"IF('{escapedMl}'!$B{dossier.MlRow}=\"\",\"Hồ sơ số: {numberFallback}\"," +
                $"\"Hồ sơ số: \"&'{escapedMl}'!$B{dossier.MlRow})";
            subtitle.Style.Font.FontName = "Times New Roman";
            subtitle.Style.Font.FontSize = layout.SubtitleFontSize;
            subtitle.Style.Font.Bold = true;
            subtitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            subtitle.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            subtitle.Style.Alignment.WrapText = true;

            WriteHeaderRow(worksheet, headerRow, layout.TableFontSize);
            WriteDataRow(
                worksheet,
                dataRow,
                dossier,
                escapedMl,
                layout.TableFontSize);

            var total = worksheet.Range(totalRow, 1, totalRow, 7);
            total.Merge();
            worksheet.Cell(totalRow, 1).FormulaA1 =
                $"IFERROR(\"Tổng số tờ: \"&VALUE(MID(F{dataRow},FIND(\"-\",F{dataRow})+1,10))&\" tờ\"," +
                "\"Tổng số tờ: … tờ\")";
            total.Style.Font.FontName = "Times New Roman";
            total.Style.Font.FontSize = layout.TotalFontSize;
            total.Style.Font.Bold = true;
            total.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            total.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ApplyBoxBorder(total, XLBorderStyleValues.Thin);
        }
    }

    private static void WriteHeaderRow(IXLWorksheet worksheet, int row, double fontSize)
    {
        for (var column = 1; column <= 7; column++)
        {
            var cell = worksheet.Cell(row, column);
            cell.Value = Headers[column - 1];
            cell.Style.Font.FontName = "Times New Roman";
            cell.Style.Font.FontSize = fontSize;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Fill.BackgroundColor = HeaderGray;
        }

        ApplyBoxBorder(worksheet.Range(row, 1, row, 7), XLBorderStyleValues.Thin);
    }

    private void WriteDataRow(
        IXLWorksheet worksheet,
        int row,
        DossierRecord dossier,
        string escapedMl,
        double fontSize)
    {
        var document = dossier.Documents.FirstOrDefault();

        worksheet.Cell(row, 1).Value = document?.Stt > 0 ? document.Stt : 1;
        worksheet.Cell(row, 2).FormulaA1 =
            $"IF('{escapedMl}'!$F{dossier.MlRow}=\"\",\"\",'{escapedMl}'!$F{dossier.MlRow})";
        worksheet.Cell(row, 3).FormulaA1 =
            $"IF('{escapedMl}'!$E{dossier.MlRow}=\"\",\"\",IF(ISNUMBER('{escapedMl}'!$E{dossier.MlRow})," +
            $"'{escapedMl}'!$E{dossier.MlRow},IFERROR(DATE(VALUE(RIGHT(TRIM('{escapedMl}'!$E{dossier.MlRow}),4))," +
            $"VALUE(MID(TRIM('{escapedMl}'!$E{dossier.MlRow}),FIND(\"/\",TRIM('{escapedMl}'!$E{dossier.MlRow}))+1," +
            $"FIND(\"/\",TRIM('{escapedMl}'!$E{dossier.MlRow}),FIND(\"/\",TRIM('{escapedMl}'!$E{dossier.MlRow}))+1)-" +
            $"FIND(\"/\",TRIM('{escapedMl}'!$E{dossier.MlRow}))-1))," +
            $"VALUE(LEFT(TRIM('{escapedMl}'!$E{dossier.MlRow}),FIND(\"/\",TRIM('{escapedMl}'!$E{dossier.MlRow}))-1)))," +
            $"'{escapedMl}'!$E{dossier.MlRow})))";
        worksheet.Cell(row, 4).FormulaA1 =
            $"IF('{escapedMl}'!$D{dossier.MlRow}=\"\",\"\",'{escapedMl}'!$D{dossier.MlRow})";
        worksheet.Cell(row, 5).Value = string.IsNullOrWhiteSpace(document?.TacGia)
            ? _options.DefaultAuthor
            : document.TacGia;
        worksheet.Cell(row, 6).FormulaA1 =
            $"IF('{escapedMl}'!$G{dossier.MlRow}=\"\",\"\",'{escapedMl}'!$G{dossier.MlRow})";
        worksheet.Cell(row, 7).FormulaA1 =
            $"IF('{escapedMl}'!$H{dossier.MlRow}=\"\",\"\",'{escapedMl}'!$H{dossier.MlRow})";

        worksheet.Cell(row, 3).Style.NumberFormat.Format = "dd/mm/yyyy";

        for (var column = 1; column <= 7; column++)
        {
            var cell = worksheet.Cell(row, column);
            cell.Style.Font.FontName = "Times New Roman";
            cell.Style.Font.FontSize = fontSize;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        ApplyBoxBorder(worksheet.Range(row, 1, row, 7), XLBorderStyleValues.Thin);
    }

    private static void EnsureYearSelectorSheet(
        XLWorkbook workbook,
        IReadOnlyList<string> yearSheetNames,
        List<string> warnings)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(ws =>
            string.Equals(ws.Name, "DS", StringComparison.OrdinalIgnoreCase));

        if (sheet is null)
        {
            sheet = workbook.Worksheets.Add("DS");
        }

        var existing = sheet.Column(1).CellsUsed()
            .Select(c => c.GetString())
            .Where(v => v.Length > 0)
            .ToList();

        if (existing.Count > 0)
        {
            var missing = yearSheetNames
                .Where(name => !existing.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (missing.Count > 0)
            {
                warnings.Add(
                    $"Sheet DS đã có danh sách năm; bỏ qua {missing.Count} sheet năm mới. " +
                    "Có thể bổ sung thủ công nếu cần chọn năm bằng dropdown.");
            }

            return;
        }

        for (var i = 0; i < yearSheetNames.Count; i++)
        {
            sheet.Cell(i + 1, 1).Value = yearSheetNames[i];
        }

        sheet.Hide();
        sheet.Column(1).Width = 14;
    }

    private static void StylePanelHeader(IXLRange range, string text)
    {
        range.Merge();
        range.Value = text;
        range.Style.Font.FontName = "Calibri";
        range.Style.Font.FontSize = 10;
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = DarkBlue;
        range.Style.Fill.BackgroundColor = LightBlue;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void StyleInputLabel(IXLRange range, string text)
    {
        range.Merge();
        range.Value = text;
        range.Style.Font.FontName = "Calibri";
        range.Style.Font.FontSize = 10;
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = DarkBlue;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void StyleInputCell(IXLCell cell)
    {
        cell.Style.Font.FontName = "Calibri";
        cell.Style.Font.FontSize = 12;
        cell.Style.Font.FontColor = XLColor.Black;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ApplyBoxBorder(cell, XLBorderStyleValues.Thin);
    }

    private static void StyleNote(IXLCell cell, bool italic, double fontSize)
    {
        cell.Style.Font.FontName = "Calibri";
        cell.Style.Font.FontSize = fontSize;
        cell.Style.Font.Italic = italic;
        cell.Style.Font.FontColor = NoteGray;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Alignment.WrapText = true;
    }

    private static void ApplyBoxBorder(IXLRange range, XLBorderStyleValues style)
    {
        range.Style.Border.TopBorder = style;
        range.Style.Border.BottomBorder = style;
        range.Style.Border.LeftBorder = style;
        range.Style.Border.RightBorder = style;
    }

    private static void ApplyBoxBorder(IXLCell cell, XLBorderStyleValues style)
    {
        cell.Style.Border.TopBorder = style;
        cell.Style.Border.BottomBorder = style;
        cell.Style.Border.LeftBorder = style;
        cell.Style.Border.RightBorder = style;
    }

    private static int LastBlockRow(int dossierCount)
    {
        return FirstBlockRow + (BlockRows * Math.Max(1, dossierCount)) - 1;
    }

    private static string MakeUniqueSheetName(string baseName, HashSet<string> usedNames)
    {
        var name = baseName;
        var suffix = 1;
        while (usedNames.Contains(name))
        {
            suffix++;
            name = $"{baseName} ({suffix})";
        }

        usedNames.Add(name);
        return name;
    }

    private static string EscapeSheetName(string name)
    {
        return name.Replace("'", "''");
    }

    private static string ResolveOutputPath(string inputPath, string? requested, bool overwrite)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var full = Path.GetFullPath(requested);
            if (!overwrite && File.Exists(full))
            {
                throw new IOException($"File kết quả đã tồn tại: {full}");
            }

            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return full;
        }

        var inputDirectory = Path.GetDirectoryName(inputPath)!;
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(inputDirectory, $"{baseName}.print.xlsx");
        var counter = 1;
        while (File.Exists(candidate) && !overwrite)
        {
            candidate = Path.Combine(inputDirectory, $"{baseName}.print ({counter++}).xlsx");
        }

        return candidate;
    }

    private static void TryDelete(string path, string inputPath)
    {
        try
        {
            if (!string.Equals(path, inputPath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Bỏ qua file tạm không xóa được.
        }
    }
}
