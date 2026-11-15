using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Contracts;
using Stp.Core.AutoTick;
using Stp.Core.Storage;
using Stp.Infrastructure.AutoTick;
using Stp.Infrastructure.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Stp.Tests.AutoTick;

public sealed class AutoTickServiceTests
{
    private readonly ITestOutputHelper _output;

    public AutoTickServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }
    private sealed class DummyPublisher : IAutoTickEventPublisher
    {
        public List<AutoTickProgressEvent> ProgressEvents { get; } = new();
        public Task PublishProgressAsync(AutoTickProgressEvent progress, CancellationToken ct = default)
        {
            ProgressEvents.Add(progress);
            return Task.CompletedTask;
        }

        public Task PublishWatcherAlertAsync(AutoTickWatcherCheckResponse alert, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task InspectAndExecute_TicksOnlyMissingColumns_LeavesExistingTicksIntact()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_autotick_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("1997");
                // Row 4: Column headers
                ws.Cell(4, 9).SetValue("phiếu đề xuất");   // Col I
                ws.Cell(4, 10).SetValue("bộ công hàm");     // Col J
                ws.Cell(4, 11).SetValue("giấy khai sinh");   // Col K
                ws.Cell(4, 12).SetValue("CMND");             // Col L

                // Row 5: Has (...) with 3 documents: phiếu đề xuất, bộ công hàm, CMND
                // But col I is already ticked (true), col J and L are empty (missing)
                ws.Cell(5, 2).SetValue(101);
                ws.Cell(5, 4).SetValue("Quyết định kết hôn của Bà A và Ông B. Năm 1997 (phiếu đề xuất, bộ công hàm, CMND)");
                ws.Cell(5, 9).SetValue(true); // Already ticked

                // Row 6: Has (...) with 1 document: giấy khai sinh, already ticked
                ws.Cell(6, 2).SetValue(102);
                ws.Cell(6, 4).SetValue("Quyết định kết hôn của Bà C và Ông D. Năm 1997 (giấy khai sinh)");
                ws.Cell(6, 11).SetValue(true); // Already ticked

                // Row 7: Has NO (...)
                ws.Cell(7, 2).SetValue(103);
                ws.Cell(7, 4).SetValue("Quyết định kết hôn của Bà E và Ông F. Năm 1997");

                wb.SaveAs(tempFile);
            }

            var storage = new FileStorage(Options.Create(new StorageOptions { RootPath = Path.GetTempPath() }));
            var publisher = new DummyPublisher();
            var service = new AutoTickService(storage, publisher, NullLogger<AutoTickService>.Instance);

            // 1. Inspect
            var inspect = await service.InspectAsync(new AutoTickInspectRequest
            {
                OneDriveUrl = tempFile,
                SheetName = "1997"
            });

            Assert.Equal("1997", inspect.ActiveSheet);
            Assert.Equal(3, inspect.TotalRows);
            Assert.Equal(2, inspect.RowsWithParentheses);
            Assert.Equal(1, inspect.RowsFullyTicked); // Row 6
            Assert.Equal(1, inspect.RowsPartiallyTicked); // Row 5

            // 2. Start Job
            var jobId = await service.StartJobAsync(new AutoTickStartRequest
            {
                OneDriveUrl = tempFile,
                SheetName = "1997"
            });

            // Wait for completion
            AutoTickJobStatusResponse? status = null;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);
                status = await service.GetJobStatusAsync(jobId);
                if (status is not null && status.Status == "Completed") break;
            }

            Assert.NotNull(status);
            Assert.Equal("Completed", status.Status);
            Assert.Equal(1, status.NewlyTickedRowsCount); // Row 5 was ticked
            Assert.Equal(2, status.NewlyTickedCellsCount); // Cols J and L

            // 3. Verify in saved file
            var result = await service.GetResultFileAsync(jobId);
            Assert.NotNull(result);

            using var resultWb = new XLWorkbook(result.Value.Stream);
            var resultWs = resultWb.Worksheet("1997");

            // Row 5: Col I was already true, Col J and L should now be true, Col K false/empty
            Assert.True(resultWs.Cell(5, 9).GetBoolean());
            Assert.True(resultWs.Cell(5, 10).GetBoolean());
            Assert.True(resultWs.Cell(5, 11).IsEmpty() || !resultWs.Cell(5, 11).GetBoolean());
            Assert.True(resultWs.Cell(5, 12).GetBoolean());

            // Row 6: Col K remains true
            Assert.True(resultWs.Cell(6, 11).GetBoolean());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Inspect_RealOneDriveLink_DownloadsSuccessfully()
    {
        var publisher = new DummyPublisher();
        var storage = new FileStorage(Options.Create(new StorageOptions
        {
            RootPath = Path.GetTempPath()
        }));

        var service = new AutoTickService(storage, publisher, NullLogger<AutoTickService>.Instance);

        var url = "https://onedrive.live.com/:x:/g/personal/52960fb7b88aa4ef/IQDder6_nmp6QI5JsHL1gk1oAV2fQl2Dv7Nt09mpsdj3lJ0?e=5ggKl7";
        var res = await service.InspectAsync(new AutoTickInspectRequest
        {
            OneDriveUrl = url
        });

        Assert.NotNull(res);
        Assert.NotEmpty(res.Sheets);
        Assert.True(res.TotalRows > 0);
    }

    [Fact]
    public void Inspect_RealFile_D_STP()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stp_onedrive");
        var file = Directory.GetFiles(tempDir, "*DK KET HON.xlsx").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
        if (file == null)
        {
            _output.WriteLine("No file in stp_onedrive");
            return;
        }

        _output.WriteLine($"Inspecting: {file}");
        using var wb = new XLWorkbook(file);
        _output.WriteLine($"Total sheets: {wb.Worksheets.Count}");
        _output.WriteLine("Sheet names: " + string.Join(", ", wb.Worksheets.Select(w => w.Name)));

        // Check sheet 1997 or whichever is there
        var ws = wb.Worksheet("1997") ?? wb.Worksheets.First();
        _output.WriteLine($"--- Sheet: {ws.Name} ---");
        for (int r = 1; r <= 8; r++)
        {
            var rowTexts = new List<string>();
            for (int c = 1; c <= 45; c++)
            {
                var text = ws.Cell(r, c).GetString().Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var colLetter = ws.Column(c).ColumnLetter();
                    rowTexts.Add($"{colLetter}{r}: {text}");
                }
            }
            _output.WriteLine($"Row {r}: {string.Join(" | ", rowTexts)}");
        }

        // Test improved matching
        var sheetsToCheck = new[] { "1997", "1998", "file nhập", "1995ok" };

        foreach (var sName in sheetsToCheck)
        {
            var wsTarget = wb.Worksheet(sName);
            if (wsTarget == null) continue;

            var hdrs = typeof(AutoTickService).GetMethod("ExtractHeaders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { wsTarget }) as List<AutoTickSheetHeader>;
            Assert.NotNull(hdrs);

            var unmatchedList = new HashSet<string>();
            int checkedRows = 0;
            int lastR = Math.Min(wsTarget.LastRowUsed()?.RowNumber() ?? 50, 150);

            for (int r = 9; r <= lastR; r++)
            {
                var summary = wsTarget.Cell(r, 8).GetString().Trim();
                if (string.IsNullOrEmpty(summary)) summary = wsTarget.Cell(r, 4).GetString().Trim();

                var m = System.Text.RegularExpressions.Regex.Match(summary, @"\(([^)]+)\)\s*$");
                if (!m.Success) continue;

                checkedRows++;
                var items = m.Groups[1].Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToLowerInvariant())
                    .ToList();

                foreach (var item in items)
                {
                    bool isMatched = hdrs.Any(h => IsSmartMatchInternal(h.Title, item));
                    if (!isMatched)
                    {
                        unmatchedList.Add(item);
                        _output.WriteLine($"  [{sName} Row {r}] UNMATCHED: '{item}'");
                    }
                }
            }
            _output.WriteLine($"Sheet '{sName}': Checked {checkedRows} rows. Unmatched count: {unmatchedList.Count}. Items: {string.Join(" | ", unmatchedList)}");
        }
    }

    [Fact]
    public async Task SyncSummaryRows_AutoAddsMissingColumnHeader_AndTicksTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_autotick_newcol_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("1997");
                // Row 4: Only 2 existing headers: Col I (9) and Col J (10)
                ws.Cell(4, 9).SetValue("phiếu đề xuất");
                ws.Cell(4, 10).SetValue("bộ công hàm");

                // Row 5: Row with number label "1"
                ws.Cell(5, 2).SetValue(1);
                ws.Cell(5, 4).SetValue("Quyết định kết hôn của Bà Nguyễn Thị A và Ông John Smith. Năm 1997");

                wb.SaveAs(tempFile);
            }

            var storage = new FileStorage(Options.Create(new StorageOptions { RootPath = Path.GetTempPath() }));
            var publisher = new DummyPublisher();
            var service = new AutoTickService(storage, publisher, NullLogger<AutoTickService>.Instance);

            // Row contains "chứng nhận hạnh kiểm" which is NOT in Row 4 headers
            var req = new AutoTickSyncSummaryRequest
            {
                OneDriveUrl = tempFile,
                SheetName = "1997",
                EnsureFormulaH = true,
                Rows = new List<AutoTickSyncSummaryItem>
                {
                    new()
                    {
                        RowIndex = 5,
                        NumberLabel = "1",
                        SummaryText = "Quyết định kết hôn của Bà Nguyễn Thị A và Ông John Smith. (phiếu đề xuất, chứng nhận hạnh kiểm)"
                    }
                }
            };

            var res = await service.SyncSummaryRowsToOneDriveAsync(req);

            Assert.True(res.Success);
            Assert.Equal(1, res.SyncedRowsCount);
            Assert.Equal(2, res.TickedCellsCount); // Cả "phiếu đề xuất" và "chứng nhận hạnh kiểm" đều được tick!
            Assert.Equal(1, res.NewHeadersCount);
            Assert.Contains("chứng nhận hạnh kiểm", res.NewHeaderTitles);

            // Verify the saved file
            using var resultWb = new XLWorkbook(tempFile);
            var resultWs = resultWb.Worksheet("1997");

            // 1. Col I (9) was matched and ticked
            Assert.True(resultWs.Cell(5, 9).GetBoolean());

            // 2. Col J (10) was not in summary, remains unticked
            Assert.True(resultWs.Cell(5, 10).IsEmpty() || !resultWs.Cell(5, 10).GetBoolean());

            // 3. Col K (11) was automatically created with header "chứng nhận hạnh kiểm"
            Assert.Equal("chứng nhận hạnh kiểm", resultWs.Cell(4, 11).GetString().Trim());

            // 4. "nếu có thể tick cột mới thành true được thì cứ làm" -> Row 5 Col K (11) IS ticked to TRUE!
            Assert.True(resultWs.Cell(5, 11).GetBoolean());

            // 5. Formula H must cover up to K4 (last column)
            Assert.True(resultWs.Cell(5, 8).HasFormula);
            Assert.Contains("K$4", resultWs.Cell(5, 8).FormulaA1);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteJob_AutoAddsMissingColumnHeader_AndTicksTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_job_newcol_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("1997");
                ws.Cell(4, 9).SetValue("phiếu đề xuất");
                ws.Cell(4, 10).SetValue("bộ công hàm");

                // Row 5 has document "tập tục đời sống" which is not in columns
                ws.Cell(5, 2).SetValue(101);
                ws.Cell(5, 4).SetValue("Quyết định kết hôn của Bà A và Ông B. Năm 1997 (phiếu đề xuất, tập tục đời sống)");

                wb.SaveAs(tempFile);
            }

            var storage = new FileStorage(Options.Create(new StorageOptions { RootPath = Path.GetTempPath() }));
            var publisher = new DummyPublisher();
            var service = new AutoTickService(storage, publisher, NullLogger<AutoTickService>.Instance);

            var jobId = await service.StartJobAsync(new AutoTickStartRequest
            {
                OneDriveUrl = tempFile,
                SheetName = "1997"
            });

            AutoTickJobStatusResponse? status = null;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(100);
                status = await service.GetJobStatusAsync(jobId);
                if (status is not null && status.Status == "Completed") break;
            }

            Assert.NotNull(status);
            Assert.Equal("Completed", status.Status);
            Assert.Equal(2, status.NewlyTickedCellsCount); // Cả phiếu đề xuất và tập tục đời sống đều được tick!

            var result = await service.GetResultFileAsync(jobId);
            Assert.NotNull(result);

            using var resultWb = new XLWorkbook(result.Value.Stream);
            var resultWs = resultWb.Worksheet("1997");

            // Header created in Col 11
            Assert.Equal("tập tục đời sống", resultWs.Cell(4, 11).GetString().Trim());

            // Col 9 is ticked
            Assert.True(resultWs.Cell(5, 9).GetBoolean());

            // Col 11 IS ticked to TRUE!
            Assert.True(resultWs.Cell(5, 11).GetBoolean());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static bool IsSmartMatchInternal(string headerTitle, string documentItem)
    {
        return (bool)typeof(AutoTickService).GetMethod("IsSmartMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { headerTitle, documentItem })!;
    }
}
