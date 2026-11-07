using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Domain;
using Stp.Core.Excel;
using Stp.Infrastructure.Excel;

namespace Stp.Tests.Excel;

public sealed class Type2SheetGeneratorTests
{
    [Fact]
    public void Generate_MlOnlyWorkbook_CreatesPrintSheetsForEveryYear()
    {
        var directory = CreateTempDirectory();
        var inputPath = CreateMlOnlyWorkbook(directory);
        var outputPath = Path.Combine(directory, "output.xlsx");

        try
        {
            var outcome = CreateGenerator().Generate(new PrintGenerationRequest
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                Overwrite = true
            });

            Assert.True(outcome.Success);
            Assert.True(outcome.Generated);
            Assert.Equal(new[] { 1990, 1991 }, outcome.GeneratedYears);
            Assert.Equal(2, outcome.GeneratedSheetCount);
            Assert.Empty(outcome.SkippedYears);
            Assert.True(File.Exists(outputPath));

            using var workbook = new XLWorkbook(outputPath);

            Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet("DS").Visibility);
            Assert.Equal("1990", workbook.Worksheet("DS").Cell("A1").GetString());
            Assert.Equal("1991", workbook.Worksheet("DS").Cell("A2").GetString());

            var sheet1990 = workbook.Worksheet("1990");
            var sheet1991 = workbook.Worksheet("1991");

            AssertPrintSheet(sheet1990, expectedDocuments: 2, firstMlRow: 5);
            AssertPrintSheet(sheet1991, expectedDocuments: 1, firstMlRow: 9);

            Assert.Equal(2, sheet1990.Cell("K6").GetValue<int>());
            Assert.Equal(1, sheet1991.Cell("K6").GetValue<int>());

            Assert.Equal("1990", sheet1990.Cell("E5").GetString());
            Assert.Equal("1991", sheet1991.Cell("E5").GetString());
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public void Generate_WritesControlPanelFormulasAndYearSelector()
    {
        var directory = CreateTempDirectory();
        var inputPath = CreateMlOnlyWorkbook(directory);
        var outputPath = Path.Combine(directory, "output.xlsx");

        try
        {
            var outcome = CreateGenerator().Generate(new PrintGenerationRequest
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                Overwrite = true
            });

            Assert.True(outcome.Generated);

            using var workbook = new XLWorkbook(outputPath);
            var worksheet = workbook.Worksheet("1990");

            Assert.Equal("BANG_IN_HANG_LOAT", worksheet.Cell("K1").GetString());
            Assert.Equal("Tất cả hồ sơ", worksheet.Cell("K2").GetString());
            Assert.Equal("Từ số - đến số", worksheet.Cell("K3").GetString());
            Assert.Equal("Từng số", worksheet.Cell("K4").GetString());
            Assert.Equal("LOC", worksheet.Cell("L7").GetString());
            Assert.Equal(1, worksheet.Cell("K15").GetValue<int>());
            Assert.Equal(2, worksheet.Cell("K16").GetValue<int>());

            Assert.Contains("COUNTIF", worksheet.Cell("K7").FormulaA1);
            Assert.Contains("Hồ sơ số:*", worksheet.Cell("K7").FormulaA1);
            Assert.Contains("$K$9", worksheet.Cell("A6").FormulaA1);
            Assert.Contains("$K$6", worksheet.Cell("A6").FormulaA1);
            Assert.Contains("$K$15", worksheet.Cell("A6").FormulaA1);

            Assert.Contains("Hồ sơ số", worksheet.Cell("D9").FormulaA1);
            Assert.Contains("ML", worksheet.Cell("D9").FormulaA1);
            Assert.Contains("$B5", worksheet.Cell("D9").FormulaA1);

            var modeValidation = FindValidation(worksheet, "A3");
            Assert.Equal(XLAllowedValues.List, modeValidation.AllowedValues);
            Assert.Contains("$K$2:$K$4", modeValidation.Value);

            var yearValidation = FindValidation(worksheet, "E5");
            Assert.Equal(XLAllowedValues.List, yearValidation.AllowedValues);
            Assert.Contains("DS!$A$1:$A$2", yearValidation.Value);

            Assert.True(worksheet.Cell("E3").HasHyperlink);
            var hyperlink = worksheet.Cell("E3").GetHyperlink();
            Assert.Contains("E3", hyperlink.InternalAddress);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public void Generate_SecondRunOnGeneratedWorkbook_DoesNotDuplicateSheets()
    {
        var directory = CreateTempDirectory();
        var inputPath = CreateMlOnlyWorkbook(directory);
        var firstOutput = Path.Combine(directory, "first.xlsx");
        var secondOutput = Path.Combine(directory, "second.xlsx");

        try
        {
            var generator = CreateGenerator();
            var first = generator.Generate(new PrintGenerationRequest
            {
                InputPath = inputPath,
                OutputPath = firstOutput,
                Overwrite = true
            });

            Assert.True(first.Generated);

            var second = generator.Generate(new PrintGenerationRequest
            {
                InputPath = firstOutput,
                OutputPath = secondOutput,
                Overwrite = true
            });

            Assert.True(second.Success);
            Assert.False(second.Generated);
            Assert.Equal(new[] { 1990, 1991 }, second.SkippedYears);
            Assert.False(File.Exists(secondOutput));

            using var workbook = new XLWorkbook(firstOutput);
            Assert.Single(workbook.Worksheets, sheet => sheet.Name == "1990");
            Assert.Single(workbook.Worksheets, sheet => sheet.Name == "1991");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public void Generate_WorkbookWithoutMl_ReturnsFailure()
    {
        var directory = CreateTempDirectory();
        var inputPath = Path.Combine(directory, "other.xlsx");

        try
        {
            using (var workbook = new XLWorkbook())
            {
                workbook.AddWorksheet("Sheet1").Cell("A1").Value = "Không phải ML";
                workbook.SaveAs(inputPath);
            }

            var outcome = CreateGenerator().Generate(new PrintGenerationRequest
            {
                InputPath = inputPath,
                OutputPath = Path.Combine(directory, "output.xlsx"),
                Overwrite = true
            });

            Assert.False(outcome.Success);
            Assert.False(outcome.Generated);
            Assert.NotEmpty(outcome.Warnings);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void AssertPrintSheet(
        IXLWorksheet worksheet,
        int expectedDocuments,
        int firstMlRow)
    {
        Assert.False(worksheet.ShowGridLines);
        Assert.Equal(XLPaperSize.A4Paper, worksheet.PageSetup.PaperSize);
        Assert.Equal(XLPageOrientation.Portrait, worksheet.PageSetup.PageOrientation);
        Assert.Equal(1, worksheet.PageSetup.PagesWide);
        Assert.Equal(0, worksheet.PageSetup.PagesTall);
        Assert.Equal(7, worksheet.SheetView.SplitRow);

        var printArea = Assert.Single(worksheet.PageSetup.PrintAreas);
        Assert.Equal(8, printArea.RangeAddress.FirstAddress.RowNumber);
        Assert.Equal(expectedDocuments * 9 + 7, printArea.RangeAddress.LastAddress.RowNumber);

        Assert.Equal(expectedDocuments - 1, worksheet.PageSetup.RowBreaks.Count);
        for (var block = 1; block < expectedDocuments; block++)
        {
            Assert.Contains(8 + (9 * block), worksheet.PageSetup.RowBreaks);
        }

        Assert.Equal("MỤC LỤC VĂN BẢN", worksheet.Cell("D8").GetString());
        Assert.Equal(15.0, worksheet.Cell("D8").Style.Font.FontSize, 3);
        Assert.Equal("STT", worksheet.Cell("A10").GetString());
        Assert.Equal("Số ký hiệu văn bản", worksheet.Cell("B10").GetString());
        Assert.Equal("Ghi chú", worksheet.Cell("G10").GetString());
        Assert.True(worksheet.Cell("A10").Style.Font.Bold);
        Assert.True(worksheet.Cell("A10").Style.Alignment.WrapText);

        Assert.Equal(1, worksheet.Cell("A11").GetValue<int>());
        Assert.Contains($"$F{firstMlRow}", worksheet.Cell("B11").FormulaA1);
        Assert.Contains($"$E{firstMlRow}", worksheet.Cell("C11").FormulaA1);
        Assert.Contains($"$D{firstMlRow}", worksheet.Cell("D11").FormulaA1);
        Assert.Contains($"$G{firstMlRow}", worksheet.Cell("F11").FormulaA1);
        Assert.Contains($"$H{firstMlRow}", worksheet.Cell("G11").FormulaA1);

        Assert.Equal("Ủy ban nhân dân tỉnh Đồng Nai", worksheet.Cell("E11").GetString());
        Assert.Equal(9.5, worksheet.Cell("A11").Style.Font.FontSize, 3);

        Assert.True(worksheet.Range("A12:G12").IsMerged());
        Assert.Contains("Tổng số tờ", worksheet.Cell("A12").FormulaA1);

        Assert.True(worksheet.Column(1).Width > 5.0);
        Assert.True(worksheet.Column(4).Width > 30.0);
        Assert.True(worksheet.Column(8).IsHidden);
        Assert.True(worksheet.Column(11).IsHidden);
        Assert.True(worksheet.Column(12).IsHidden);
    }

    private static IXLDataValidation FindValidation(IXLWorksheet worksheet, string address)
    {
        var cell = worksheet.Cell(address);
        return worksheet.DataValidations.Single(validation => validation.Ranges.Any(range =>
            range.RangeAddress.FirstAddress.RowNumber == cell.Address.RowNumber
            && range.RangeAddress.FirstAddress.ColumnNumber == cell.Address.ColumnNumber
            && range.RangeAddress.LastAddress.RowNumber == cell.Address.RowNumber
            && range.RangeAddress.LastAddress.ColumnNumber == cell.Address.ColumnNumber));
    }

    private static Type2SheetGenerator CreateGenerator()
    {
        var options = Options.Create(new PrintLayoutOptions());
        var inspector = new ExcelInspector(NullLogger<ExcelInspector>.Instance);
        var layout = new PrintLayoutService(options.Value);

        return new Type2SheetGenerator(
            inspector,
            layout,
            options,
            NullLogger<Type2SheetGenerator>.Instance);
    }

    private static string CreateMlOnlyWorkbook(string directory)
    {
        var path = Path.Combine(directory, "ml-only.xlsx");

        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");
        ml.Cell("A1").Value = "Hộp số";
        ml.Cell("B1").Value = "Số hồ sơ";
        ml.Cell("C1").Value = "Số phụ";
        ml.Cell("D1").Value = "Trích yếu nội dung";
        ml.Cell("E1").Value = "Ngày";
        ml.Cell("F1").Value = "Số ký hiệu";
        ml.Cell("G1").Value = "Tờ số";
        ml.Cell("H1").Value = "Ghi chú";
        ml.Cell("I1").Value = "Trích yếu gốc";

        ml.Cell("D2").Value = "I. HỒ SƠ ĐĂNG KÝ KẾT HÔN";
        ml.Cell("D3").Value = "NĂM 1990";
        ml.Cell("D4").Value = "*Đăng ký kết hôn";
        ml.Cell("B5").Value = "01";
        ml.Cell("E5").Value = "12/03/1990";
        ml.Cell("F5").Value = "12/1990/KH";
        ml.Cell("D5").Value = "Đăng ký kết hôn cho ông Nguyễn Văn A và bà Trần Thị B";
        ml.Cell("G5").Value = "01-02";
        ml.Cell("H5").Value = "Bản gốc";
        ml.Cell("I5").Value = "Trích yếu gốc 1";
        ml.Cell("B6").Value = "02";
        ml.Cell("E6").Value = "20/06/1990";
        ml.Cell("F6").Value = "20/1990/KH";
        ml.Cell("D6").Value = "Đăng ký kết hôn cho ông Lê Văn C và bà Phạm Thị D";
        ml.Cell("G6").Value = "03-05";
        ml.Cell("H6").Value = "Bản sao";

        ml.Cell("D7").Value = "NĂM 1991";
        ml.Cell("D8").Value = "*Đăng ký kết hôn";
        ml.Cell("B9").Value = "01";
        ml.Cell("E9").Value = "05/01/1991";
        ml.Cell("F9").Value = "01/1991/KH";
        ml.Cell("D9").Value = "Đăng ký kết hôn cho ông Võ Văn E và bà Đỗ Thị F";
        ml.Cell("G9").Value = "01-01";
        ml.Cell("H9").Value = "Bản gốc";

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "stp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Bỏ qua thư mục tạm còn file đang mở.
        }
    }
}
