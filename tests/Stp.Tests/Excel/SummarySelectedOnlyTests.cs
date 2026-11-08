using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

/// <summary>
/// Kiểm chứng: khi chỉ định OnlyRowNumbers, preview/export chỉ xử lý đúng các dòng đã chọn,
/// không chuẩn hoá/sửa các dòng ngoài danh sách (tránh xuất thừa).
/// </summary>
public sealed class SummarySelectedOnlyTests
{
    private const string StandardPrefix =
        "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn với người nước ngoài";

    [Fact]
    public async Task Preview_WithOnlyRowNumbers_ExcludesOtherNormalizableRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenAsync(service, path);

            // Chỉ chọn dòng 2 (một dòng cần chuẩn hoá) — không có sửa tay.
            var preview = service.Preview(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                onlyRowNumbers: new[] { 2 });

            var row = Assert.Single(preview);
            Assert.Equal(2, row.RowNumber);
            Assert.StartsWith(StandardPrefix, row.Summary, StringComparison.Ordinal);

            // Không truyền OnlyRowNumbers → ra cả 3 dòng cần chuẩn hoá (hành vi cũ).
            var all = service.Preview(session.SessionId, Array.Empty<SummaryChange>());
            Assert.Equal(3, all.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_WithOnlyRowNumbers_OnlyWritesSelectedRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenAsync(service, path);

            var changes = new List<SummaryChange>
            {
                new(2, Array.Empty<string>(), SheetNumber: "21-40")
            };

            var result = await service.ExportAsync(
                session.SessionId,
                changes,
                outputFileName: "selected-only.xlsx",
                onlyRowNumbers: new[] { 2 });

            Assert.Equal("selected-only.xlsx", result.FileName);
            Assert.Equal(1, result.ChangedRows);
            Assert.Equal(1, result.NormalizedRows);

            var output = Path.Combine(directory, "selected-only-out.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            var ml = workbook.Worksheet("ML");

            // Dòng 2 được sửa: trích yếu chuẩn hoá + vá số tờ.
            Assert.Equal(
                StandardPrefix + " của Bà NGUYỄN THỊ A và Ông JOHN SMITH",
                ml.Cell("D2").GetString());
            Assert.Equal("21-40", ml.Cell("G2").GetString());

            // Các dòng khác NGUYÊN VẸN (không bị chuẩn hoá dù chúng cũng sai tiền tố).
            Assert.Equal(
                "Hồ sơ đăng ký kết hôn của Bà TRẦN THỊ B và Ông PIERRE DUPONT",
                ml.Cell("D3").GetString());
            Assert.Equal(
                "Quyết định về việc cho phép công nhân Việt Nam đăng ký kết hôn với người nước ngoài của Bà LÊ THỊ C và Ông MARIO ROSSI",
                ml.Cell("D4").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<SummaryWorkbook> OpenAsync(SummaryToolService service, string path)
    {
        await using var stream = File.OpenRead(path);
        return await service.CreateSessionAsync(stream, Path.GetFileName(path));
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
        var path = Path.Combine(directory, "selected-only.xlsx");

        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");
        ml.Cell("A1").Value = "Hộp số";
        ml.Cell("C1").Value = "Hồ sơ số";
        ml.Cell("D1").Value = "Trích yếu nội dung";
        ml.Cell("E1").Value = "Thời gian tài liệu";
        ml.Cell("F1").Value = "Số tờ";
        ml.Cell("G1").Value = "Ghi chú";

        ml.Cell("B2").Value = 1;
        ml.Cell("C2").Value = "HS-01";
        ml.Cell("D2").Value = "Hồ sơ về việc đăng ký kết hôn của Bà NGUYỄN THỊ A và Ông JOHN SMITH";
        ml.Cell("E2").Value = new DateTime(1990, 1, 10);
        ml.Cell("F2").Value = "93/QĐ.UBT";
        ml.Cell("G2").Value = "01-20";

        ml.Cell("B3").Value = 2;
        ml.Cell("C3").Value = "HS-02";
        ml.Cell("D3").Value = "Hồ sơ đăng ký kết hôn của Bà TRẦN THỊ B và Ông PIERRE DUPONT";
        ml.Cell("E3").Value = new DateTime(1990, 2, 10);
        ml.Cell("F3").Value = "94/QĐ.UBT";

        ml.Cell("B4").Value = 3;
        ml.Cell("C4").Value = "HS-03";
        ml.Cell("D4").Value =
            "Quyết định về việc cho phép công nhân Việt Nam đăng ký kết hôn với người nước ngoài của Bà LÊ THỊ C và Ông MARIO ROSSI";
        ml.Cell("E4").Value = new DateTime(1991, 1, 10);
        ml.Cell("F4").Value = "95/QĐ.UBT";
        ml.Cell("G4").Value = "21-40";

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-selected-only-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
