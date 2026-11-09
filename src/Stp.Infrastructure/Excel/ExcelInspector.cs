using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Stp.Core.Domain;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

public sealed class ExcelInspector : IExcelInspector
{
    private readonly ILogger<ExcelInspector> _logger;

    public ExcelInspector(ILogger<ExcelInspector> logger)
    {
        _logger = logger;
    }

    public WorkbookInspection Inspect(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Không tìm thấy file Excel.", filePath);
        }

        EnsureSupportedExtension(filePath);

        using var workbook = new XLWorkbook(filePath);
        var mlSheet = ExcelArchiveParser.FindMlSheet(workbook);
        var printSheets = workbook.Worksheets
            .Where(ExcelArchiveParser.IsPrintSheet)
            .ToList();

        var kind = mlSheet is null
            ? WorkbookKind.Other
            : printSheets.Count > 0
                ? WorkbookKind.StandardWithPrintSheets
                : WorkbookKind.MlOnly;

        var snapshot = ExcelArchiveParser.ReadSnapshot(
            workbook,
            Path.GetFileName(filePath),
            kind);

        var hasMacro = ExcelArchiveParser.HasVbaProject(filePath);
        var sheets = workbook.Worksheets
            .Select(ws => InspectSheet(ws, mlSheet?.Name))
            .ToList();

        var warnings = new List<string>();
        if (mlSheet is null)
        {
            warnings.Add("Không tìm thấy sheet dữ liệu (ML/file nhập). File này chưa thuộc cấu trúc chuẩn của STP.");
        }

        if (kind == WorkbookKind.MlOnly)
        {
            warnings.Add("File chỉ có sheet dữ liệu, chưa có các sheet in năm. Hệ thống sẽ tự sinh sheet in từ dữ liệu.");
        }

        if (hasMacro)
        {
            warnings.Add("File đã có macro. Hệ thống sẽ cập nhật macro STP vào ThisWorkbook và giữ các module khác.");
        }

        if (string.Equals(Path.GetExtension(filePath), ".xls", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Định dạng .xls không được hỗ trợ trực tiếp. Hãy lưu file sang .xlsx trước.");
        }

        var yearInspections = snapshot.Years
            .Select(y => new YearInspection
            {
                Year = y.Year,
                SheetName = y.SheetName,
                DossierCount = y.Dossiers.Count,
                MinNumber = y.Dossiers.Count == 0 ? 0 : y.Dossiers.Min(d => d.Number),
                MaxNumber = y.Dossiers.Count == 0 ? 0 : y.Dossiers.Max(d => d.Number),
                HasPrintSheet = printSheets.Any(ps =>
                    ExcelArchiveParser.ParseYear(ps.Name) == y.Year),
                SourceSheets = y.SourceSheets
            })
            .ToList();

        _logger.LogInformation(
            "Inspect {File}: loại {Kind}, {Sheets} sheet, {Years} năm, {Dossiers} hồ sơ, macro={HasMacro}",
            fileInfo.Name,
            kind,
            workbook.Worksheets.Count,
            snapshot.Years.Count,
            snapshot.TotalDossiers,
            hasMacro);

        return new WorkbookInspection
        {
            FilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            Kind = kind,
            HasMlSheet = mlSheet is not null,
            MlSheetName = mlSheet?.Name,
            HasPrintSheets = printSheets.Count > 0,
            HasMacro = hasMacro,
            SheetCount = workbook.Worksheets.Count,
            YearCount = snapshot.Years.Count,
            DossierCount = snapshot.TotalDossiers,
            PrintReadyCount = snapshot.TotalPrintReady,
            TotalPages = snapshot.TotalPages,
            Types = snapshot.Years
                .SelectMany(y => y.Dossiers)
                .Select(d => d.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(DossierTypes.Order)
                .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Years = yearInspections,
            Sheets = sheets,
            Warnings = warnings
        };
    }

    public ArchiveSnapshot Read(string filePath)
    {
        EnsureSupportedExtension(filePath);
        using var workbook = new XLWorkbook(filePath);

        var mlSheet = ExcelArchiveParser.FindMlSheet(workbook);
        var hasPrintSheets = workbook.Worksheets.Any(ExcelArchiveParser.IsPrintSheet);
        var kind = mlSheet is null
            ? WorkbookKind.Other
            : hasPrintSheets
                ? WorkbookKind.StandardWithPrintSheets
                : WorkbookKind.MlOnly;

        return ExcelArchiveParser.ReadSnapshot(workbook, Path.GetFileName(filePath), kind);
    }

    private static SheetInspection InspectSheet(IXLWorksheet worksheet, string? mlSheetName)
    {
        var isMl = string.Equals(worksheet.Name, mlSheetName, StringComparison.OrdinalIgnoreCase);
        var isPrint = ExcelArchiveParser.IsPrintSheet(worksheet);
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

        return new SheetInspection
        {
            Name = worksheet.Name,
            IsMl = isMl,
            IsPrint = isPrint,
            Year = isPrint ? ExcelArchiveParser.ParseYear(worksheet.Name) : null,
            LastRow = lastRow,
            LastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0,
            BlockCount = isPrint && lastRow >= 11 ? (lastRow - 8) / 9 + 1 : 0
        };
    }

    private static void EnsureSupportedExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new NotSupportedException(
            $"Định dạng '{extension}' chưa được hỗ trợ. Hãy lưu workbook sang .xlsx hoặc .xlsm.");
    }
}
