using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

public sealed class SummaryNormalizationTests
{
    private const string StandardPrefix =
        "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn với người nước ngoài";

    [Fact]
    public async Task Inspect_DetectsSheetNumberColumnFromPageRangeData()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            Assert.Equal("D", session.SummaryColumn);
            Assert.Equal("C", session.DocumentNumberColumn);
            // Cột F có tiêu đề "Số tờ" nhưng dữ liệu là số quyết định, cột G mới chứa khoảng trang.
            Assert.Equal("G", session.SheetNumberColumn);

            Assert.Equal(3, session.MissingSheetNumberCount);
            Assert.Equal(1, session.MissingDocumentNumberCount);
            Assert.Equal(1, session.MissingNumberCount);
            Assert.Equal(4, session.NormalizableCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_NormalizesOnlyMarriageRegistrationRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var preview = service.Preview(session.SessionId, Array.Empty<SummaryChange>());
            var byRow = preview.ToDictionary(row => row.RowNumber);

            Assert.Equal(
                StandardPrefix + " của Bà NGUYỄN THỊ A và Ông JOHN SMITH",
                byRow[2].Summary);
            Assert.Equal(
                StandardPrefix + " của Bà TRẦN THỊ B và Ông PIERRE DUPONT",
                byRow[3].Summary);
            Assert.Equal(
                StandardPrefix + " của Bà LÊ THỊ C và Ông MARIO ROSSI",
                byRow[4].Summary);
            Assert.Equal(
                StandardPrefix + " của Bà PHẠM THỊ D và Ông KIM MIN SOO",
                byRow[5].Summary);

            // Các nhóm cấp giấy/ghi chú/trích lục không bị chạm tới.
            Assert.False(byRow.ContainsKey(6));
            Assert.False(byRow.ContainsKey(7));
            Assert.False(byRow.ContainsKey(8));

            // Dòng đã đúng chuẩn không xuất hiện trong bản xem trước.
            Assert.False(byRow.ContainsKey(9));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_NormalizesMarriageRowsAndPatchesMissingSheetNumber()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var row3 = session.Rows.Single(row => row.RowNumber == 3);
            Assert.Equal(string.Empty, row3.SheetNumber);

            var changes = new List<SummaryChange>
            {
                new(row3.RowNumber, Array.Empty<string>(), SheetNumber: "21-40")
            };

            var result = await service.ExportAsync(session.SessionId, changes);
            Assert.Equal(4, result.ChangedRows);
            Assert.Equal(4, result.NormalizedRows);

            var output = Path.Combine(directory, "normalized.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            var ml = workbook.Worksheet("ML");

            Assert.Equal(
                StandardPrefix + " của Bà NGUYỄN THỊ A và Ông JOHN SMITH",
                ml.Cell("D2").GetString());
            Assert.Equal(
                StandardPrefix + " của Bà TRẦN THỊ B và Ông PIERRE DUPONT",
                ml.Cell("D3").GetString());
            Assert.Equal(
                StandardPrefix + " của Bà LÊ THỊ C và Ông MARIO ROSSI",
                ml.Cell("D4").GetString());
            Assert.Equal(
                StandardPrefix + " của Bà PHẠM THỊ D và Ông KIM MIN SOO",
                ml.Cell("D5").GetString());
            Assert.Equal(
                StandardPrefix + " của Bà HOÀNG THỊ E và Ông LI WEI",
                ml.Cell("D9").GetString());

            // Số tờ được vá đúng cột G, không đụng cột F.
            Assert.Equal("21-40", ml.Cell("G3").GetString());
            Assert.Equal("94/QĐ.UBT", ml.Cell("F3").GetString());
            Assert.Equal("01-20", ml.Cell("G2").GetString());

            // Nhóm khác giữ nguyên từng ký tự.
            Assert.Equal("Cấp giấy kết hôn của Bà VŨ THỊ F", ml.Cell("D6").GetString());
            Assert.Equal("Ghi chú kết hôn của Bà ĐỖ THỊ G", ml.Cell("D7").GetString());
            Assert.Equal("Trích lục ghi chú kết hôn của Bà BÙI THỊ H", ml.Cell("D8").GetString());

            // Định dạng ô được giữ nguyên.
            Assert.True(ml.Cell("D2").Style.Font.Bold);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_SummaryTextOverrideWinsOverItems()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var row6 = session.Rows.Single(row => row.RowNumber == 6);
            var custom = "Cấp giấy kết hôn của Bà VŨ THỊ F (bản sao)";
            var changes = new List<SummaryChange>
            {
                new(row6.RowNumber, new[] { "hộ chiếu" }, SummaryText: custom)
            };

            var result = await service.ExportAsync(session.SessionId, changes);
            var output = Path.Combine(directory, "override.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            Assert.Equal(custom, workbook.Worksheet("ML").Cell("D6").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetRows_FiltersMissingSheetNumberAndSortsThemFirst()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var missing = service.GetRows(
                session.SessionId,
                new SummaryScope(OnlyMissingSheetNumber: true),
                1,
                20);
            Assert.Equal(3, missing.Total);
            Assert.All(missing.Items, row => Assert.Equal(string.Empty, row.SheetNumber));

            var sorted = service.GetRows(
                session.SessionId,
                new SummaryScope(MissingSheetNumberFirst: true),
                1,
                20);
            Assert.Equal(9, sorted.Total);
            Assert.All(
                sorted.Items.Take(3),
                row => Assert.Equal(string.Empty, row.SheetNumber));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetRows_FiltersByYearRangeAndSearchInSheetNumber()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var yearRange = service.GetRows(
                session.SessionId,
                new SummaryScope(YearFrom: 1990, YearTo: 1991),
                1,
                20);
            Assert.Equal(4, yearRange.Total);

            var search = service.GetRows(
                session.SessionId,
                new SummaryScope(Search: "01-20"),
                1,
                20);
            Assert.Equal(1, search.Total);
            Assert.Equal(2, search.Items[0].RowNumber);
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
        var path = Path.Combine(directory, "normalization.xlsx");

        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");
        ml.Cell("A1").Value = "Hộp số";
        ml.Cell("C1").Value = "Hồ sơ số";
        ml.Cell("D1").Value = "Trích yếu nội dung";
        ml.Cell("E1").Value = "Thời gian tài liệu";
        ml.Cell("F1").Value = "Số tờ";
        ml.Cell("G1").Value = "Ghi chú";

        ml.Cell("S1").Value = "phiếu đề xuất";
        ml.Cell("T1").Value = "hộ chiếu";

        // Dòng 2: tiền tố "Hồ sơ về việc đăng ký kết hôn", đã có số tờ.
        ml.Cell("B2").Value = 1;
        ml.Cell("C2").Value = "HS-01";
        ml.Cell("D2").Value = "Hồ sơ về việc đăng ký kết hôn của Bà NGUYỄN THỊ A và Ông JOHN SMITH";
        ml.Cell("D2").Style.Font.Bold = true;
        ml.Cell("E2").Value = new DateTime(1990, 1, 10);
        ml.Cell("F2").Value = "93/QĐ.UBT";
        ml.Cell("G2").Value = "01-20";

        // Dòng 3: tiền tố "Hồ sơ đăng ký kết hôn", thiếu số tờ.
        ml.Cell("B3").Value = 2;
        ml.Cell("C3").Value = "HS-02";
        ml.Cell("D3").Value = "Hồ sơ đăng ký kết hôn của Bà TRẦN THỊ B và Ông PIERRE DUPONT";
        ml.Cell("E3").Value = new DateTime(1990, 2, 10);
        ml.Cell("F3").Value = "94/QĐ.UBT";

        // Dòng 4: tiền tố cũ "công nhân", thiếu số tờ.
        ml.Cell("B4").Value = 3;
        ml.Cell("C4").Value = "HS-03";
        ml.Cell("D4").Value =
            "Quyết định về việc cho phép công nhân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà LÊ THỊ C và Ông MARIO ROSSI";
        ml.Cell("E4").Value = new DateTime(1991, 1, 10);
        ml.Cell("F4").Value = "95/QĐ.UBT";

        // Dòng 5: tiền tố bị thừa khoảng trắng, thiếu số tờ.
        ml.Cell("B5").Value = 4;
        ml.Cell("C5").Value = "HS-04";
        ml.Cell("D5").Value = "Hồ  sơ  về việc đăng ký kết hôn của Bà PHẠM THỊ D và Ông KIM MIN SOO";
        ml.Cell("E5").Value = new DateTime(1991, 2, 10);
        ml.Cell("F5").Value = "96/QĐ.UBT";

        // Dòng 6-8: các nhóm không được chuẩn hoá.
        ml.Cell("B6").Value = 5;
        ml.Cell("C6").Value = "HS-05";
        ml.Cell("D6").Value = "Cấp giấy kết hôn của Bà VŨ THỊ F";
        ml.Cell("E6").Value = new DateTime(1992, 1, 10);
        ml.Cell("G6").Value = "41-60";

        ml.Cell("B7").Value = 6;
        ml.Cell("C7").Value = "HS-06";
        ml.Cell("D7").Value = "Ghi chú kết hôn của Bà ĐỖ THỊ G";
        ml.Cell("E7").Value = new DateTime(1992, 2, 10);
        ml.Cell("G7").Value = "61-80";

        ml.Cell("B8").Value = 7;
        ml.Cell("C8").Value = "HS-07";
        ml.Cell("D8").Value = "Trích lục ghi chú kết hôn của Bà BÙI THỊ H";
        ml.Cell("E8").Value = new DateTime(1992, 3, 10);
        ml.Cell("G8").Value = "81-90";

        // Dòng 9: đã đúng chuẩn, thiếu hồ sơ số.
        ml.Cell("B9").Value = 8;
        ml.Cell("D9").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà HOÀNG THỊ E và Ông LI WEI";
        ml.Cell("E9").Value = new DateTime(1993, 1, 10);
        ml.Cell("G9").Value = "91-100";

        // Dòng 10: chỉ có hồ sơ số và số tờ, không có số hồ sơ/trích yếu.
        ml.Cell("C10").Value = "HS-10";
        ml.Cell("G10").Value = "101-120";

        var form = workbook.AddWorksheet("form");
        form.Cell("A1").Value = "Tra cứu";
        form.Cell("B1").FormulaA1 = "=10+20";

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-normalization-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
