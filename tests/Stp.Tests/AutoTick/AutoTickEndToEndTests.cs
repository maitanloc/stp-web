using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Api.Controllers;
using Stp.Contracts;
using Stp.Core.AutoTick;
using Stp.Core.Storage;
using Stp.Infrastructure.AutoTick;
using Stp.Infrastructure.Storage;
using Xunit;

namespace Stp.Tests.AutoTick;

public sealed class AutoTickEndToEndTests
{
    private sealed class DummyPublisher : IAutoTickEventPublisher
    {
        public List<AutoTickProgressEvent> ProgressHistory { get; } = new();

        public Task PublishProgressAsync(AutoTickProgressEvent progress, CancellationToken ct = default)
        {
            ProgressHistory.Add(progress);
            return Task.CompletedTask;
        }

        public Task PublishWatcherAlertAsync(AutoTickWatcherCheckResponse alert, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task EndToEnd_ControllerAndService_RealDataVerification()
    {
        // 1. Prepare copy of real test workbook
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../onedrive_file.xlsx")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../onedrive_file.xlsx")),
            @"d:\A\stp-web\onedrive_file.xlsx",
            Path.GetFullPath("onedrive_file.xlsx")
        };

        var realSamplePath = candidates.FirstOrDefault(File.Exists);
        Assert.True(realSamplePath is not null && File.Exists(realSamplePath), $"Tệp onedrive_file.xlsx phải tồn tại để chạy E2E test.");

        var testFile = Path.Combine(Path.GetTempPath(), $"e2e_autotick_{Guid.NewGuid():N}.xlsx");
        File.Copy(realSamplePath, testFile, overwrite: true);

        try
        {
            var storage = new FileStorage(Options.Create(new StorageOptions { RootPath = Path.GetTempPath() }));
            var publisher = new DummyPublisher();
            var service = new AutoTickService(storage, publisher, NullLogger<AutoTickService>.Instance);
            var controller = new AutoTickController(service, NullLogger<AutoTickController>.Instance);

            // 2. Test Controller POST /api/auto-tick/inspect
            var inspectAction = await controller.Inspect(new AutoTickInspectRequest
            {
                OneDriveUrl = testFile,
                SheetName = "1997"
            }, CancellationToken.None);

            if (inspectAction.Result is ObjectResult objRes && objRes.StatusCode != 200)
            {
                Assert.Fail($"Inspect thất bại: StatusCode={objRes.StatusCode}, Value={objRes.Value}");
            }

            var okInspect = Assert.IsType<OkObjectResult>(inspectAction.Result);
            var inspectData = Assert.IsType<AutoTickInspectResponse>(okInspect.Value);

            Assert.Equal("1997", inspectData.ActiveSheet);
            Assert.True(inspectData.TotalRows > 500, "Sheet 1997 phải có hơn 500 dòng");
            Assert.True(inspectData.DocumentHeaders.Count >= 50, "Sheet 1997 phải nhận diện được hơn 50 cột giấy tờ");

            // Verify Row 529 in sample rows or rows
            using (var wb = new XLWorkbook(testFile))
            {
                var ws = wb.Worksheet("1997");
                var row529Text = ws.Cell(529, 4).GetString();
                Assert.Contains("NGUYỄN THỊ GIÀU", row529Text);
                Assert.Contains("KOO SANG DAE", row529Text);

                // Ensure Col I (9) to Col BJ (62) on Row 529 are unticked before start
                for (int c = 9; c <= 62; c++)
                {
                    var cell = ws.Cell(529, c);
                    bool isTicked = !cell.IsEmpty() && cell.DataType == XLDataType.Boolean && cell.GetBoolean();
                    Assert.False(isTicked, $"Ô hàng 529 cột {c} ban đầu phải chưa tick.");
                }
            }

            // 3. Test Controller POST /api/auto-tick/start
            // Run on number range containing row 529 (B=2385)
            var startAction = await controller.Start(new AutoTickStartRequest
            {
                OneDriveUrl = testFile,
                SheetName = "1997",
                NumberFrom = 2380,
                NumberTo = 2390
            }, CancellationToken.None);

            var okStart = Assert.IsType<OkObjectResult>(startAction.Result);
            var jobId = Assert.IsType<Guid>(okStart.Value);
            Assert.NotEqual(Guid.Empty, jobId);

            // 4. Poll Controller GET /api/auto-tick/{jobId}/status until completed
            AutoTickJobStatusResponse? jobStatus = null;
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(250);
                var statusAction = await controller.GetStatus(jobId, CancellationToken.None);
                var okStatus = Assert.IsType<OkObjectResult>(statusAction.Result);
                jobStatus = Assert.IsType<AutoTickJobStatusResponse>(okStatus.Value);
                if (jobStatus.Status == "Completed" || jobStatus.Status == "Failed") break;
            }

            Assert.NotNull(jobStatus);
            Assert.Equal("Completed", jobStatus.Status);
            Assert.True(jobStatus.NewlyTickedCellsCount > 0, "Phải có các ô được đánh tick mới.");
            Assert.True(publisher.ProgressHistory.Count > 0, "Phải có sự kiện progress được gửi qua SignalR publisher.");

            // 5. Test Controller GET /api/auto-tick/{jobId}/download
            var downloadAction = await controller.Download(jobId, CancellationToken.None);
            var fileResult = Assert.IsType<FileStreamResult>(downloadAction);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
            Assert.NotNull(fileResult.FileStream);

            // 6. Verify result file contents
            using (var resultWb = new XLWorkbook(fileResult.FileStream))
            {
                var resultWs = resultWb.Worksheet("1997");

                // Check Row 529: Column I (phiếu đề xuất) must now be TRUE!
                var cellI529 = resultWs.Cell(529, 9);
                Assert.True(cellI529.GetBoolean(), "Cột I (phiếu đề xuất) dòng 529 phải được tick TRUE.");

                // Check Row 529: Column J (bộ công hàm) must now be TRUE!
                var cellJ529 = resultWs.Cell(529, 10);
                Assert.True(cellJ529.GetBoolean(), "Cột J (bộ công hàm) dòng 529 phải được tick TRUE.");

                // Check Row 529: Column P (CMND) must now be TRUE!
                var cellP529 = resultWs.Cell(529, 16);
                Assert.True(cellP529.GetBoolean(), "Cột P (CMND) dòng 529 phải được tick TRUE.");

                // Check Row 529: Column AP (giấy khai sinh) must now be TRUE!
                var cellAP529 = resultWs.Cell(529, 42); // AP is 42
                Assert.True(cellAP529.GetBoolean(), "Cột AP (giấy khai sinh) dòng 529 phải được tick TRUE.");
            }

            // 7. Test Watcher endpoint POST /api/auto-tick/watcher/check
            var watcherAction = await controller.CheckWatcher(new AutoTickWatcherCheckRequest
            {
                OneDriveUrl = testFile,
                SheetName = "1997"
            }, CancellationToken.None);

            var okWatcher = Assert.IsType<OkObjectResult>(watcherAction.Result);
            var watcherData = Assert.IsType<AutoTickWatcherCheckResponse>(okWatcher.Value);
            Assert.Equal("1997", watcherData.SheetName);
        }
        finally
        {
            if (File.Exists(testFile)) File.Delete(testFile);
        }
    }
}
