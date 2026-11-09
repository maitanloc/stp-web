using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Stp.Infrastructure.Excel;

namespace Stp.Tests.Excel;

/// <summary>
/// File xuất lọc chỉ giữ sheet "file nhập" với hàng tiêu đề ở dòng 6.
/// Inspector đồng bộ Kho phải nhận diện sheet này
/// (trước đây báo "không có sheet ML" và sync trả 422).
/// </summary>
public sealed class MlSheetDetectionTests
{
    [Fact]
    public void Inspect_DetectsFileNhapWithHeaderAtRow6()
    {
        var directory = Path.Combine(Path.GetTempPath(), "stp-ml-detect", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "filtered.xlsx");
            using (var created = new XLWorkbook())
            {
                var sheet = created.AddWorksheet("file nhập");
                // Menu/tiêu đề nằm ở dòng 1-5; hàng tiêu đề thật ở dòng 6.
                sheet.Cell("A1").Value = "ĐẢNG BỘ THÀNH PHỐ ĐỒNG NAI";
                sheet.Cell("A6").Value = "Hộp số";
                sheet.Cell("C6").Value = "Hồ sơ số";
                sheet.Cell("D6").Value = "Trích yếu nội dung";
                sheet.Cell("E6").Value = "Thời gian tài liệu";
                sheet.Cell("F6").Value = "Số tờ";
                sheet.Cell("G6").Value = "Ghi chú";
                sheet.Cell("D8").Value = "* Dang ky ket hon";
                sheet.Cell("A9").Value = 88;
                sheet.Cell("B9").Value = 775;
                sheet.Cell("C9").Value = 160;
                sheet.Cell("D9").Value = "Hồ sơ về việc đăng ký kết hôn. Năm 1994";
                sheet.Cell("E9").Value = new DateTime(1994, 7, 29);
                sheet.Cell("F9").Value = "1763/QD.UBT";
                sheet.Cell("G9").Value = 10959;
                created.SaveAs(path);
            }

            var inspector = new ExcelInspector(NullLogger<ExcelInspector>.Instance);
            var inspection = inspector.Inspect(path);
            Assert.True(inspection.HasMlSheet);
            Assert.Equal("file nhập", inspection.MlSheetName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
