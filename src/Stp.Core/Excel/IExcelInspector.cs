namespace Stp.Core.Excel;

/// <summary>
/// Đọc cấu trúc workbook bằng ClosedXML, không phụ thuộc Microsoft Excel.
/// </summary>
public interface IExcelInspector
{
    /// <summary>Nhận diện loại file, số năm/hồ sơ và tình trạng macro.</summary>
    WorkbookInspection Inspect(string filePath);

    /// <summary>Đọc đầy đủ dữ liệu ML để lưu DB hoặc đồng bộ.</summary>
    ArchiveSnapshot Read(string filePath);
}
