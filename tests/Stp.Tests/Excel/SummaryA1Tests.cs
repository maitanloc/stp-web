using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Jobs;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

public sealed class SummaryA1Tests
{
    [Fact]
    public async Task Export_PatchesDocumentNumberAndNumberLabelColumns()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "a1.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var ml = workbook.AddWorksheet("ML");
                ml.Cell("A1").Value = "Hộp số";
                ml.Cell("C1").Value = "Hồ sơ số";
                ml.Cell("D1").Value = "Trích yếu nội dung";
                ml.Cell("E1").Value = "Thời gian tài liệu";
                ml.Cell("F1").Value = "Số tờ";
                ml.Cell("G1").Value = "Ghi chú";
                ml.Cell("B2").Value = 1;
                ml.Cell("C2").Value = "HS-01";
                ml.Cell("D2").Value = "Hồ sơ về việc đăng ký kết hôn của Bà A và Ông B";
                ml.Cell("E2").Value = new DateTime(1990, 1, 10);
                ml.Cell("F2").Value = "93/QĐ";
                ml.Cell("G2").Value = "01-20";
                workbook.SaveAs(path);
            }

            var service = CreateService(directory);
            await using (var stream = File.OpenRead(path))
            {
                var session = await service.CreateSessionAsync(stream, Path.GetFileName(path));

                Assert.Equal("B", session.NumberColumn);
                Assert.Equal("C", session.DocumentNumberColumn);
                Assert.Equal("G", session.SheetNumberColumn);

                var changes = new List<SummaryChange>
                {
                    new(2, Array.Empty<string>(), SheetNumber: "21-40", DocumentNumber: "HS-99", NumberLabel: "5A")
                };

                var preview = service.Preview(session.SessionId, changes);
                var row = Assert.Single(preview);
                Assert.True(row.DocumentNumberChanged);
                Assert.True(row.NumberLabelChanged);
                Assert.Equal("HS-99", row.DocumentNumber);
                Assert.Equal("5A", row.NumberLabel);

                var result = await service.ExportAsync(session.SessionId, changes, outputFileName: "my-out.xlsx");
                Assert.Equal("my-out.xlsx", result.FileName);

                var output = Path.Combine(directory, "a1-out.xlsx");
                await File.WriteAllBytesAsync(output, result.Content);
                using var outWorkbook = new XLWorkbook(output);
                var sheet = outWorkbook.Worksheet("ML");
                Assert.Equal("5A", sheet.Cell("B2").GetString());
                Assert.Equal("HS-99", sheet.Cell("C2").GetString());
                Assert.Equal("21-40", sheet.Cell("G2").GetString());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetReport_GroupsByYear()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "report.xlsx");
            using (var workbook = new XLWorkbook())
            {
                var ml = workbook.AddWorksheet("ML");
                ml.Cell("A1").Value = "Hộp số";
                ml.Cell("C1").Value = "Hồ sơ số";
                ml.Cell("D1").Value = "Trích yếu nội dung";
                ml.Cell("E1").Value = "Thời gian tài liệu";
                ml.Cell("F1").Value = "Số tờ";
                ml.Cell("G1").Value = "Ghi chú";

                ml.Cell("B2").Value = 1;
                ml.Cell("C2").Value = "HS-01";
                ml.Cell("D2").Value = "Hồ sơ về việc đăng ký kết hôn của Bà A và Ông B";
                ml.Cell("E2").Value = new DateTime(1990, 1, 10);
                ml.Cell("G2").Value = "01-20";

                ml.Cell("D3").Value = "Hồ sơ đăng ký kết hôn của Bà C và Ông D";
                ml.Cell("E3").Value = new DateTime(1991, 1, 10);

                workbook.SaveAs(path);
            }

            var service = CreateService(directory);
            await using (var stream = File.OpenRead(path))
            {
                var session = await service.CreateSessionAsync(stream, Path.GetFileName(path));
                var report = service.GetReport(session.SessionId);

                Assert.Equal(2, report.Count);
                Assert.Equal(1990, report[0].Year);
                Assert.Equal(0, report[0].MissingSheetNumber);
                Assert.Equal(1, report[1].MissingSheetNumber);
                Assert.Equal(1, report[1].MissingDocumentNumber);
                Assert.Equal(1, report[1].MissingNumber);
                Assert.Equal(2, report.Sum(item => item.NeedsNormalization));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SummaryToolService CreateService(string directory)
    {
        var storage = new FileStorage(Options.Create(new StorageOptions
        {
            RootPath = Path.Combine(directory, "data")
        }));

        return new SummaryToolService(storage, NullLogger<SummaryToolService>.Instance);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-a1-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
