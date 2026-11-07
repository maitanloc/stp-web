using ClosedXML.Excel;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

/// <summary>Mức thu gọn của một block in, tương ứng các lớp CSS fit-* của bản web.</summary>
public enum PrintFitClass
{
    Normal = 0,
    Compact = 1,
    Tight = 2,
    Tiny = 3
}

/// <summary>Kích thước chữ và chiều cao dòng của một block in.</summary>
public sealed record PrintBlockLayout(
    PrintFitClass FitClass,
    double TableFontSize,
    double TitleFontSize,
    double SubtitleFontSize,
    double TotalFontSize,
    double TitleRowHeight,
    double SubtitleRowHeight,
    double HeaderRowHeight,
    double DataRowHeight,
    double TotalRowHeight,
    double SpacerRowHeight)
{
    public double BlockHeight =>
        TitleRowHeight
        + SubtitleRowHeight
        + HeaderRowHeight
        + DataRowHeight
        + TotalRowHeight
        + (SpacerRowHeight * 4);
}

/// <summary>Cấu hình bố cục in dùng chung cho sheet in sinh tự động và sheet in hiện có.</summary>
public sealed class PrintLayoutOptions
{
    public string DefaultAuthor { get; set; } = "Ủy ban nhân dân tỉnh Đồng Nai";

    public string Title { get; set; } = "MỤC LỤC VĂN BẢN";

    /// <summary>Độ rộng 7 cột theo tỷ lệ bản in web (A4 dọc).</summary>
    public double[] ColumnWidths { get; set; } = { 5.5, 12.5, 11.5, 40.0, 15.5, 7.5, 4.5 };

    public double LeftRightMarginInches { get; set; } = 0.4;

    public double TopBottomMarginInches { get; set; } = 0.5;

    public double HeaderFooterMarginInches { get; set; } = 0.3;

    public string HeaderText { get; set; } = string.Empty;

    public string FooterText { get; set; } = string.Empty;

    public bool CenterHorizontally { get; set; } = true;

    /// <summary>Chiều cao tối đa một block (pt) trước khi phải giảm thêm cỡ chữ.</summary>
    public double MaxBlockHeightPoints { get; set; } = 740;
}

/// <summary>
/// Tính bố cục in A4 dọc, 7 cột, mỗi hồ sơ một trang. Các ngưỡng thu gọn và cỡ chữ
/// lấy đúng theo bản in web hiện tại (site.css: fit-compact/fit-tight/fit-tiny).
/// </summary>
public sealed class PrintLayoutService
{
    private readonly PrintLayoutOptions _options;

    public PrintLayoutService(PrintLayoutOptions? options = null)
    {
        _options = options ?? new PrintLayoutOptions();
    }

    public PrintLayoutOptions Options => _options;

    /// <summary>Ngưỡng giống hệt Dossier.PrintFitClass của bản web.</summary>
    public static PrintFitClass ResolveFit(int documentCount, int textLength)
    {
        if (documentCount >= 12 || textLength >= 1200)
        {
            return PrintFitClass.Tiny;
        }

        if (documentCount >= 8 || textLength >= 850)
        {
            return PrintFitClass.Tight;
        }

        if (documentCount >= 4 || textLength >= 550)
        {
            return PrintFitClass.Compact;
        }

        return PrintFitClass.Normal;
    }

    public static PrintFitClass ResolveFit(DossierRecord dossier)
    {
        var textLength = dossier.Documents.Sum(d =>
            (d.TrichYeu?.Length ?? 0)
            + (d.SoKyHieu?.Length ?? 0)
            + (d.Ngay?.Length ?? 0)
            + (d.TacGia?.Length ?? 0)
            + (d.ToSo?.Length ?? 0)
            + (d.GhiChu?.Length ?? 0));

        return ResolveFit(dossier.Documents.Count, textLength);
    }

    /// <summary>Tính bố cục cho một hồ sơ; tự giảm cỡ chữ nếu block vượt chiều cao cho phép.</summary>
    public PrintBlockLayout ResolveBlockLayout(DossierRecord dossier)
    {
        var fit = ResolveFit(dossier);
        var metrics = GetMetrics(fit);
        var texts = BuildTexts(dossier);
        var widths = _options.ColumnWidths;

        var tableFont = metrics.TableFontSize;
        var titleFont = metrics.TitleFontSize;
        var subtitleFont = metrics.SubtitleFontSize;
        var totalFont = metrics.TotalFontSize;

        while (true)
        {
            var layout = BuildLayout(
                fit,
                tableFont,
                titleFont,
                subtitleFont,
                totalFont,
                metrics,
                texts,
                widths);

            if (layout.BlockHeight <= _options.MaxBlockHeightPoints || tableFont <= 6.0)
            {
                return layout;
            }

            tableFont = Math.Max(6.0, tableFont - 0.5);
            if (tableFont <= 7.0)
            {
                titleFont = Math.Max(11.0, titleFont - 0.5);
                subtitleFont = Math.Max(9.0, subtitleFont - 0.5);
                totalFont = Math.Max(7.5, totalFont - 0.5);
            }
        }
    }

    /// <summary>Áp dụng khổ giấy, lề, vùng in, header/footer và ngắt trang cho sheet in.</summary>
    public void ApplyWorksheetSetup(
        IXLWorksheet worksheet,
        int firstPrintRow,
        int lastPrintRow,
        IReadOnlyCollection<int>? pageBreakBeforeRows = null)
    {
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        worksheet.PageSetup.Margins.Left = _options.LeftRightMarginInches;
        worksheet.PageSetup.Margins.Right = _options.LeftRightMarginInches;
        worksheet.PageSetup.Margins.Top = _options.TopBottomMarginInches;
        worksheet.PageSetup.Margins.Bottom = _options.TopBottomMarginInches;
        worksheet.PageSetup.Margins.Header = _options.HeaderFooterMarginInches;
        worksheet.PageSetup.Margins.Footer = _options.HeaderFooterMarginInches;
        worksheet.PageSetup.CenterHorizontally = _options.CenterHorizontally;

        // Fit theo chiều rộng, chiều cao để tự động (0). Mỗi hồ sơ đã có ngắt trang riêng.
        worksheet.PageSetup.PagesWide = 1;
        worksheet.PageSetup.PagesTall = 0;

        worksheet.PageSetup.PrintAreas.Clear();
        worksheet.PageSetup.PrintAreas.Add($"A{firstPrintRow}:G{lastPrintRow}");

        if (!string.IsNullOrWhiteSpace(_options.HeaderText))
        {
            worksheet.PageSetup.Header.Center.AddText(_options.HeaderText);
        }

        if (!string.IsNullOrWhiteSpace(_options.FooterText))
        {
            worksheet.PageSetup.Footer.Center.AddText(_options.FooterText);
        }

        if (pageBreakBeforeRows is not null)
        {
            foreach (var row in pageBreakBeforeRows.OrderBy(r => r))
            {
                if (row > firstPrintRow && row <= lastPrintRow)
                {
                    worksheet.PageSetup.AddHorizontalPageBreak(row);
                }
            }
        }
    }

    private static PrintBlockLayout BuildLayout(
        PrintFitClass fit,
        double tableFont,
        double titleFont,
        double subtitleFont,
        double totalFont,
        PrintFitMetrics metrics,
        IReadOnlyList<string> texts,
        IReadOnlyList<double> widths)
    {
        var headerHeight = Math.Max(
            metrics.MinHeaderRowHeight,
            CalculateRowHeight(
                new[]
                {
                    "STT",
                    "Số ký hiệu văn bản",
                    "Ngày, tháng, văn bản",
                    "Trích yếu nội dung văn bản",
                    "Tác giả Văn bản",
                    "Tờ số",
                    "Ghi chú"
                },
                widths,
                tableFont,
                bold: true,
                minHeight: metrics.MinHeaderRowHeight));

        var dataHeight = CalculateRowHeight(
            texts,
            widths,
            tableFont,
            bold: false,
            minHeight: metrics.MinDataRowHeight);

        return new PrintBlockLayout(
            fit,
            tableFont,
            titleFont,
            subtitleFont,
            totalFont,
            TitleRowHeight: RoundUp(titleFont * 1.25 + 6),
            SubtitleRowHeight: RoundUp(subtitleFont * 1.25 + 6),
            HeaderRowHeight: RoundUp(headerHeight),
            DataRowHeight: RoundUp(dataHeight),
            TotalRowHeight: RoundUp(totalFont * 1.25 + 8),
            SpacerRowHeight: 10);
    }

    private static PrintFitMetrics GetMetrics(PrintFitClass fit)
    {
        return fit switch
        {
            PrintFitClass.Compact => new PrintFitMetrics(
                fit,
                TableFontSize: 9.0,
                TitleFontSize: 15.0,
                SubtitleFontSize: 11.5,
                TotalFontSize: 9.5,
                MinDataRowHeight: 51.0,
                MinHeaderRowHeight: 46.0),
            PrintFitClass.Tight => new PrintFitMetrics(
                fit,
                TableFontSize: 8.5,
                TitleFontSize: 14.0,
                SubtitleFontSize: 11.5,
                TotalFontSize: 9.0,
                MinDataRowHeight: 40.0,
                MinHeaderRowHeight: 40.0),
            PrintFitClass.Tiny => new PrintFitMetrics(
                fit,
                TableFontSize: 8.0,
                TitleFontSize: 13.0,
                SubtitleFontSize: 10.5,
                TotalFontSize: 8.5,
                MinDataRowHeight: 28.0,
                MinHeaderRowHeight: 36.0),
            _ => new PrintFitMetrics(
                fit,
                TableFontSize: 9.5,
                TitleFontSize: 15.0,
                SubtitleFontSize: 11.5,
                TotalFontSize: 10.0,
                MinDataRowHeight: 62.0,
                MinHeaderRowHeight: 52.0)
        };
    }

    private static IReadOnlyList<string> BuildTexts(DossierRecord dossier)
    {
        var document = dossier.Documents.FirstOrDefault();
        return new[]
        {
            document?.Stt > 0 ? document.Stt.ToString() : "1",
            document?.SoKyHieu ?? string.Empty,
            document?.Ngay ?? string.Empty,
            document?.TrichYeu ?? dossier.OriginalSummary,
            document?.TacGia ?? string.Empty,
            document?.ToSo ?? string.Empty,
            document?.GhiChu ?? string.Empty
        };
    }

    private static double CalculateRowHeight(
        IReadOnlyList<string> texts,
        IReadOnlyList<double> widths,
        double fontSize,
        bool bold,
        double minHeight)
    {
        var maxHeight = minHeight;
        var count = Math.Min(texts.Count, widths.Count);
        for (var i = 0; i < count; i++)
        {
            var lines = EstimateWrappedLines(texts[i], widths[i], fontSize, bold);
            var height = (lines * fontSize * 1.3) + 7.0;
            if (height > maxHeight)
            {
                maxHeight = height;
            }
        }

        return maxHeight;
    }

    /// <summary>
    /// Ước lượng số dòng sau khi wrap. Độ rộng cột Excel tính theo font mặc định 11pt;
    /// chữ Times New Roman hẹp hơn nên nhân thêm hệ số, và trừ hao cho dấu tiếng Việt.
    /// </summary>
    public static int EstimateWrappedLines(string? text, double columnWidth, double fontSize, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 1;
        }

        var fontFactor = (11.0 / Math.Max(6.0, fontSize)) * 0.9;
        if (bold)
        {
            fontFactor *= 0.9;
        }

        var charsPerLine = Math.Max(1.0, columnWidth * fontFactor);
        var lines = 0;
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var length = paragraph.Trim().Length;
            lines += Math.Max(1, (int)Math.Ceiling(length / charsPerLine));
        }

        return Math.Max(1, lines);
    }

    private static double RoundUp(double value) => Math.Ceiling(value);

    private sealed record PrintFitMetrics(
        PrintFitClass FitClass,
        double TableFontSize,
        double TitleFontSize,
        double SubtitleFontSize,
        double TotalFontSize,
        double MinDataRowHeight,
        double MinHeaderRowHeight);
}
