using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

/// <summary>
/// Cột ghi chú (G) ở sheet "file nhập" chứa serial ngày Excel từ "01-24"
/// (45292 = 01/01/2024). Số tờ phải hiển thị dạng "01-24", không phải serial.
/// </summary>
public sealed class SheetNumberFormatTests
{
    [Fact]
    public async Task Read_DecodesExcelSerialSheetNumbers()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);

            await using var stream = File.OpenRead(source);
            var session = await service.CreateSessionAsync(stream, Path.GetFileName(source));

            var first = session.Rows.Single(row => row.RowNumber == 2);
            Assert.Equal("01-24", first.SheetNumber);

            var second = session.Rows.Single(row => row.RowNumber == 3);
            Assert.Equal("01-20", second.SheetNumber);
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

    private static string CreateWorkbook(string directory)
    {
        var path = Path.Combine(directory, "sheet-number.xlsx");

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("ML");
        sheet.Cell("A1").Value = "Hộp số";
        sheet.Cell("C1").Value = "Hồ sơ số";
        sheet.Cell("D1").Value = "Trích yếu nội dung";
        sheet.Cell("E1").Value = "Thời gian tài liệu";
        sheet.Cell("F1").Value = "Số tờ";
        sheet.Cell("G1").Value = "Ghi chú";

        sheet.Cell("B2").Value = 85;
        sheet.Cell("C2").Value = 1;
        sheet.Cell("D2").Value = "Hồ sơ về việc đăng ký kết hôn. Năm 1991";
        sheet.Cell("E2").Value = new DateTime(1991, 3, 4);
        sheet.Cell("F2").Value = "50/QD.UBT";
        sheet.Cell("G2").Value = 45292;

        sheet.Cell("B3").Value = 36;
        sheet.Cell("C3").Value = 1;
        sheet.Cell("D3").Value = "Hồ sơ về việc đăng ký kết hôn. Năm 1990";
        sheet.Cell("E3").Value = new DateTime(1990, 1, 20);
        sheet.Cell("F3").Value = "93/QD.UBT";
        sheet.Cell("G3").Value = 43831;

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "stp-sheet-number",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
