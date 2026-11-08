using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Excel;
using Stp.Infrastructure.Excel;

namespace Stp.Tests.Excel;

public sealed class TemplateEngineTests
{
    [Fact]
    public async Task Convert_WithoutExcel_ProducesXlsmWithVba()
    {
        var directory = Path.Combine(Path.GetTempPath(), "stp-template-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var xlsx = Path.Combine(directory, "input.xlsx");
            var xlsm = Path.Combine(directory, "output.xlsm");

            using (var workbook = new XLWorkbook())
            {
                var ml = workbook.AddWorksheet("ML");
                ml.Cell("A1").Value = "Hộp số";
                ml.Cell("B2").Value = 1;
                workbook.SaveAs(xlsx);
            }

            var options = Options.Create(new ExcelComOptions
            {
                ExcelPath = "none",
                PreferredHost = "Microsoft"
            });

            var injector = new VbaProjectInjector(
                Options.Create(new VbaInjectionOptions { Enabled = true }),
                NullLogger<VbaProjectInjector>.Instance);

            var engine = new ComExcelEngine(options, injector, NullLogger<ComExcelEngine>.Instance);

            Assert.False(engine.IsAvailable);

            var outcome = await engine.ConvertAsync(new ConversionRequest
            {
                InputPath = xlsx,
                OutputPath = xlsm,
                MacroCode = "Sub Test() End Sub",
                GeneratePrintSheets = false,
                InjectMacro = true
            });

            Assert.True(outcome.Success, outcome.ErrorMessage ?? outcome.Warning ?? "Conversion failed");
            Assert.True(File.Exists(xlsm));
            Assert.Contains("không cần Excel", outcome.Warning ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            using var zip = System.IO.Compression.ZipFile.OpenRead(xlsm);
            Assert.NotNull(zip.GetEntry("xl/vbaProject.bin"));

            var contentTypes = zip.GetEntry("[Content_Types].xml");
            Assert.NotNull(contentTypes);
            using (var reader = new StreamReader(contentTypes!.Open()))
            {
                var xml = reader.ReadToEnd();
                Assert.Contains("macroEnabled.main+xml", xml);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
