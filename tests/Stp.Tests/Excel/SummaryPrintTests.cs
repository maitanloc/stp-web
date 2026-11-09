using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Excel;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

public sealed class SummaryPrintTests
{
    [Fact]
    public async Task ExportPrint_Combined_ProducesMacroWorkbookWithYearSheets()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = CreateSummaryWorkbook(directory, includeMissingSheetNumber: false);
            var service = CreateSummaryService(directory);

            await using var stream = File.OpenRead(path);
            var session = await service.CreateSessionAsync(stream, Path.GetFileName(path));

            var result = await service.ExportPrintAsync(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                new SummaryPrintOptions(
                    Mode: SummaryPrintOutputMode.Combined,
                    AllowIncomplete: true,
                    OutputFileName: "combined.xlsm"));

            Assert.Equal("combined.xlsm", result.FileName);
            Assert.Equal(2, result.Years.Count);

            var output = Path.Combine(directory, "combined.xlsm");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            Assert.Equal("BANG_IN_HANG_LOAT", workbook.Worksheet("1990").Cell("K1").GetString());
            Assert.Equal("BANG_IN_HANG_LOAT", workbook.Worksheet("1991").Cell("K1").GetString());
            Assert.Contains(
                "$A$8:$G$",
                workbook.Worksheet("1990").PageSetup.PrintAreas.Single().RangeAddress.ToString());

            // Dòng dữ liệu đầu tiên của sheet in phải trỏ đúng cột ML:
            // Số ký hiệu ← F, Ngày ← E, Trích yếu ← D, Tờ số ← G.
            var printSheet = workbook.Worksheet("1990");
            Assert.Contains("$F3", printSheet.Cell("B11").FormulaA1);
            Assert.Contains("$E3", printSheet.Cell("C11").FormulaA1);
            Assert.Contains("$D3", printSheet.Cell("D11").FormulaA1);
            Assert.Contains("$G3", printSheet.Cell("F11").FormulaA1);
            Assert.Contains("$B3", printSheet.Cell("D9").FormulaA1);

            using var archive = ZipFile.OpenRead(output);
            Assert.Contains(archive.Entries, entry => entry.FullName == "xl/vbaProject.bin");

            var ml = workbook.Worksheet("ML");
            // Mỗi năm có một dòng tiêu đề "NĂM ..." trước nhóm hồ sơ.
            Assert.Equal("NĂM 1990", ml.Cell("D2").GetString());
            Assert.Equal("36", ml.Cell("B3").GetString());
            Assert.Equal("93/QĐ.UBT", ml.Cell("F3").GetString());
            Assert.Equal("01-20", ml.Cell("G3").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportPrint_PerYear_ReturnsZipOfMacroWorkbooks()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = CreateSummaryWorkbook(directory, includeMissingSheetNumber: false);
            var service = CreateSummaryService(directory);

            await using var stream = File.OpenRead(path);
            var session = await service.CreateSessionAsync(stream, Path.GetFileName(path));

            var result = await service.ExportPrintAsync(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                new SummaryPrintOptions(
                    Mode: SummaryPrintOutputMode.PerYear,
                    AllowIncomplete: true,
                    OutputFileName: "theo-nam.zip"));

            Assert.Equal("theo-nam.zip", result.FileName);
            Assert.Equal("application/zip", result.ContentType);

            using var streamZip = new MemoryStream(result.Content);
            using var archive = new ZipArchive(streamZip, ZipArchiveMode.Read);
            Assert.Equal(2, archive.Entries.Count);

            foreach (var entry in archive.Entries)
            {
                Assert.EndsWith(".xlsm", entry.FullName, StringComparison.OrdinalIgnoreCase);

                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                await entryStream.CopyToAsync(buffer);
                buffer.Position = 0;

                using var workbook = new XLWorkbook(buffer);
                Assert.Contains(
                    workbook.Worksheets,
                    sheet => sheet.Cell("K1").GetString() == "BANG_IN_HANG_LOAT");

                buffer.Position = 0;
                using var workbookArchive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
                Assert.Contains(
                    workbookArchive.Entries,
                    item => item.FullName == "xl/vbaProject.bin");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportPrint_AppliesEditsAndSanitizesImplausibleSheetNumbers()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = CreateSummaryWorkbook(directory, includeMissingSheetNumber: false);
            var service = CreateSummaryService(directory);

            await using var stream = File.OpenRead(path);
            var session = await service.CreateSessionAsync(stream, Path.GetFileName(path));

            var changes = new List<SummaryChange>
            {
                new(
                    2,
                    Array.Empty<string>(),
                    SummaryText: "Trích yếu đã sửa cho hồ sơ 36",
                    SheetNumber: "01-25",
                    NumberLabel: "36A"),
                new(3, Array.Empty<string>(), SheetNumber: "43831")
            };

            var preview = service.PreviewPrint(session.SessionId, changes);
            Assert.True(preview.HasIssues);
            var year1991 = Assert.Single(preview.Years, item => item.Year == 1991);
            Assert.Equal(1, year1991.MissingSheetNumber);

            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExportPrintAsync(
                    session.SessionId,
                    changes,
                    new SummaryPrintOptions(Mode: SummaryPrintOutputMode.Combined)));
            Assert.Contains("thiếu số tờ", blocked.Message);

            var result = await service.ExportPrintAsync(
                session.SessionId,
                changes,
                new SummaryPrintOptions(
                    Mode: SummaryPrintOutputMode.Combined,
                    AllowIncomplete: true,
                    OutputFileName: "edited.xlsm"));

            var output = Path.Combine(directory, "edited.xlsm");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            var ml = workbook.Worksheet("ML");

            Assert.Equal("36A", ml.Cell("B3").GetString());
            Assert.Equal("Trích yếu đã sửa cho hồ sơ 36", ml.Cell("D3").GetString());
            Assert.Equal("01-25", ml.Cell("G3").GetString());

            // Serial Excel 43831 không phải khoảng trang nên phải bị bỏ trống.
            Assert.Equal(string.Empty, ml.Cell("G5").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SummaryToolService CreateSummaryService(string directory)
    {
        var storage = new FileStorage(Options.Create(new StorageOptions
        {
            RootPath = Path.Combine(directory, "data")
        }));

        var inspector = new ExcelInspector(NullLogger<ExcelInspector>.Instance);
        var layout = new PrintLayoutService(new PrintLayoutOptions());
        var generator = new Type2SheetGenerator(
            inspector,
            layout,
            Options.Create(new PrintLayoutOptions()),
            NullLogger<Type2SheetGenerator>.Instance);
        var injector = new VbaProjectInjector(
            Options.Create(new VbaInjectionOptions()),
            NullLogger<VbaProjectInjector>.Instance);
        var print = new SummaryPrintService(
            generator,
            injector,
            NullLogger<SummaryPrintService>.Instance);

        return new SummaryToolService(storage, NullLogger<SummaryToolService>.Instance, print);
    }

    private static string CreateSummaryWorkbook(string directory, bool includeMissingSheetNumber)
    {
        var path = Path.Combine(directory, "print-source.xlsx");

        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");
        ml.Cell("A1").Value = "Hộp số";
        ml.Cell("B1").Value = "Số hồ sơ";
        ml.Cell("C1").Value = "Hồ sơ số";
        ml.Cell("D1").Value = "Trích yếu nội dung";
        ml.Cell("E1").Value = "Thời gian tài liệu";
        ml.Cell("F1").Value = "Số tờ";
        ml.Cell("G1").Value = "Ghi chú";

        ml.Cell("B2").Value = 36;
        ml.Cell("C2").Value = "01";
        ml.Cell("D2").Value = "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn";
        ml.Cell("E2").Value = new DateTime(1990, 1, 20);
        ml.Cell("F2").Value = "93/QĐ.UBT";
        ml.Cell("G2").Value = "01-20";

        ml.Cell("B3").Value = 37;
        ml.Cell("C3").Value = "02";
        ml.Cell("D3").Value = "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn";
        ml.Cell("E3").Value = new DateTime(1991, 2, 3);
        ml.Cell("F3").Value = "99/QĐ.UBT";
        if (!includeMissingSheetNumber)
        {
            ml.Cell("G3").Value = "21-40";
        }

        workbook.SaveAs(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-print-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
