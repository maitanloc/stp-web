using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

/// <summary>
/// Luật chọn sheet khi import (11/11/2026): dữ liệu 1990–1995 nằm ở sheet
/// "file nhập"; từ 1996 trở đi lấy sheet mang tên năm ("1997", "1995ok"…),
/// KHÔNG lấy "file nhập" cho các năm đã có sheet năm riêng.
/// </summary>
public sealed class SummaryMultiSheetImportTests
{
    [Fact]
    public async Task Parse_CombinesInputSheetAndYearSheetsByLaw()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateMultiSheetWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            // Sheet năm → ghép nhiều sheet; "file dán" và "form" bị loại.
            Assert.Equal(3, session.Sheets.Count);
            Assert.Equal("file nhập", session.Sheets[0].Name);
            Assert.Null(session.Sheets[0].Year);
            Assert.Equal(5, session.Sheets[0].RowCount);
            Assert.Equal("1995ok", session.Sheets[1].Name);
            Assert.Equal(1995, session.Sheets[1].Year);
            Assert.Equal("1997", session.Sheets[2].Name);
            Assert.Equal(1997, session.Sheets[2].Year);
            Assert.DoesNotContain(session.Sheets, sheet => sheet.Name is "file dán" or "form");

            // Dòng giữ lại của file nhập đứng trước, rồi các sheet năm theo năm.
            Assert.Equal(7, session.Rows.Count);
            AssertYears(session, new[] { 1990, 1992, 1994, 1995, 1997, 1998 });

            var row1 = session.Rows.Single(row => row.RowNumber == 1);
            Assert.Equal("file nhập", row1.SheetName);
            Assert.Equal(2, row1.SourceRowNumber);
            Assert.Null(row1.Year);

            // 1990–1994 + null giữ nguyên trong file nhập.
            AssertRow(session, 2, "file nhập", 3, 1990);
            AssertRow(session, 3, "file nhập", 4, 1992);

            // Năm ≥1996 KHÔNG có sheet riêng vẫn lấy từ file nhập (1998).
            AssertRow(session, 4, "file nhập", 7, 1998);
            AssertRow(session, 5, "file nhập", 8, 1994);

            // 1995 bị "1995ok" phủ → dòng 1995 trong file nhập bị loại;
            // dòng 1995 duy nhất đến từ sheet 1995ok với năm bị ép theo sheet.
            AssertRow(session, 6, "1995ok", 2, 1995);

            // 1997 trong file nhập bị loại khi có sheet "1997".
            AssertRow(session, 7, "1997", 2, 1997);
            Assert.DoesNotContain(
                session.Rows,
                row => row.SheetName == "file nhập" && row.Year is 1995 or 1997);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_WritesEachPatchToItsOwnSheet()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateMultiSheetWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var row1 = session.Rows.Single(row => row.RowNumber == 1); // file nhập, dòng 2
            var row7 = session.Rows.Single(row => row.RowNumber == 7); // sheet 1997, dòng 2
            var changes = new List<SummaryChange>
            {
                new(row1.RowNumber, new[] { "phiếu đề xuất", "giấy khai sinh" }),
                new(row7.RowNumber, new[] { "hộ chiếu", "phiếu đề xuất" })
            };

            var result = await service.ExportAsync(session.SessionId, changes);
            Assert.Equal(2, result.ChangedRows);

            var output = Path.Combine(directory, "exported-multi.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            var input = workbook.Worksheet("file nhập");
            Assert.Equal(
                row1.BaseSummary + " (phiếu đề xuất, giấy khai sinh)",
                input.Cell("D2").GetString());
            Assert.Equal(
                "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
                + "với người nước ngoài của Bà TRẦN THỊ B và Ông NGUYỄN VĂN C",
                input.Cell("D3").GetString());

            var year1997 = workbook.Worksheet("1997");
            Assert.Equal(
                row7.BaseSummary + " (hộ chiếu, phiếu đề xuất)",
                year1997.Cell("D2").GetString());

            // Sheet không có thay đổi giữ nguyên.
            var year1995 = workbook.Worksheet("1995ok");
            Assert.Equal(
                "Trích yếu của sheet mười chín chín năm (phiếu đề xuất)",
                year1995.Cell("D2").GetString());

            // "file dán" và "form" vẫn còn nguyên trong bản xuất đầy đủ.
            Assert.Equal("FILE-DAN-JUNK", workbook.Worksheet("file dán").Cell("A1").GetString());
            Assert.Equal("Tra cứu", workbook.Worksheet("form").Cell("A1").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilteredExport_SpanningSheets_KeepsEachSheetWithScopedRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateMultiSheetWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var scope = new SummaryScope(YearFrom: 1994, YearTo: 1997);
            var row7 = session.Rows.Single(row => row.RowNumber == 7);
            var changes = new List<SummaryChange>
            {
                new(row7.RowNumber, new[] { "giấy khai sinh", "hộ chiếu" })
            };

            var result = await service.ExportFilteredAsync(session.SessionId, changes, scope);
            Assert.Equal(3, result.TotalRows);
            Assert.Equal(1, result.ChangedRows);

            var output = Path.Combine(directory, "filtered-multi.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            var names = workbook.Worksheets.Select(sheet => sheet.Name).ToList();
            Assert.Equal(new[] { "file nhập", "1995ok", "1997" }, names);

            // file nhập chỉ giữ dòng tiêu đề + dòng 1994 (dòng 8); các dòng
            // ngoài phạm vi bị xoá khỏi XML.
            var input = workbook.Worksheet("file nhập");
            Assert.Equal(
                "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
                + "với người nước ngoài của Bà VÕ THỊ E và Ông ADAM SMITH",
                input.Cell("D8").GetString());
            Assert.Equal(string.Empty, input.Cell("D2").GetString());
            Assert.Equal(string.Empty, input.Cell("D7").GetString());

            var year1995 = workbook.Worksheet("1995ok");
            Assert.Equal(
                "Trích yếu của sheet mười chín chín năm (phiếu đề xuất)",
                year1995.Cell("D2").GetString());

            var year1997 = workbook.Worksheet("1997");
            Assert.Equal(
                row7.BaseSummary + " (hộ chiếu, giấy khai sinh)",
                year1997.Cell("D2").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Parse_WorkbookWithoutYearSheets_KeepsLegacySingleSheetBehaviour()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateLegacyWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var sheet = Assert.Single(session.Sheets);
            Assert.Equal("ML", sheet.Name);
            Assert.Null(sheet.Year);
            Assert.Equal("ML", session.SheetName);

            var row = session.Rows.Single(item => item.RowNumber == 2);
            Assert.Equal("ML", row.SheetName);
            Assert.Equal(2, row.SourceRowNumber);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertYears(SummaryWorkbook session, int[] years)
    {
        Assert.Equal(years.Length, session.Years.Count);
        foreach (var year in years)
        {
            Assert.Equal(1, session.Years.Single(item => item.Year == year).Count);
        }
    }

    private static void AssertRow(
        SummaryWorkbook session,
        int rowNumber,
        string sheetName,
        int sourceRowNumber,
        int? year)
    {
        var row = session.Rows.Single(item => item.RowNumber == rowNumber);
        Assert.Equal(sheetName, row.SheetName);
        Assert.Equal(sourceRowNumber, row.SourceRowNumber);
        Assert.Equal(year, row.Year);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-multi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
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

    /// <summary>
    /// Workbook giống file thật: "file nhập" trộn nhiều năm, "file dán"/"form"
    /// rác, và các sheet năm "1995ok"/"1997" sạch.
    /// </summary>
    private static string CreateMultiSheetWorkbook(string directory)
    {
        var path = Path.Combine(directory, "multi.xlsx");

        using var workbook = new XLWorkbook();

        var input = workbook.AddWorksheet("file nhập");
        WriteHeaders(input);
        input.Cell("D2").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà NGUYỄN THỊ A và Ông JOHN SMITH";
        input.Cell("B2").Value = 10;
        input.Cell("D3").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà TRẦN THỊ B và Ông NGUYỄN VĂN C";
        input.Cell("B3").Value = 11;
        input.Cell("E3").Value = new DateTime(1990, 1, 15);
        input.Cell("D4").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà LÊ THỊ D và Ông PIERRE DUPONT";
        input.Cell("B4").Value = 12;
        input.Cell("E4").Value = new DateTime(1992, 2, 20);
        input.Cell("D5").Value = "Hồ sơ dòng rác bị loại vì có sheet năm riêng phủ";
        input.Cell("B5").Value = 13;
        input.Cell("E5").Value = new DateTime(1995, 3, 5);
        input.Cell("D6").Value = "Hồ sơ dòng rác năm mười chín chín bảy";
        input.Cell("B6").Value = 14;
        input.Cell("E6").Value = new DateTime(1997, 4, 7);
        input.Cell("D7").Value = "Hồ sơ năm không có sheet riêng vẫn giữ";
        input.Cell("B7").Value = 15;
        input.Cell("E7").Value = new DateTime(1998, 5, 9);
        input.Cell("D8").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà VÕ THỊ E và Ông ADAM SMITH";
        input.Cell("B8").Value = 16;
        input.Cell("E8").Value = new DateTime(1994, 6, 11);

        var paste = workbook.AddWorksheet("file dán");
        paste.Cell("A1").Value = "FILE-DAN-JUNK";
        paste.Cell("A2").Value = "CHI-TON-TAI-TRONG-SHEET-DAN";

        var form = workbook.AddWorksheet("form");
        form.Cell("A1").Value = "Tra cứu";

        var year1995 = workbook.AddWorksheet("1995ok");
        WriteHeaders(year1995);
        year1995.Cell("D2").Value = "Trích yếu của sheet mười chín chín năm (phiếu đề xuất)";
        year1995.Cell("B2").Value = 1;
        year1995.Cell("E2").Value = new DateTime(1995, 1, 12);

        var year1997 = workbook.AddWorksheet("1997");
        WriteHeaders(year1997);
        year1997.Cell("D2").Value = "Trích yếu của sheet năm riêng (hộ chiếu)";
        year1997.Cell("B2").Value = 1;
        year1997.Cell("E2").Value = new DateTime(1997, 2, 2);

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateLegacyWorkbook(string directory)
    {
        var path = Path.Combine(directory, "legacy.xlsx");

        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");
        WriteHeaders(ml);
        ml.Cell("D2").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà NGUYỄN THỊ A và Ông JOHN SMITH";
        ml.Cell("B2").Value = 10;

        var form = workbook.AddWorksheet("form");
        form.Cell("A1").Value = "Tra cứu";

        workbook.SaveAs(path);
        return path;
    }

    private static void WriteHeaders(IXLWorksheet sheet)
    {
        sheet.Cell("B1").Value = "Hộp số";
        sheet.Cell("C1").Value = "Hồ sơ số";
        sheet.Cell("D1").Value = "Trích yếu nội dung";
        sheet.Cell("E1").Value = "Thời gian tài liệu";
        sheet.Cell("F1").Value = "Số tờ";
        sheet.Cell("G1").Value = "Ghi chú";
    }
}
