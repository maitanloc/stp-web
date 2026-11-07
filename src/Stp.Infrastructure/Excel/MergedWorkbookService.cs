using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Stp.Core.Domain;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Gộp nhiều workbook ML thành một workbook mới. Dữ liệu ML được giữ nguyên
/// (kể cả hồ sơ trùng), thêm cột "Nguồn file"; sau đó sinh sheet in năm bằng
/// <see cref="IPrintSheetGenerator"/>.
/// </summary>
public sealed class MergedWorkbookService : IMergedWorkbookService
{
    private readonly IExcelInspector _inspector;
    private readonly IPrintSheetGenerator _printSheetGenerator;
    private readonly ILogger<MergedWorkbookService> _logger;

    public MergedWorkbookService(
        IExcelInspector inspector,
        IPrintSheetGenerator printSheetGenerator,
        ILogger<MergedWorkbookService> logger)
    {
        _inspector = inspector;
        _printSheetGenerator = printSheetGenerator;
        _logger = logger;
    }

    public MergeOutcome Merge(
        MergeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.InputPaths.Count == 0)
        {
            return Failure("Chưa có file nào để gộp.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return Failure("Thiếu đường dẫn file kết quả.");
        }

        var outputPath = Path.GetFullPath(request.OutputPath);
        if (!string.Equals(
                Path.GetExtension(outputPath),
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure("File gộp trung gian phải có đuôi .xlsx trước khi chuyển sang .xlsm.");
        }

        if (File.Exists(outputPath) && !request.Overwrite)
        {
            return Failure($"File kết quả đã tồn tại: {Path.GetFileName(outputPath)}");
        }

        var warnings = new List<string>();
        var snapshots = new List<(string SourceFileName, ArchiveSnapshot Snapshot)>();

        foreach (var inputPath in request.InputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(inputPath))
            {
                warnings.Add($"Bỏ qua file không tồn tại: {Path.GetFileName(inputPath)}");
                continue;
            }

            try
            {
                var snapshot = _inspector.Read(inputPath);
                if (snapshot.Kind == WorkbookKind.Other)
                {
                    warnings.Add(
                        $"Bỏ qua {Path.GetFileName(inputPath)}: không tìm thấy sheet ML.");
                    continue;
                }

                if (snapshot.TotalDossiers == 0)
                {
                    warnings.Add($"Bỏ qua {Path.GetFileName(inputPath)}: không có hồ sơ.");
                    continue;
                }

                snapshots.Add((Path.GetFileName(inputPath), snapshot));
            }
            catch (Exception ex)
            {
                warnings.Add($"Không đọc được {Path.GetFileName(inputPath)}: {ex.Message}");
            }
        }

        if (snapshots.Count == 0)
        {
            var message = warnings.Count > 0
                ? string.Join(" ", warnings)
                : "Không có file ML hợp lệ để gộp.";
            return new MergeOutcome
            {
                Success = false,
                OutputPath = outputPath,
                Warnings = warnings,
                ErrorMessage = message
            };
        }

        var groups = snapshots
            .SelectMany(snapshot => snapshot.Snapshot.Years.SelectMany(
                year => year.Dossiers.Select(dossier => new MergeEntry(
                    year.Year,
                    dossier,
                    snapshot.SourceFileName))))
            .GroupBy(entry => entry.Year)
            .OrderBy(group => group.Key)
            .ToList();

        var duplicateCount = groups.Sum(group =>
            group.GroupBy(entry => NormalizeDossierKey(entry.Dossier))
                .Sum(sameKey => Math.Max(0, sameKey.Count() - 1)));

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var temporaryPath = Path.Combine(
            outputDirectory ?? Path.GetTempPath(),
            $"~stp-merge-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteMergedWorkbook(temporaryPath, groups, snapshots, duplicateCount, cancellationToken);

            if (request.GeneratePrintSheets)
            {
                var generation = _printSheetGenerator.Generate(new PrintGenerationRequest
                {
                    InputPath = temporaryPath,
                    OutputPath = outputPath,
                    Overwrite = true
                }, cancellationToken);

                if (!generation.Success)
                {
                    return new MergeOutcome
                    {
                        Success = false,
                        OutputPath = outputPath,
                        FileCount = snapshots.Count,
                        YearCount = groups.Count,
                        DossierCount = groups.Sum(group => group.Count()),
                        DuplicateCount = duplicateCount,
                        Warnings = warnings,
                        ErrorMessage = generation.Warnings.Count > 0
                            ? string.Join(" ", generation.Warnings)
                            : "Không sinh được sheet in cho workbook gộp."
                    };
                }

                warnings.AddRange(generation.Warnings);
                TryDelete(temporaryPath);
            }
            else
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.Move(temporaryPath, outputPath);
            }

            var dossierCount = groups.Sum(group => group.Count());
            _logger.LogInformation(
                "Gộp {Files} file, {Years} năm, {Dossiers} hồ sơ, {Duplicates} trùng -> {Output}",
                snapshots.Count,
                groups.Count,
                dossierCount,
                duplicateCount,
                outputPath);

            return new MergeOutcome
            {
                Success = true,
                OutputPath = outputPath,
                FileCount = snapshots.Count,
                YearCount = groups.Count,
                DossierCount = dossierCount,
                DuplicateCount = duplicateCount,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không gộp được workbook vào {Output}", outputPath);
            return new MergeOutcome
            {
                Success = false,
                OutputPath = outputPath,
                FileCount = snapshots.Count,
                YearCount = groups.Count,
                DossierCount = groups.Sum(group => group.Count()),
                DuplicateCount = duplicateCount,
                Warnings = warnings,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void WriteMergedWorkbook(
        string path,
        IReadOnlyList<IGrouping<int, MergeEntry>> groups,
        IReadOnlyList<(string SourceFileName, ArchiveSnapshot Snapshot)> snapshots,
        int duplicateCount,
        CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");

        ml.Cell("A1").Value = "Hộp số";
        ml.Cell("B1").Value = "Số hồ sơ";
        ml.Cell("C1").Value = "Số phụ";
        ml.Cell("D1").Value = "Trích yếu nội dung";
        ml.Cell("E1").Value = "Ngày";
        ml.Cell("F1").Value = "Số ký hiệu";
        ml.Cell("G1").Value = "Tờ số";
        ml.Cell("H1").Value = "Ghi chú";
        ml.Cell("I1").Value = "Trích yếu gốc";
        ml.Cell("J1").Value = "Nguồn file";

        var header = ml.Range("A1:J1");
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
        header.Style.Font.FontColor = XLColor.FromHtml("#1F3864");

        var row = 2;
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var yearCell = ml.Cell(row, 4);
            yearCell.Value = $"NĂM {group.Key}";
            yearCell.Style.Font.Bold = true;
            yearCell.Style.Font.FontColor = XLColor.FromHtml("#1F3864");
            yearCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
            row++;

            string? currentType = null;
            foreach (var entry in group
                         .OrderBy(item => item.SourceFileName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Dossier.MlRow))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dossier = entry.Dossier;
                var type = string.IsNullOrWhiteSpace(dossier.Type)
                    ? "Chưa phân loại"
                    : dossier.Type;

                if (!string.Equals(currentType, type, StringComparison.OrdinalIgnoreCase))
                {
                    var typeCell = ml.Cell(row, 4);
                    typeCell.Value = "*" + type;
                    typeCell.Style.Font.Italic = true;
                    typeCell.Style.Font.FontColor = XLColor.FromHtml("#404040");
                    row++;
                    currentType = type;
                }

                var document = dossier.Documents.FirstOrDefault();
                ml.Cell(row, 1).Value = dossier.BoxNumber;
                ml.Cell(row, 2).Value = string.IsNullOrWhiteSpace(dossier.NumberLabel)
                    ? dossier.Number.ToString(CultureInfo.InvariantCulture)
                    : dossier.NumberLabel;
                ml.Cell(row, 3).Value = dossier.SubNumber;
                ml.Cell(row, 4).Value = document?.TrichYeu ?? dossier.OriginalSummary;
                ml.Cell(row, 5).Value = document?.Ngay ?? string.Empty;
                ml.Cell(row, 6).Value = document?.SoKyHieu ?? string.Empty;
                ml.Cell(row, 7).Value = document?.ToSo ?? string.Empty;
                ml.Cell(row, 8).Value = document?.GhiChu ?? string.Empty;
                ml.Cell(row, 9).Value = dossier.OriginalSummary;
                ml.Cell(row, 10).Value = entry.SourceFileName;

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
        ml.Column(10).Width = 28;
        ml.Range(2, 1, Math.Max(2, row - 1), 10).Style.Alignment.WrapText = true;
        ml.Range(2, 1, Math.Max(2, row - 1), 10).Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;
        ml.SheetView.FreezeRows(1);

        var overview = workbook.AddWorksheet("GOP");
        overview.Cell("A1").Value = "NGUỒN FILE";
        overview.Cell("B1").Value = "SỐ NĂM";
        overview.Cell("C1").Value = "SỐ HỒ SƠ";
        overview.Cell("D1").Value = "LOẠI FILE";
        overview.Range("A1:D1").Style.Font.Bold = true;
        overview.Range("A1:D1").Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");

        var overviewRow = 2;
        foreach (var snapshot in snapshots)
        {
            overview.Cell(overviewRow, 1).Value = snapshot.SourceFileName;
            overview.Cell(overviewRow, 2).Value = snapshot.Snapshot.Years.Count;
            overview.Cell(overviewRow, 3).Value = snapshot.Snapshot.TotalDossiers;
            overview.Cell(overviewRow, 4).Value = snapshot.Snapshot.Kind.ToString();
            overviewRow++;
        }

        overview.Cell(overviewRow, 1).Value = "Tổng";
        overview.Cell(overviewRow, 2).Value = groups.Count;
        overview.Cell(overviewRow, 3).Value = groups.Sum(group => group.Count());
        overview.Cell(overviewRow, 4).Value = $"{duplicateCount} hồ sơ trùng";
        overview.Range(overviewRow, 1, overviewRow, 4).Style.Font.Bold = true;

        overview.Column(1).Width = 42;
        overview.Column(2).Width = 10;
        overview.Column(3).Width = 12;
        overview.Column(4).Width = 24;
        overview.SheetView.FreezeRows(1);

        ml.SetTabActive();
        workbook.SaveAs(path);
    }

    private static string NormalizeDossierKey(DossierRecord dossier)
    {
        return string.IsNullOrWhiteSpace(dossier.NumberLabel)
            ? dossier.Number.ToString(CultureInfo.InvariantCulture)
            : dossier.NumberLabel.Trim().ToUpperInvariant();
    }

    private static MergeOutcome Failure(string message)
    {
        return new MergeOutcome
        {
            Success = false,
            ErrorMessage = message
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Bỏ qua file tạm không xoá được.
        }
    }

    private sealed record MergeEntry(int Year, DossierRecord Dossier, string SourceFileName);
}
