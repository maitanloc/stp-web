using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Infrastructure.Excel;

namespace Stp.Tests.Excel;

public sealed class VbaProjectInjectorTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void Inject_AddsMacroAndCodeNames_ForWorkbookWithoutVba()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "sample.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                workbook.AddWorksheet("ML").Cell("A1").Value = "Hộp số";
                workbook.AddWorksheet("1990").Cell("A1").Value = "Năm 1990";
                workbook.SaveAs(path);
            }

            var injector = CreateInjector();
            var result = injector.Inject(path);

            Assert.True(result.Injected);
            Assert.Equal("ThisWorkbook", result.WorkbookCodeName);
            Assert.Equal(1, result.DonorSheetModules);
            Assert.Equal(1, result.MappedSheetModules);
            Assert.Equal(1, result.SheetsWithoutModules);
            Assert.Equal(223, result.MacroLines);

            using var archive = ZipFile.OpenRead(path);
            var vbaEntries = archive.Entries
                .Where(e => e.FullName.Equals("xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Single(vbaEntries);
            Assert.True(vbaEntries[0].Length > 10_000);

            var contentTypes = ReadEntry(archive, "[Content_Types].xml");
            Assert.Contains("application/vnd.ms-excel.sheet.macroEnabled.main+xml", contentTypes);
            Assert.Contains("/xl/vbaProject.bin", contentTypes);

            var workbookXml = XDocument.Parse(ReadEntry(archive, "xl/workbook.xml"));
            var codeName = workbookXml.Root?
                .Element(MainNs + "workbookPr")?
                .Attribute("codeName")?
                .Value;
            Assert.Equal("ThisWorkbook", codeName);

            var firstSheet = XDocument.Parse(ReadEntry(archive, "xl/worksheets/sheet1.xml"));
            Assert.Equal(
                "Sheet1",
                firstSheet.Root?.Element(MainNs + "sheetPr")?.Attribute("codeName")?.Value);

            var secondSheet = XDocument.Parse(ReadEntry(archive, "xl/worksheets/sheet2.xml"));
            Assert.Null(secondSheet.Root?.Element(MainNs + "sheetPr")?.Attribute("codeName"));

            var relationships = XDocument.Parse(ReadEntry(archive, "xl/_rels/workbook.xml.rels"));
            var vbaRelationships = relationships.Root?
                .Elements()
                .Where(e => ((string?)e.Attribute("Type"))?.EndsWith("/vbaProject", StringComparison.OrdinalIgnoreCase) == true)
                .ToList() ?? new List<XElement>();
            Assert.Single(vbaRelationships);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Inject_IsIdempotent_WhenRunTwice()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "sample.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                workbook.AddWorksheet("ML");
                workbook.SaveAs(path);
            }

            var injector = CreateInjector();
            injector.Inject(path);
            var second = injector.Inject(path);

            Assert.True(second.Injected);

            using var archive = ZipFile.OpenRead(path);
            Assert.Single(
                archive.Entries,
                e => e.FullName.Equals("xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase));

            var relationships = XDocument.Parse(ReadEntry(archive, "xl/_rels/workbook.xml.rels"));
            Assert.Single(
                relationships.Root?
                    .Elements()
                    .Where(e => ((string?)e.Attribute("Type"))?.EndsWith("/vbaProject", StringComparison.OrdinalIgnoreCase) == true)
                    ?? Enumerable.Empty<XElement>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static VbaProjectInjector CreateInjector()
    {
        return new VbaProjectInjector(
            Options.Create(new VbaInjectionOptions()),
            NullLogger<VbaProjectInjector>.Instance);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name)
            ?? throw new InvalidDataException($"Missing zip entry {name}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "stp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
