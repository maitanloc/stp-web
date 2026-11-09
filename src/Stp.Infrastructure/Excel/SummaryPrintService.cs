using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Stp.Core.Excel;
using Stp.Core.Summary;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Dựng bản in theo năm từ dữ liệu trích yếu đã hoàn thiện:
/// workbook ML trung gian → <see cref="IPrintSheetGenerator"/> sinh sheet
/// <c>BANG_IN_HANG_LOAT</c> → <see cref="VbaProjectInjector"/> chèn macro lọc/in.
/// </summary>
public sealed class SummaryPrintService : ISummaryPrintService
{
    private const string MacroContentType =
        "application/vnd.ms-excel.sheet.macroEnabled.12";
    private const string ZipContentType = "application/zip";

    private static readonly string[] Headers =
    {
        "Hộp số",
        "Số hồ sơ",
        "Số phụ",
        "Trích yếu nội dung",
        "Ngày",
        "Số ký hiệu",
        "Tờ số",
        "Ghi chú",
        "Trích yếu gốc"
    };

    private readonly IPrintSheetGenerator _generator;
    private readonly VbaProjectInjector _injector;
    private readonly ILogger<SummaryPrintService> _logger;

    public SummaryPrintService(
        IPrintSheetGenerator generator,
        VbaProjectInjector injector,
        ILogger<SummaryPrintService> logger)
    {
        _generator = generator;
        _injector = injector;
        _logger = logger;
    }

    public Task<SummaryPrintBuildResult> BuildAsync(
        SummaryPrintBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Rows.Count == 0)
        {
            throw new InvalidOperationException("Không có dòng nào để dựng bản in.");
        }

        // ClosedXML và đóng gói ZIP là tác vụ đồng bộ; chạy ngoài luồng request.
        return Task.Run(() => Build(request, cancellationToken), cancellationToken);
    }

    private SummaryPrintBuildResult Build(
        SummaryPrintBuildRequest request,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-print",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            return request.Mode == SummaryPrintOutputMode.Combined
                ? BuildCombined(request, root, cancellationToken)
                : BuildPerYear(request, root, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private SummaryPrintBuildResult BuildCombined(
        SummaryPrintBuildRequest request,
        string root,
        CancellationToken cancellationToken)
    {
        var rows = OrderRows(request.Rows);
        var years = rows.Select(row => row.Year).Distinct().OrderBy(year => year).ToList();
        var fileName = SanitizeFileName(request.BaseFileName) + ".xlsm";

        var single = BuildSingle(
            root,
            fileName,
            rows,
            years,
            cancellationToken);

        return single with { FileName = fileName };
    }

    private SummaryPrintBuildResult BuildPerYear(
        SummaryPrintBuildRequest request,
        string root,
        CancellationToken cancellationToken)
    {
        var groups = OrderRows(request.Rows)
            .GroupBy(row => row.Year)
            .OrderBy(group => group.Key)
            .ToList();

        var files = new List<(string Name, byte[] Content)>();
        var warnings = new List<string>();
        var years = new List<int>();

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = SanitizeFileName(request.BaseFileName)
                + "_NAM_"
                + group.Key.ToString(CultureInfo.InvariantCulture)
                + ".xlsm";

            var built = BuildSingle(
                root,
                fileName,
                group.ToList(),
                new[] { group.Key },
                cancellationToken);

            files.Add((fileName, built.Content));
            years.Add(group.Key);
            warnings.AddRange(built.Warnings);
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("Không có năm nào để dựng bản in.");
        }

        var zipName = SanitizeFileName(request.BaseFileName) + "_theo_nam.zip";
        var content = CreateZip(files);
        var rowCount = groups.Sum(group => group.Count());

        _logger.LogInformation(
            "Đóng gói bản in theo năm: {Years} file .xlsm, {Rows} hồ sơ, {Bytes} byte.",
            files.Count,
            rowCount,
            content.Length);

        return new SummaryPrintBuildResult(
            content,
            zipName,
            ZipContentType,
            years,
            rowCount,
            warnings);
    }

    private SummaryPrintBuildResult BuildSingle(
        string root,
        string fileName,
        IReadOnlyList<SummaryPrintRow> rows,
        IReadOnlyList<int> years,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var mlPath = Path.Combine(folder, "ml.xlsx");
        var printPath = Path.Combine(folder, "print.xlsx");
        var finalPath = Path.Combine(folder, fileName);

        WriteMlWorkbook(mlPath, rows);

        var generation = _generator.Generate(
            new PrintGenerationRequest
            {
                InputPath = mlPath,
                OutputPath = printPath,
                Overwrite = true
            },
            cancellationToken);

        if (!generation.Success)
        {
            throw new InvalidOperationException(
                generation.Warnings.Count > 0
                    ? string.Join(" ", generation.Warnings)
                    : "Không sinh được sheet in cho bản in.");
        }

        if (!generation.Generated)
        {
            throw new InvalidOperationException(
                "Không sinh được sheet in năm nào. Kiểm tra lại dữ liệu năm của hồ sơ.");
        }

        File.Copy(printPath, finalPath, overwrite: true);
        var injection = _injector.Inject(finalPath, cancellationToken);

        var warnings = generation.Warnings
            .Concat(injection.Warnings)
            .ToList();

        if (generation.GeneratedYears.Count != years.Count)
        {
            warnings.Add(
                "Số sheet in sinh ra không khớp số năm yêu cầu: "
                + $"{generation.GeneratedYears.Count}/{years.Count}.");
        }

        var content = File.ReadAllBytes(finalPath);

        return new SummaryPrintBuildResult(
            content,
            fileName,
            MacroContentType,
            generation.GeneratedYears.Count > 0
                ? generation.GeneratedYears.OrderBy(year => year).ToList()
                : years,
            rows.Count,
            warnings);
    }

    private static void WriteMlWorkbook(
        string path,
        IReadOnlyList<SummaryPrintRow> rows)
    {
        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");

        for (var column = 1; column <= Headers.Length; column++)
        {
            ml.Cell(1, column).Value = Headers[column - 1];
        }

        var header = ml.Range(1, 1, 1, Headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
        header.Style.Font.FontColor = XLColor.FromHtml("#1F3864");
        header.Style.Alignment.WrapText = true;

        var row = 2;
        foreach (var group in OrderRows(rows).GroupBy(item => item.Year).OrderBy(item => item.Key))
        {
            var yearCell = ml.Cell(row, 4);
            yearCell.Value = $"NĂM {group.Key}";
            yearCell.Style.Font.Bold = true;
            yearCell.Style.Font.FontColor = XLColor.FromHtml("#1F3864");
            yearCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
            row++;

            foreach (var item in group)
            {
                ml.Cell(row, 1).Value = string.Empty;
                ml.Cell(row, 2).Value = item.NumberLabel;
                ml.Cell(row, 3).Value = item.DocumentNumber;
                ml.Cell(row, 4).Value = item.Summary;
                WriteDate(ml.Cell(row, 5), item.Date);
                ml.Cell(row, 6).Value = item.DocumentCode;
                ml.Cell(row, 7).Value = item.SheetNumber;
                ml.Cell(row, 8).Value = item.Note;
                ml.Cell(row, 9).Value = item.OriginalSummary;
                row++;
            }
        }

        ml.Column(1).Width = 10;
        ml.Column(2).Width = 12;
        ml.Column(3).Width = 10;
        ml.Column(4).Width = 60;
        ml.Column(5).Width = 14;
        ml.Column(6).Width = 16;
        ml.Column(7).Width = 10;
        ml.Column(8).Width = 18;
        ml.Column(9).Width = 40;

        if (row > 2)
        {
            var data = ml.Range(2, 1, row - 1, Headers.Length);
            data.Style.Alignment.WrapText = true;
            data.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        ml.SheetView.FreezeRows(1);
        ml.SetTabActive();
        workbook.SaveAs(path);
    }

    private static void WriteDate(IXLCell cell, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            cell.Value = string.Empty;
            return;
        }

        if (DateTime.TryParseExact(
                value,
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact)
            || DateTime.TryParse(
                value,
                CultureInfo.GetCultureInfo("vi-VN"),
                DateTimeStyles.None,
                out exact)
            || DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out exact))
        {
            cell.Value = exact.Date;
            cell.Style.NumberFormat.Format = "dd/mm/yyyy";
            return;
        }

        cell.Value = value;
    }

    private static List<SummaryPrintRow> OrderRows(IReadOnlyList<SummaryPrintRow> rows)
    {
        return rows
            .OrderBy(row => row.Year)
            .ThenBy(row => ParseSortNumber(row.NumberLabel))
            .ThenBy(row => row.NumberLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static int ParseSortNumber(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+");
        return match.Success
               && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;
    }

    private static byte[] CreateZip(IReadOnlyList<(string Name, byte[] Content)> files)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(file.Content, 0, file.Content.Length);
            }
        }

        return stream.ToArray();
    }

    private static string SanitizeFileName(string value)
    {
        var name = Regex.Replace(value ?? string.Empty, @"[\\/:*?""<>|]", "_").Trim();
        return name.Length == 0 ? "ban_in" : name;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Bỏ qua thư mục tạm không xoá được.
        }
    }
}
