using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

public sealed class SummaryFilteredExportTests
{
    [Fact]
    public async Task FilteredExport_ByYear_KeepsOnlyMlAndScopedRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateScopedWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);
            var scope = new SummaryScope(Year: 1990);

            var preview = service.PreviewFiltered(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                scope);

            Assert.Equal(2, preview.TotalRows);
            Assert.Equal(0, preview.ChangedRows);
            Assert.Equal(0, preview.IgnoredChanges);

            var result = await service.ExportFilteredAsync(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                scope);

            Assert.Equal(2, result.TotalRows);
            Assert.Equal(0, result.ChangedRows);
            Assert.Equal(0, result.IgnoredChanges);
            Assert.EndsWith("_loc_1990.xlsx", result.FileName);

            var output = Path.Combine(directory, "filtered-1990.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using (var workbook = new XLWorkbook(output))
            {
                Assert.Single(workbook.Worksheets);
                var ml = workbook.Worksheet("ML");

                Assert.Equal("Hộp số", ml.Cell("A1").GetString());
                Assert.Equal("Hồ sơ 36 (phiếu đề xuất, hộ chiếu)", ml.Cell("D2").GetString());
                Assert.Equal("Hồ sơ 37 (giấy khai sinh)", ml.Cell("D3").GetString());
                Assert.True(ml.Cell("D2").Style.Font.Bold);
                Assert.True(ml.Cell("R2").HasFormula);
                Assert.True(ml.Cell("D2").HasComment);
                Assert.False(ml.Cell("D4").HasComment);
                Assert.Equal(3, ml.RangeUsed()!.LastRow().RowNumber());

                Assert.Equal(string.Empty, ml.Cell("D4").GetString());
                Assert.Equal(string.Empty, ml.Cell("D5").GetString());
                Assert.Equal(string.Empty, ml.Cell("D6").GetString());
                Assert.Equal(string.Empty, ml.Cell("D7").GetString());
            }

            using (var archive = ZipFile.OpenRead(output))
            {
                var worksheets = archive.Entries
                    .Where(entry => entry.FullName.StartsWith(
                        "xl/worksheets/sheet",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Assert.Single(worksheets);
                Assert.Equal("xl/worksheets/sheet1.xml", worksheets[0].FullName);
                Assert.Null(archive.GetEntry("xl/calcChain.xml"));
                Assert.NotNull(archive.GetEntry("xl/comments1.xml"));
                Assert.NotNull(archive.GetEntry("xl/drawings/vmldrawing.vml"));
            }

            var sharedStrings = ReadEntry(output, "xl/sharedStrings.xml");
            Assert.Contains("Hồ sơ 36 (phiếu đề xuất, hộ chiếu)", sharedStrings);
            Assert.DoesNotContain("CHI-TON-TAI-TRONG-SHEET-FORM", sharedStrings);
            Assert.DoesNotContain("CHI-TON-TAI-TRONG-SHEET-1991", sharedStrings);
            Assert.DoesNotContain("CHI-TON-TAI-TRONG-SHEET-1992", sharedStrings);

            var commentsXml = ReadEntry(output, "xl/comments1.xml");
            Assert.Contains("Ghi chú dòng 36", commentsXml);
            Assert.DoesNotContain("Ghi chú dòng 38", commentsXml);

            var vmlXml = ReadEntry(output, "xl/drawings/vmldrawing.vml");
            Assert.Matches(@"(?:\w+:)?Row>1</(?:\w+:)?Row>", vmlXml);
            Assert.DoesNotMatch(@"(?:\w+:)?Row>3</(?:\w+:)?Row>", vmlXml);

            var workbookXml = ReadEntry(output, "xl/workbook.xml");
            Assert.Single(Regex.Matches(workbookXml, @"<(?:\w+:)?sheet\b"));
            Assert.Contains("name=\"ML\"", workbookXml);
            Assert.Contains("fullCalcOnLoad=\"1\"", workbookXml);
            Assert.Contains("forceFullCalc=\"1\"", workbookXml);

            var workbookRels = ReadEntry(output, "xl/_rels/workbook.xml.rels");
            Assert.Single(Regex.Matches(workbookRels, @"/worksheet"""));

            var contentTypes = ReadEntry(output, "[Content_Types].xml");
            Assert.DoesNotContain("worksheets/sheet2.xml", contentTypes);
            Assert.DoesNotContain("worksheets/sheet3.xml", contentTypes);
            Assert.DoesNotContain("worksheets/sheet4.xml", contentTypes);
            Assert.DoesNotContain("calcChain.xml", contentTypes);

            var worksheetXml = ReadEntry(output, "xl/worksheets/sheet1.xml");
            Assert.Contains("ref=\"A1:U3\"", worksheetXml);
            Assert.Matches(@"<(?:\w+:)?f\b", worksheetXml);
            AssertSharedStringIndexesAreValid(worksheetXml, sharedStrings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilteredExport_HandlesUnprefixedSharedStrings()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateScopedWorkbook(directory);
            StripClosedXmlNamespacePrefixes(source);

            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);
            var result = await service.ExportFilteredAsync(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                new SummaryScope(Year: 1990));

            var output = Path.Combine(directory, "filtered-unprefixed.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            Assert.Single(workbook.Worksheets);
            var ml = workbook.Worksheet("ML");
            Assert.Equal("Hồ sơ 36 (phiếu đề xuất, hộ chiếu)", ml.Cell("D2").GetString());
            Assert.Equal("Hồ sơ 37 (giấy khai sinh)", ml.Cell("D3").GetString());
            Assert.True(ml.Cell("D2").Style.Font.Bold);
            Assert.True(ml.Cell("R2").HasFormula);

            var sharedStrings = ReadEntry(output, "xl/sharedStrings.xml");
            Assert.Contains("Hồ sơ 36 (phiếu đề xuất, hộ chiếu)", sharedStrings);
            Assert.DoesNotContain("CHI-TON-TAI-TRONG-SHEET-1991", sharedStrings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilteredExport_ByNumberRange_AppliesScopedEditsAndIgnoresOutsideChanges()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateScopedWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);
            var scope = new SummaryScope(
                NumberMode: SummaryNumberMode.Range,
                NumberFrom: 36,
                NumberTo: 37);

            var row36 = session.Rows.Single(row => row.Number == 36);
            var row38 = session.Rows.Single(row => row.Number == 38);
            var changes = new List<SummaryChange>
            {
                new(row36.RowNumber, new[] { "phiếu đề xuất", "hộ chiếu", "giấy khai sinh" }),
                new(row38.RowNumber, new[] { "hộ chiếu" })
            };

            var preview = service.PreviewFiltered(session.SessionId, changes, scope);
            Assert.Equal(2, preview.TotalRows);
            Assert.Equal(1, preview.ChangedRows);
            Assert.Equal(1, preview.IgnoredChanges);
            Assert.Single(preview.Items);
            Assert.Equal(row36.RowNumber, preview.Items[0].RowNumber);

            var result = await service.ExportFilteredAsync(session.SessionId, changes, scope);
            Assert.Equal(2, result.TotalRows);
            Assert.Equal(1, result.ChangedRows);
            Assert.Equal(1, result.IgnoredChanges);

            var output = Path.Combine(directory, "filtered-range.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using var workbook = new XLWorkbook(output);
            Assert.Single(workbook.Worksheets);
            var ml = workbook.Worksheet("ML");

            Assert.Equal(
                "Hồ sơ 36 (phiếu đề xuất, giấy khai sinh, hộ chiếu)",
                ml.Cell("D2").GetString());
            Assert.Equal("Hồ sơ 37 (giấy khai sinh)", ml.Cell("D3").GetString());
            Assert.Equal(string.Empty, ml.Cell("D4").GetString());
            Assert.True(ml.Cell("D2").Style.Font.Bold);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilteredExport_BySingleNumber_KeepsOnlyThatDossier()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateScopedWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);
            var scope = new SummaryScope(
                NumberMode: SummaryNumberMode.Single,
                Number: 38);

            var result = await service.ExportFilteredAsync(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                scope);

            Assert.Equal(1, result.TotalRows);
            Assert.Equal(0, result.ChangedRows);

            var output = Path.Combine(directory, "filtered-single.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using (var workbook = new XLWorkbook(output))
            {
                Assert.Single(workbook.Worksheets);
                var ml = workbook.Worksheet("ML");

                Assert.Equal("Hồ sơ 38 (hộ chiếu)", ml.Cell("D4").GetString());
                Assert.Equal(string.Empty, ml.Cell("D2").GetString());
                Assert.Equal(string.Empty, ml.Cell("D3").GetString());
                Assert.Equal(string.Empty, ml.Cell("D5").GetString());
                Assert.Equal(string.Empty, ml.Cell("D6").GetString());
                Assert.Equal(string.Empty, ml.Cell("D7").GetString());
                Assert.False(ml.Cell("D2").HasComment);
                Assert.True(ml.Cell("D4").HasComment);
            }

            using (var archive = ZipFile.OpenRead(output))
            {
                Assert.NotNull(archive.GetEntry("xl/comments1.xml"));
                Assert.NotNull(archive.GetEntry("xl/drawings/vmldrawing.vml"));
                Assert.NotNull(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels"));
            }

            var commentsXml = ReadEntry(output, "xl/comments1.xml");
            Assert.Contains("Ghi chú dòng 38", commentsXml);
            Assert.DoesNotContain("Ghi chú dòng 36", commentsXml);

            var vmlXml = ReadEntry(output, "xl/drawings/vmldrawing.vml");
            Assert.Matches(@"(?:\w+:)?Row>3</(?:\w+:)?Row>", vmlXml);
            Assert.DoesNotMatch(@"(?:\w+:)?Row>1</(?:\w+:)?Row>", vmlXml);

            var worksheetXml = ReadEntry(output, "xl/worksheets/sheet1.xml");
            Assert.Contains("legacyDrawing", worksheetXml);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilteredExport_DropsCommentsAndVmlWhenNoScopedCommentRemains()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateScopedWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);
            var scope = new SummaryScope(
                NumberMode: SummaryNumberMode.Single,
                Number: 39);

            var result = await service.ExportFilteredAsync(
                session.SessionId,
                Array.Empty<SummaryChange>(),
                scope);

            var output = Path.Combine(directory, "filtered-no-comments.xlsx");
            await File.WriteAllBytesAsync(output, result.Content);

            using (var workbook = new XLWorkbook(output))
            {
                Assert.Single(workbook.Worksheets);
                var ml = workbook.Worksheet("ML");
                Assert.Equal("Hồ sơ 39", ml.Cell("D5").GetString());
                Assert.False(ml.Cell("D2").HasComment);
                Assert.False(ml.Cell("D4").HasComment);
            }

            using (var archive = ZipFile.OpenRead(output))
            {
                Assert.Null(archive.GetEntry("xl/comments1.xml"));
                Assert.Null(archive.GetEntry("xl/drawings/vmldrawing.vml"));
                Assert.Null(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels"));
            }

            var worksheetXml = ReadEntry(output, "xl/worksheets/sheet1.xml");
            Assert.DoesNotContain("legacyDrawing", worksheetXml);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetRows_FiltersByNumberScopeAndCombinesWithYear()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateScopedWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);

            Assert.Equal(6, session.Rows.Count);

            Assert.Equal(6, service.GetRows(session.SessionId, SummaryScope.All, 1, 20).Total);
            Assert.Equal(
                2,
                service.GetRows(session.SessionId, new SummaryScope(Year: 1990), 1, 20).Total);
            Assert.Equal(
                3,
                service.GetRows(
                    session.SessionId,
                    new SummaryScope(
                        NumberMode: SummaryNumberMode.Range,
                        NumberFrom: 37,
                        NumberTo: 39),
                    1,
                    20).Total);
            Assert.Equal(
                1,
                service.GetRows(
                    session.SessionId,
                    new SummaryScope(
                        NumberMode: SummaryNumberMode.Single,
                        Number: 40),
                    1,
                    20).Total);
            Assert.Equal(
                0,
                service.GetRows(
                    session.SessionId,
                    new SummaryScope(
                        NumberMode: SummaryNumberMode.Single,
                        Number: 41),
                    1,
                    20).Total);
            Assert.Equal(
                1,
                service.GetRows(
                    session.SessionId,
                    new SummaryScope(
                        Year: 1991,
                        NumberMode: SummaryNumberMode.Single,
                        Number: 38),
                    1,
                    20).Total);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilteredExport_WithoutMatchingRows_Throws()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateScopedWorkbook(directory);
            var service = CreateService(directory);
            var session = await OpenSessionAsync(service, source);
            var scope = new SummaryScope(
                NumberMode: SummaryNumberMode.Single,
                Number: 999);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExportFilteredAsync(
                    session.SessionId,
                    Array.Empty<SummaryChange>(),
                    scope));
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

    private static void StripClosedXmlNamespacePrefixes(string path)
    {
        var temp = path + ".unprefixed";
        using (var source = ZipFile.OpenRead(path))
        using (var output = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var target = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var input = entry.Open();
                using var destination = target.Open();

                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    input.CopyTo(destination);
                    continue;
                }

                using var reader = new StreamReader(input);
                var text = reader.ReadToEnd();
                text = text.Replace("<x:", "<").Replace("</x:", "</");
                text = text.Replace(
                    "xmlns:x=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"",
                    "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"");
                using var writer = new StreamWriter(destination, new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    private static string CreateScopedWorkbook(string directory)
    {
        var path = Path.Combine(directory, "scoped.xlsx");

        using var workbook = new XLWorkbook();
        var ml = workbook.AddWorksheet("ML");
        ml.Cell("A1").Value = "Hộp số";
        ml.Cell("B1").Value = "Hồ sơ số";
        ml.Cell("C1").Value = "Số ký hiệu";
        ml.Cell("D1").Value = "Trích yếu nội dung";
        ml.Cell("E1").Value = "Thời gian tài liệu";
        ml.Cell("R1").Value = "Công thức";
        ml.Cell("S1").Value = "phiếu đề xuất";
        ml.Cell("T1").Value = "giấy khai sinh";
        ml.Cell("U1").Value = "hộ chiếu";

        ml.Cell("B2").Value = 36;
        ml.Cell("E2").Value = new DateTime(1990, 1, 10);
        ml.Cell("D2").Value = "Hồ sơ 36 (phiếu đề xuất, hộ chiếu)";
        ml.Cell("D2").Style.Font.Bold = true;
        ml.Cell("D2").CreateComment().AddText("Ghi chú dòng 36");
        ml.Cell("R2").FormulaA1 = "=D2&\"!\"";

        ml.Cell("B3").Value = 37;
        ml.Cell("E3").Value = new DateTime(1990, 2, 10);
        ml.Cell("D3").Value = "Hồ sơ 37 (giấy khai sinh)";
        ml.Cell("R3").FormulaA1 = "=D3&\"!\"";

        ml.Cell("B4").Value = 38;
        ml.Cell("E4").Value = new DateTime(1991, 1, 10);
        ml.Cell("D4").Value = "Hồ sơ 38 (hộ chiếu)";
        ml.Cell("D4").CreateComment().AddText("Ghi chú dòng 38");
        ml.Cell("R4").FormulaA1 = "=D4&\"!\"";

        ml.Cell("B5").Value = 39;
        ml.Cell("E5").Value = new DateTime(1991, 2, 10);
        ml.Cell("D5").Value = "Hồ sơ 39";

        ml.Cell("B6").Value = 40;
        ml.Cell("E6").Value = new DateTime(1992, 1, 10);
        ml.Cell("D6").Value = "Hồ sơ 40 (phiếu đề xuất)";

        ml.Cell("B7").Value = "A41";
        ml.Cell("E7").Value = new DateTime(1992, 2, 10);
        ml.Cell("D7").Value = "Hồ sơ A41";

        var form = workbook.AddWorksheet("form");
        form.Cell("A1").Value = "Tra cứu";
        form.Cell("B1").FormulaA1 = "=10+20";
        form.Cell("A2").Value = "CHI-TON-TAI-TRONG-SHEET-FORM";

        var year1991 = workbook.AddWorksheet("1991");
        year1991.Cell("A1").Value = "Sheet năm 1991";
        year1991.Cell("A2").Value = "CHI-TON-TAI-TRONG-SHEET-1991";

        var year1992 = workbook.AddWorksheet("1992");
        year1992.Cell("A1").Value = "Sheet năm 1992";
        year1992.Cell("A2").Value = "CHI-TON-TAI-TRONG-SHEET-1992";

        workbook.SaveAs(path);
        return path;
    }

    private static void AssertSharedStringIndexesAreValid(
        string worksheetXml,
        string sharedStringsXml)
    {
        var uniqueCountMatch = Regex.Match(
            sharedStringsXml,
            @"\buniqueCount=""(?<count>\d+)""");

        Assert.True(uniqueCountMatch.Success);
        var uniqueCount = int.Parse(uniqueCountMatch.Groups["count"].Value);

        foreach (Match cell in Regex.Matches(
                     worksheetXml,
                     @"<c\b(?=[^>]*\bt=""s"")(?=[^>]*[^/]>)[^>]*>.*?</c>",
                     RegexOptions.Singleline))
        {
            var value = Regex.Match(cell.Value, @"<v>\s*(?<index>\d+)\s*</v>");
            Assert.True(value.Success);
            var index = int.Parse(value.Groups["index"].Value);
            Assert.InRange(index, 0, uniqueCount - 1);
        }
    }

    private static string ReadEntry(string path, string entryName)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "stp-summary-filtered-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
