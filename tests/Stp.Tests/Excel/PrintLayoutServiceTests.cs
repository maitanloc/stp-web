using ClosedXML.Excel;
using Stp.Core.Domain;
using Stp.Core.Excel;
using Stp.Infrastructure.Excel;

namespace Stp.Tests.Excel;

public sealed class PrintLayoutServiceTests
{
    [Theory]
    [InlineData(0, 0, PrintFitClass.Normal)]
    [InlineData(3, 549, PrintFitClass.Normal)]
    [InlineData(4, 0, PrintFitClass.Compact)]
    [InlineData(0, 550, PrintFitClass.Compact)]
    [InlineData(7, 849, PrintFitClass.Compact)]
    [InlineData(8, 0, PrintFitClass.Tight)]
    [InlineData(0, 850, PrintFitClass.Tight)]
    [InlineData(11, 1199, PrintFitClass.Tight)]
    [InlineData(12, 0, PrintFitClass.Tiny)]
    [InlineData(200, 5000, PrintFitClass.Tiny)]
    public void ResolveFit_MatchesWebThresholds(
        int documentCount,
        int textLength,
        PrintFitClass expected)
    {
        Assert.Equal(expected, PrintLayoutService.ResolveFit(documentCount, textLength));
    }

    [Fact]
    public void ResolveFit_CountsAllDocumentColumns()
    {
        var dossier = new DossierRecord
        {
            Documents =
            {
                new DocumentLine { TrichYeu = new string('a', 550) }
            }
        };

        Assert.Equal(PrintFitClass.Compact, PrintLayoutService.ResolveFit(dossier));
    }

    [Fact]
    public void ResolveBlockLayout_ShortDossier_UsesNormalMetrics()
    {
        var service = new PrintLayoutService();
        var dossier = CreateDossier("Đăng ký kết hôn cho ông A và bà B");

        var layout = service.ResolveBlockLayout(dossier);

        Assert.Equal(PrintFitClass.Normal, layout.FitClass);
        Assert.Equal(9.5, layout.TableFontSize, 3);
        Assert.Equal(15.0, layout.TitleFontSize, 3);
        Assert.True(layout.DataRowHeight >= 62.0);
        Assert.InRange(layout.BlockHeight, 1.0, 740.0);
    }

    [Fact]
    public void ResolveBlockLayout_LongSummary_GrowsDataRow()
    {
        var service = new PrintLayoutService();
        var shortDossier = CreateDossier("Trích yếu ngắn");
        var longDossier = CreateDossier(new string('a', 400));

        var shortLayout = service.ResolveBlockLayout(shortDossier);
        var longLayout = service.ResolveBlockLayout(longDossier);

        Assert.True(longLayout.DataRowHeight > shortLayout.DataRowHeight);
        Assert.Equal(shortLayout.FitClass, longLayout.FitClass);
    }

    [Fact]
    public void ResolveBlockLayout_VeryLongSummary_ReducesFontToFitPage()
    {
        var service = new PrintLayoutService();
        var dossier = CreateDossier(new string('a', 4000));

        var layout = service.ResolveBlockLayout(dossier);

        Assert.Equal(PrintFitClass.Tiny, layout.FitClass);
        Assert.True(layout.TableFontSize < 8.0);
        Assert.True(layout.TitleFontSize < 13.0);
        Assert.True(layout.BlockHeight <= 740.0);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("Ngắn", 1)]
    public void EstimateWrappedLines_ShortText_ReturnsOneLine(string? text, int expected)
    {
        Assert.Equal(expected, PrintLayoutService.EstimateWrappedLines(text, 40, 9.5));
    }

    [Fact]
    public void EstimateWrappedLines_LongText_ReturnsMultipleLines()
    {
        var lines = PrintLayoutService.EstimateWrappedLines(new string('a', 500), 40, 9.5);

        Assert.True(lines > 1);
    }

    [Fact]
    public void ApplyWorksheetSetup_SetsA4PortraitPrintAreaAndBreaks()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("1990");
        var service = new PrintLayoutService(new PrintLayoutOptions
        {
            HeaderText = "STP - Mục lục hồ sơ",
            FooterText = "Trang &P/&N"
        });

        service.ApplyWorksheetSetup(worksheet, 8, 25, new[] { 17, 26 });

        Assert.Equal(XLPaperSize.A4Paper, worksheet.PageSetup.PaperSize);
        Assert.Equal(XLPageOrientation.Portrait, worksheet.PageSetup.PageOrientation);
        Assert.Equal(1, worksheet.PageSetup.PagesWide);
        Assert.Equal(0, worksheet.PageSetup.PagesTall);
        Assert.True(worksheet.PageSetup.CenterHorizontally);

        var printArea = Assert.Single(worksheet.PageSetup.PrintAreas);
        Assert.Equal(8, printArea.RangeAddress.FirstAddress.RowNumber);
        Assert.Equal(1, printArea.RangeAddress.FirstAddress.ColumnNumber);
        Assert.Equal(25, printArea.RangeAddress.LastAddress.RowNumber);
        Assert.Equal(7, printArea.RangeAddress.LastAddress.ColumnNumber);

        Assert.Contains(17, worksheet.PageSetup.RowBreaks);
        Assert.DoesNotContain(26, worksheet.PageSetup.RowBreaks);

        Assert.Equal(
            "STP - Mục lục hồ sơ",
            worksheet.PageSetup.Header.Center.GetText(XLHFOccurrence.OddPages));
        Assert.Equal(
            "Trang &P/&N",
            worksheet.PageSetup.Footer.Center.GetText(XLHFOccurrence.OddPages));
    }

    private static DossierRecord CreateDossier(string summary, int documentCount = 1)
    {
        var dossier = new DossierRecord
        {
            MlRow = 5,
            Number = 1,
            NumberLabel = "01",
            Title = "MỤC LỤC VĂN BẢN",
            OriginalSummary = summary
        };

        for (var index = 0; index < documentCount; index++)
        {
            dossier.Documents.Add(new DocumentLine
            {
                Stt = index + 1,
                SoKyHieu = "12/1990/KH",
                Ngay = "12/03/1990",
                TrichYeu = summary,
                TacGia = "Ủy ban nhân dân tỉnh Đồng Nai",
                ToSo = "01-02",
                GhiChu = string.Empty
            });
        }

        return dossier;
    }
}
