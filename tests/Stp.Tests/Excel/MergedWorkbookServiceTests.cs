using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Excel;
using Stp.Infrastructure.Excel;

namespace Stp.Tests.Excel;

public sealed class MergedWorkbookServiceTests
{
    [Fact]
    public void Merge_TwoFiles_KeepsDuplicatesAndAddsSourceColumn()
    {
        var directory = CreateTempDirectory();
        var first = CreateMlWorkbook(
            Path.Combine(directory, "first.xlsx"),
            new[]
            {
                ("01", "Hồ sơ A1", "1990"),
                ("02", "Hồ sơ A2", "1990"),
                ("01", "Hồ sơ A3", "1991")
            });
        var second = CreateMlWorkbook(
            Path.Combine(directory, "second.xlsx"),
            new[]
            {
                ("01", "Hồ sơ B1", "1990"),
                ("02", "Hồ sơ B2", "1990"),
                ("03", "Hồ sơ B3", "1990")
            });
        var output = Path.Combine(directory, "merged.xlsx");

        try
        {
            var service = CreateService();
            var outcome = service.Merge(new MergeRequest
            {
                InputPaths = new[] { first, second },
                OutputPath = output,
                Overwrite = true,
                GeneratePrintSheets = true
            });

            Assert.True(outcome.Success, outcome.ErrorMessage);
            Assert.Equal(2, outcome.FileCount);
            Assert.Equal(2, outcome.YearCount);
            Assert.Equal(6, outcome.DossierCount);
            Assert.Equal(2, outcome.DuplicateCount);
            Assert.True(File.Exists(output));

            using var workbook = new XLWorkbook(output);
            Assert.NotNull(workbook.Worksheet("ML"));
            Assert.NotNull(workbook.Worksheet("GOP"));
            Assert.NotNull(workbook.Worksheet("1990"));
            Assert.NotNull(workbook.Worksheet("1991"));

            var ml = workbook.Worksheet("ML");
            Assert.Equal("Hộp số", ml.Cell("A1").GetString());
            Assert.Equal("Nguồn file", ml.Cell("J1").GetString());

            var sources = ml.Column(10).CellsUsed()
                .Skip(1)
                .Select(cell => cell.GetString())
                .Where(value => value.Length > 0)
                .ToList();
            Assert.Contains("first.xlsx", sources);
            Assert.Contains("second.xlsx", sources);
            Assert.Equal(6, sources.Count);

            var gop = workbook.Worksheet("GOP");
            Assert.Equal("Tổng", gop.Cell("A4").GetString());
            Assert.Equal(2, gop.Cell("B4").GetValue<int>());
            Assert.Equal(6, gop.Cell("C4").GetValue<int>());

            Assert.Equal(5, workbook.Worksheet("1990").Cell("K6").GetValue<int>());
            Assert.Equal(1, workbook.Worksheet("1991").Cell("K6").GetValue<int>());
            Assert.Equal(
                XLPaperSize.A4Paper,
                workbook.Worksheet("1990").PageSetup.PaperSize);
            Assert.Equal(
                XLPageOrientation.Portrait,
                workbook.Worksheet("1990").PageSetup.PageOrientation);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public void Merge_NoValidFile_ReturnsFailure()
    {
        var directory = CreateTempDirectory();
        try
        {
            var other = Path.Combine(directory, "other.xlsx");
            using (var workbook = new XLWorkbook())
            {
                workbook.AddWorksheet("Sheet1").Cell("A1").Value = "no ML";
                workbook.SaveAs(other);
            }

            var service = CreateService();
            var outcome = service.Merge(new MergeRequest
            {
                InputPaths = new[] { other },
                OutputPath = Path.Combine(directory, "merged.xlsx"),
                GeneratePrintSheets = true
            });

            Assert.False(outcome.Success);
            Assert.NotEmpty(outcome.Warnings);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static MergedWorkbookService CreateService()
    {
        var options = Options.Create(new PrintLayoutOptions());
        var inspector = new ExcelInspector(NullLogger<ExcelInspector>.Instance);
        var layout = new PrintLayoutService(options.Value);
        var generator = new Type2SheetGenerator(
            inspector,
            layout,
            options,
            NullLogger<Type2SheetGenerator>.Instance);

        return new MergedWorkbookService(
            inspector,
            generator,
            NullLogger<MergedWorkbookService>.Instance);
    }

    private static string CreateMlWorkbook(
        string path,
        IReadOnlyList<(string Number, string Summary, string Year)> dossiers)
    {
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

        var row = 2;
        string? currentYear = null;
        foreach (var (number, summary, year) in dossiers)
        {
            if (!string.Equals(currentYear, year, StringComparison.Ordinal))
            {
                ml.Cell(row, 4).Value = $"NĂM {year}";
                row++;
                ml.Cell(row, 4).Value = "*Đăng ký kết hôn";
                row++;
                currentYear = year;
            }

            ml.Cell(row, 2).Value = number;
            ml.Cell(row, 4).Value = summary;
            ml.Cell(row, 5).Value = "01/01/" + year;
            ml.Cell(row, 6).Value = $"{number}/{year}/KH";
            ml.Cell(row, 7).Value = "01-02";
            ml.Cell(row, 8).Value = "Bản gốc";
            ml.Cell(row, 9).Value = summary;
            row++;
        }

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "stp-merge-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDelete(string directory)
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
