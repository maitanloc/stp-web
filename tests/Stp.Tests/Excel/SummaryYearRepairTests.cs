using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

/// <summary>
/// Các hồi quy cho dữ liệu bẩn thường gặp: năm bị gõ sai trong cột ngày, hồ sơ
/// tổng hợp "HS ..." thiếu số ký hiệu nhưng có khoảng trang trong trích yếu, và
/// danh mục giấy tờ không dấu.
/// </summary>
public sealed class SummaryYearRepairTests
{
    [Fact]
    public async Task Read_RepairsBadDateYearFromSummaryAndExtractsAggregateSheetNumber()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            // Năm gõ sai 2201 nhưng trích yếu ghi "Năm 2001".
            var repaired = session.Rows.Single(row => row.RowNumber == 2);
            Assert.Equal(2001, repaired.Year);
            Assert.Equal("05/02/2001", repaired.DocumentDate);
            Assert.DoesNotContain(session.Years, year => year.Year == 2201);

            // Hồ sơ tổng hợp "HS 36": lấy khoảng trang từ "(Từ số 01 - 20)".
            var aggregate = session.Rows.Single(row => row.RowNumber == 3);
            Assert.Equal(1990, aggregate.Year);
            Assert.Equal("01-20", aggregate.SheetNumber);

            // Dòng thường vẫn giữ khoảng trang ở cột G.
            var normal = session.Rows.Single(row => row.RowNumber == 4);
            Assert.Equal(1990, normal.Year);
            Assert.Equal("21-40", normal.SheetNumber);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewPrint_DoesNotRequireDocumentCodeForAggregateRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            var preview = service.PreviewPrint(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                new[] { 1990 });

            var status = Assert.Single(preview.Years);
            Assert.Equal(2, status.Total);
            Assert.Equal(1, status.Ready);
            // Chỉ dòng thường thiếu số ký hiệu; hồ sơ tổng hợp không bị tính.
            Assert.Equal(1, status.MissingDocumentCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Read_CanonicalizesUnaccentedCatalogEntries()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            Assert.Contains("phiếu đề xuất", session.Catalog);
            Assert.Contains("hộ chiếu", session.Catalog);
            Assert.DoesNotContain("phieu de xuat", session.Catalog);
            Assert.DoesNotContain("ho chieu", session.Catalog);
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
        var path = Path.Combine(directory, "year-repair.xlsx");

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("ML");
        sheet.Cell("A1").Value = "Hộp số";
        sheet.Cell("C1").Value = "Hồ sơ số";
        sheet.Cell("D1").Value = "Trích yếu nội dung";
        sheet.Cell("E1").Value = "Thời gian tài liệu";
        sheet.Cell("F1").Value = "Số tờ";
        sheet.Cell("G1").Value = "Ghi chú";

        // Danh mục giấy tờ không dấu trong file nguồn.
        sheet.Cell("S1").Value = "phieu de xuat";
        sheet.Cell("T1").Value = "ho chieu";

        // Dòng 2: năm gõ sai 2201, trích yếu ghi đúng Năm 2001.
        sheet.Cell("B2").Value = 1;
        sheet.Cell("C2").Value = "31";
        sheet.Cell("D2").Value =
            "Hồ sơ về việc đăng ký kết hôn của Bà NGUYỄN THỊ A và Ông JOHN SMITH. Năm 2001";
        sheet.Cell("E2").Value = "05/02/2201";
        sheet.Cell("F2").Value = "176/UBT";
        sheet.Cell("G2").Value = "01-33";

        // Dòng 3: hồ sơ tổng hợp, không có số ký hiệu, khoảng trang nằm trong trích yếu.
        sheet.Cell("B3").Value = 2;
        sheet.Cell("C3").Value = "HS 36";
        sheet.Cell("D3").Value =
            "Hồ sơ về việc cho phép công dân được thay đổi tên và khai sinh. (Từ số 01 - 20). Năm 1990";
        sheet.Cell("E3").Value = "06/01/1990-22/01/1990";

        // Dòng 4: dòng thường thiếu số ký hiệu để kiểm tra cảnh báo vẫn còn.
        sheet.Cell("B4").Value = 3;
        sheet.Cell("C4").Value = "32";
        sheet.Cell("D4").Value = "Cấp giấy kết hôn của Bà TRẦN THỊ B";
        sheet.Cell("E4").Value = new DateTime(1990, 2, 10);
        sheet.Cell("G4").Value = "21-40";

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "stp-year-repair",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
