using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

public sealed class SummaryToolServiceTests
{
    [Fact]
    public async Task Parse_ExtractsCatalogAndKeepsPersonAlias()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            Assert.Equal("ML", session.SheetName);
            Assert.Equal("D", session.SummaryColumn);
            Assert.Equal(4, session.Rows.Count);
            Assert.Equal(1, session.ExistingListCount);
            Assert.Contains("phiếu đề xuất", session.Catalog);
            Assert.Contains("giấy khai sinh", session.Catalog);
            Assert.Contains("hộ chiếu", session.Catalog);

            var existing = session.Rows.Single(row => row.RowNumber == 5);
            Assert.True(existing.HasDocumentList);
            Assert.Equal(2, existing.Items.Count);
            Assert.Equal(
                "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
                + "với người nước ngoài của Bà NGUYỄN THỊ A và Ông JOHN SMITH",
                existing.BaseSummary);

            var alias = session.Rows.Single(row => row.RowNumber == 6);
            Assert.False(alias.HasDocumentList);
            Assert.Contains("(NGUYỄN ĐỨC THÁI)", alias.OriginalSummary);

            var noParentheses = session.Rows.Single(row => row.RowNumber == 7);
            Assert.False(noParentheses.HasDocumentList);

            var nonTrailing = session.Rows.Single(row => row.RowNumber == 8);
            Assert.False(nonTrailing.HasDocumentList);
            Assert.Contains("(Từ số 01 - 20). Năm 1990", nonTrailing.OriginalSummary);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_ChangesOnlySelectedSummaryCellsAndPreservesOtherParts()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var row5 = session.Rows.Single(row => row.RowNumber == 5);
            var row7 = session.Rows.Single(row => row.RowNumber == 7);
            var row8 = session.Rows.Single(row => row.RowNumber == 8);
            var alias = session.Rows.Single(row => row.RowNumber == 6);

            var changes = new List<SummaryChange>
            {
                new(row5.RowNumber, new[] { "phiếu đề xuất", "giấy khai sinh", "hộ chiếu" }),
                new(row7.RowNumber, new[] { "hộ chiếu" }),
                new(row8.RowNumber, new[] { "giấy xác nhận cư trú" })
            };

            var preview = service.Preview(session.SessionId, changes);
            Assert.All(preview, row => Assert.True(row.Changed));
            Assert.Equal(
                row5.BaseSummary + " (phiếu đề xuất, giấy khai sinh, hộ chiếu)",
                preview[0].Summary);
            Assert.Equal(
                row7.BaseSummary + " (hộ chiếu)",
                preview[1].Summary);

            var result = await service.ExportAsync(session.SessionId, changes);
            Assert.Equal(3, result.ChangedRows);
            Assert.Equal("sample_trichyeu.xlsx", result.FileName);
            Assert.True(result.Content.Length > 0);

            var output = Path.Combine(directory, "exported.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using (var workbook = new XLWorkbook(output))
            {
                var ml = workbook.Worksheet("ML");
                Assert.Equal(
                    row5.BaseSummary + " (phiếu đề xuất, giấy khai sinh, hộ chiếu)",
                    ml.Cell("D5").GetString());
                Assert.Equal(alias.OriginalSummary, ml.Cell("D6").GetString());
                Assert.Equal(row7.BaseSummary + " (hộ chiếu)", ml.Cell("D7").GetString());
                Assert.Equal(
                    row8.BaseSummary + " (giấy xác nhận cư trú)",
                    ml.Cell("D8").GetString());

                // Định dạng của ô trích yếu được giữ nguyên.
                Assert.True(ml.Cell("D5").Style.Font.Bold);

                var form = workbook.Worksheet("form");
                Assert.Equal("Tra cứu", form.Cell("A1").GetString());
                Assert.Equal(30, form.Cell("B1").GetValue<int>());
            }

            AssertSharedStringsUnchanged(source, output);
            AssertOnlyWorkbookAndSheetChanged(source, output);
            AssertWorkbookRequestsFullCalculation(output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<SummaryWorkbook> OpenSessionAsync(
        SummaryToolService service,
        string source)
    {
        await using var stream = File.OpenRead(source);
        return await service.CreateSessionAsync(stream, Path.GetFileName(source));
    }

    private static SummaryToolService CreateService(string directory)
    {
        var storage = new FileStorage(Options.Create(new StorageOptions
        {
            RootPath = Path.Combine(directory, "data")
        }));

        return new SummaryToolService(storage, NullLogger<SummaryToolService>.Instance);
    }

    private static string CreateWorkbook(string directory)
    {
        var path = Path.Combine(directory, "sample.xlsx");

        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");
        ml.Cell("A1").Value = "Hộp số";
        ml.Cell("C1").Value = "Hồ sơ số";
        ml.Cell("D1").Value = "Trích yếu nội dung";
        ml.Cell("E1").Value = "Thời gian tài liệu";
        ml.Cell("F1").Value = "Số tờ";
        ml.Cell("G1").Value = "Ghi chú";

        ml.Cell("D2").Value = "I. Văn phòng";
        ml.Cell("D3").Value = "NĂM 1990";
        ml.Cell("D4").Value = "* Đăng ký kết hôn";

        ml.Cell("S4").Value = "phiếu đề xuất";
        ml.Cell("T4").Value = "giấy biên nhận";
        ml.Cell("U4").Value = "giấy khai sinh";
        ml.Cell("V4").Value = "hộ chiếu";

        ml.Cell("B5").Value = 36;
        ml.Cell("E5").Value = new DateTime(1990, 1, 23);
        ml.Cell("D5").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà NGUYỄN THỊ A và Ông JOHN SMITH "
            + "(phiếu đề xuất, giấy khai sinh)";
        ml.Cell("D5").Style.Font.Bold = true;
        ml.Cell("D5").CreateComment().AddText("Giữ định dạng ô");

        ml.Cell("B6").Value = 37;
        ml.Cell("E6").Value = new DateTime(1990, 1, 24);
        ml.Cell("D6").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà TRẦN THỊ B và Ông NGUYỄN VĂN C "
            + "(NGUYỄN ĐỨC THÁI)";

        ml.Cell("B7").Value = 38;
        ml.Cell("E7").Value = new DateTime(1990, 1, 25);
        ml.Cell("D7").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà LÊ THỊ D và Ông PIERRE DUPONT";

        ml.Cell("B8").Value = 39;
        ml.Cell("E8").Value = new DateTime(1990, 2, 1);
        ml.Cell("D8").Value =
            "Hồ sơ về việc cho phép công dân được thay đổi tên và khai sinh. "
            + "(Từ số 01 - 20). Năm 1990";

        var form = workbook.AddWorksheet("form");
        form.Cell("A1").Value = "Tra cứu";
        form.Cell("B1").FormulaA1 = "=10+20";
        form.Cell("C1").Value = "=Không phải công thức";

        workbook.SaveAs(path);
        return path;
    }

    private static void AssertSharedStringsUnchanged(string source, string output)
    {
        Assert.Equal(
            ReadEntry(source, "xl/sharedStrings.xml"),
            ReadEntry(output, "xl/sharedStrings.xml"));
    }

    private static void AssertOnlyWorkbookAndSheetChanged(string source, string output)
    {
        using var sourceArchive = ZipFile.OpenRead(source);
        using var outputArchive = ZipFile.OpenRead(output);

        Assert.Equal(
            sourceArchive.Entries.Select(entry => entry.FullName).OrderBy(name => name),
            outputArchive.Entries.Select(entry => entry.FullName).OrderBy(name => name));

        foreach (var sourceEntry in sourceArchive.Entries)
        {
            if (sourceEntry.FullName is "xl/workbook.xml" or "xl/worksheets/sheet1.xml")
            {
                continue;
            }

            Assert.Equal(
                ReadEntryBytes(sourceEntry),
                ReadEntryBytes(outputArchive.GetEntry(sourceEntry.FullName)!));
        }
    }

    private static void AssertWorkbookRequestsFullCalculation(string output)
    {
        var workbookXml = ReadEntry(output, "xl/workbook.xml");
        Assert.Contains("fullCalcOnLoad=\"1\"", workbookXml);
        Assert.Contains("forceFullCalc=\"1\"", workbookXml);
    }

    private static string ReadEntry(string path, string entryName)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
