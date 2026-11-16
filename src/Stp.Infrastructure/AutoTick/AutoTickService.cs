using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Stp.Contracts;
using Stp.Core.AutoTick;
using Stp.Core.Storage;

namespace Stp.Infrastructure.AutoTick;

public sealed partial class AutoTickService : IAutoTickService
{
    private readonly IFileStorage _storage;
    private readonly IAutoTickEventPublisher _eventPublisher;
    private readonly ILogger<AutoTickService> _logger;
    private readonly HttpClient _http;

    private readonly ConcurrentDictionary<Guid, JobState> _jobs = new();
    private static readonly Regex ParenthesesRegex = new(@"\(([^)]+)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    public AutoTickService(
        IFileStorage storage,
        IAutoTickEventPublisher eventPublisher,
        ILogger<AutoTickService> logger)
    {
        _storage = storage;
        _eventPublisher = eventPublisher;
        _logger = logger;

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = new CookieContainer()
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    }

    public async Task<AutoTickInspectResponse> InspectAsync(AutoTickInspectRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OneDriveUrl))
        {
            throw new ArgumentException("Đường dẫn OneDrive không được để trống.", nameof(request));
        }

        var downloaded = await DownloadOneDriveFileAsync(request.OneDriveUrl, ct).ConfigureAwait(false);

        using var workbook = new XLWorkbook(downloaded.FilePath);
        var sheets = workbook.Worksheets
            .Select(w => w.Name)
            .Where(name => !name.Equals("file dán", StringComparison.OrdinalIgnoreCase) &&
                           !name.Equals("form", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var activeSheetName = request.SheetName;
        if (string.IsNullOrWhiteSpace(activeSheetName) || !workbook.Worksheets.Contains(activeSheetName))
        {
            activeSheetName = sheets.FirstOrDefault(s => s.Equals("1997", StringComparison.OrdinalIgnoreCase))
                ?? sheets.FirstOrDefault(s => Regex.IsMatch(s, @"^\d{4}"))
                ?? sheets.FirstOrDefault(s => s.Equals("file nhập", StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.First().Name;
        }

        var sheet = workbook.Worksheet(activeSheetName);
        var headers = ExtractHeaders(sheet);

        // Analyze rows
        int totalRows = 0;
        int rowsWithParens = 0;
        int rowsFullyTicked = 0;
        int rowsPartiallyTicked = 0;
        int rowsUnticked = 0;
        var sampleRows = new List<AutoTickRowPreview>();

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 4;
        for (int r = 5; r <= lastRow; r++)
        {
            var summary = sheet.Cell(r, 4).GetString().Trim(); // Col D
            if (string.IsNullOrWhiteSpace(summary)) continue;

            totalRows++;
            var items = ExtractDocumentItemsFromSummary(summary);
            bool hasParens = items.Count > 0;
            int tickedInRow = 0;

            // Check tick counts
            foreach (var h in headers)
            {
                if (IsCellTicked(sheet.Cell(r, h.ColumnIndex)))
                {
                    tickedInRow++;
                }
            }

            if (hasParens)
            {
                rowsWithParens++;

                // Match items against headers
                var requiredHeaders = FindMatchingHeaders(items, headers);
                int requiredCount = requiredHeaders.Count;
                int requiredTicked = requiredHeaders.Count(h => IsCellTicked(sheet.Cell(r, h.ColumnIndex)));

                if (requiredCount > 0 && requiredTicked >= requiredCount)
                {
                    rowsFullyTicked++;
                }
                else if (requiredTicked > 0)
                {
                    rowsPartiallyTicked++;
                }
                else
                {
                    rowsUnticked++;
                }
            }

            if (sampleRows.Count < 30)
            {
                var citizenNames = ExtractCitizenNames(summary);
                var numLabel = sheet.Cell(r, 2).GetString().Trim(); // Col B
                sampleRows.Add(new AutoTickRowPreview
                {
                    RowIndex = r,
                    NumberLabel = numLabel,
                    CitizenNames = citizenNames,
                    SummaryText = summary,
                    HasParentheses = hasParens,
                    ExtractedItems = items,
                    TickedCount = tickedInRow,
                    IsFullyTicked = hasParens && tickedInRow >= items.Count && items.Count > 0
                });
            }
        }

        return new AutoTickInspectResponse
        {
            FileName = downloaded.FileName,
            OneDriveUrl = request.OneDriveUrl,
            UserCanWrite = downloaded.UserCanWrite,
            ETag = downloaded.ETag,
            Sheets = sheets,
            ActiveSheet = activeSheetName,
            TotalRows = totalRows,
            RowsWithParentheses = rowsWithParens,
            RowsFullyTicked = rowsFullyTicked,
            RowsPartiallyTicked = rowsPartiallyTicked,
            RowsUnticked = rowsUnticked,
            DocumentHeaders = headers,
            SampleRows = sampleRows
        };
    }

    public async Task<Guid> StartJobAsync(AutoTickStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OneDriveUrl))
        {
            throw new ArgumentException("Đường dẫn OneDrive không được để trống.", nameof(request));
        }

        var jobId = Guid.NewGuid();
        var cts = new CancellationTokenSource();

        var state = new JobState
        {
            JobId = jobId,
            Status = "Running",
            SheetName = request.SheetName,
            StartedAt = DateTimeOffset.UtcNow,
            Cts = cts
        };
        _jobs[jobId] = state;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteJobAsync(jobId, request, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi thực thi AutoTick job {JobId}", jobId);
                state.Status = "Failed";
                state.ErrorMessage = ex.Message;
                state.FinishedAt = DateTimeOffset.UtcNow;

                await _eventPublisher.PublishProgressAsync(new AutoTickProgressEvent
                {
                    JobId = jobId,
                    SheetName = request.SheetName,
                    IsCompleted = true,
                    ErrorMessage = ex.Message
                }).ConfigureAwait(false);
            }
        }, CancellationToken.None);

        return jobId;
    }

    public Task<AutoTickJobStatusResponse?> GetJobStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            return Task.FromResult<AutoTickJobStatusResponse?>(null);
        }

        var response = new AutoTickJobStatusResponse
        {
            JobId = state.JobId,
            Status = state.Status,
            SheetName = state.SheetName,
            ProcessedRows = state.ProcessedRows,
            TotalRows = state.TotalRows,
            NewlyTickedRowsCount = state.NewlyTickedRowsCount,
            NewlyTickedCellsCount = state.NewlyTickedCellsCount,
            SkippedRowsCount = state.SkippedRowsCount,
            Percent = state.TotalRows > 0 ? (state.ProcessedRows * 100 / state.TotalRows) : 0,
            StartedAt = state.StartedAt,
            FinishedAt = state.FinishedAt,
            ErrorMessage = state.ErrorMessage,
            RecentRowResults = state.RecentRowResults.ToArray()
        };

        return Task.FromResult<AutoTickJobStatusResponse?>(response);
    }

    public Task<bool> CancelJobAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var state))
        {
            state.Cts?.Cancel();
            state.Status = "Cancelled";
            state.FinishedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<(Stream Stream, string FileName)?> GetResultFileAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var state) && !string.IsNullOrWhiteSpace(state.ResultFilePath) && File.Exists(state.ResultFilePath))
        {
            var stream = File.OpenRead(state.ResultFilePath);
            var fileName = state.ResultFileName ?? $"DK_KET_HON_ticked_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return Task.FromResult<(Stream Stream, string FileName)?>((stream, fileName));
        }

        return Task.FromResult<(Stream Stream, string FileName)?>(null);
    }

    public Task<AutoTickSaveToOneDriveResponse> SaveToLocalOneDriveAsync(Guid jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var state) || string.IsNullOrWhiteSpace(state.ResultFilePath) || !File.Exists(state.ResultFilePath))
        {
            return Task.FromResult(new AutoTickSaveToOneDriveResponse
            {
                Success = false,
                Message = "Không tìm thấy file kết quả của tiến trình này."
            });
        }

        var candidateDirs = new List<string>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            candidateDirs.Add(Path.Combine(userProfile, "OneDrive"));
        }
        var envConsumer = Environment.GetEnvironmentVariable("OneDriveConsumer");
        if (!string.IsNullOrWhiteSpace(envConsumer)) candidateDirs.Add(envConsumer);
        var envOneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(envOneDrive)) candidateDirs.Add(envOneDrive);
        candidateDirs.Add(@"C:\Users\cuong\OneDrive");

        string? targetDir = candidateDirs.FirstOrDefault(Directory.Exists);
        if (targetDir is null)
        {
            return Task.FromResult(new AutoTickSaveToOneDriveResponse
            {
                Success = false,
                Message = "Không tìm thấy thư mục OneDrive trên máy chủ để tự động đồng bộ."
            });
        }

        try
        {
            var targetName = state.ResultFileName ?? "DK KET HON.xlsx";
            var targetPath = Path.Combine(targetDir, targetName);
            File.Copy(state.ResultFilePath, targetPath, overwrite: true);

            var downloadsDir = Path.Combine(targetDir, "Tài liệu", "Downloads");
            if (Directory.Exists(downloadsDir))
            {
                var dlTarget = Path.Combine(downloadsDir, targetName);
                File.Copy(state.ResultFilePath, dlTarget, overwrite: true);
            }

            _logger.LogInformation("Đã ghi đè file kết quả AutoTick vào OneDrive: {Path}", targetPath);

            return Task.FromResult(new AutoTickSaveToOneDriveResponse
            {
                Success = true,
                SavedPath = targetPath,
                Message = $"Đã ghi đè thành công vào: {targetPath}. OneDrive trên máy sẽ tự động đồng bộ lên cloud trong giây lát!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu file vào thư mục OneDrive máy");
            return Task.FromResult(new AutoTickSaveToOneDriveResponse
            {
                Success = false,
                Message = $"Lỗi ghi file vào OneDrive: {ex.Message}"
            });
        }
    }

    public async Task<AutoTickWatcherCheckResponse> CheckWatcherAsync(AutoTickWatcherCheckRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OneDriveUrl))
        {
            return new AutoTickWatcherCheckResponse { HasChanges = false };
        }

        try
        {
            var downloaded = await DownloadOneDriveFileAsync(request.OneDriveUrl, ct).ConfigureAwait(false);
            bool etagChanged = !string.IsNullOrWhiteSpace(downloaded.ETag) &&
                               !string.IsNullOrWhiteSpace(request.LastETag) &&
                               !downloaded.ETag.Equals(request.LastETag, StringComparison.OrdinalIgnoreCase);

            using var workbook = new XLWorkbook(downloaded.FilePath);
            var sheetName = request.SheetName;
            if (string.IsNullOrWhiteSpace(sheetName) || !workbook.Worksheets.Contains(sheetName))
            {
                sheetName = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("1997", StringComparison.OrdinalIgnoreCase))?.Name
                    ?? workbook.Worksheets.First().Name;
            }

            var sheet = workbook.Worksheet(sheetName);
            var headers = ExtractHeaders(sheet);
            int maxUsedColWatcher = 0;
            if (sheet.LastColumnUsed() is not null)
            {
                maxUsedColWatcher = Math.Max(maxUsedColWatcher, sheet.LastColumnUsed()!.ColumnNumber());
            }
            if (sheet.Row(4).LastCellUsed() is not null)
            {
                maxUsedColWatcher = Math.Max(maxUsedColWatcher, sheet.Row(4).LastCellUsed()!.Address.ColumnNumber);
            }
            if (headers.Count > 0)
            {
                maxUsedColWatcher = Math.Max(maxUsedColWatcher, headers.Max(h => h.ColumnIndex));
            }
            int lastColNumber = maxUsedColWatcher > 0 ? maxUsedColWatcher : 95;
            string lastColLetter = sheet.Column(lastColNumber).ColumnLetter();
            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 4;
            int newUnticked = 0;

            int startScan = request.KnownRowCount.HasValue && request.KnownRowCount.Value > 4
                ? Math.Max(5, request.KnownRowCount.Value - 10)
                : 5;

            for (int r = startScan; r <= lastRow; r++)
            {
                var summary = sheet.Cell(r, 4).GetString().Trim();
                if (string.IsNullOrWhiteSpace(summary)) continue;

                var items = ExtractDocumentItemsFromSummary(summary);
                if (items.Count == 0) continue;

                var requiredHeaders = FindMatchingHeaders(items, headers);
                if (requiredHeaders.Count > 0 && requiredHeaders.Any(h => !IsCellTicked(sheet.Cell(r, h.ColumnIndex))))
                {
                    newUnticked++;
                }
            }

            bool hasChanges = etagChanged || newUnticked > 0;
            return new AutoTickWatcherCheckResponse
            {
                HasChanges = hasChanges,
                LatestETag = downloaded.ETag,
                NewUntickedRowsCount = newUnticked,
                SheetName = sheetName,
                CheckedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi kiểm tra watcher OneDrive");
            return new AutoTickWatcherCheckResponse { HasChanges = false };
        }
    }

    public bool EnqueueOneDriveJob(string oneDriveUrl)
    {
        if (string.IsNullOrWhiteSpace(oneDriveUrl)) return false;
        var key = NormalizeUrlKey(oneDriveUrl);
        int count = _activeJobCounts.AddOrUpdate(key, 1, (_, current) => current + 1);
        return count > 1;
    }

    public void DequeueOneDriveJob(string oneDriveUrl)
    {
        if (string.IsNullOrWhiteSpace(oneDriveUrl)) return;
        var key = NormalizeUrlKey(oneDriveUrl);
        _activeJobCounts.AddOrUpdate(key, 0, (_, current) => Math.Max(0, current - 1));
    }

    public bool IsOneDriveBusy(string oneDriveUrl)
    {
        if (string.IsNullOrWhiteSpace(oneDriveUrl)) return false;
        var key = NormalizeUrlKey(oneDriveUrl);
        if (_activeJobCounts.TryGetValue(key, out int count) && count > 0)
        {
            return true;
        }
        return _oneDriveUrlSemaphores.TryGetValue(key, out var sem) && sem.CurrentCount == 0;
    }

    public async Task<AutoTickSyncSummaryResponse> SyncSummaryRowsToOneDriveAsync(
        AutoTickSyncSummaryRequest request,
        Func<Task>? onLockAcquired = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Rows.Count == 0)
        {
            return new AutoTickSyncSummaryResponse
            {
                Success = false,
                Message = "Không có dòng nào để đồng bộ."
            };
        }

        if (string.IsNullOrWhiteSpace(request.OneDriveUrl))
        {
            return new AutoTickSyncSummaryResponse
            {
                Success = false,
                Message = "Vui lòng nhập đường dẫn liên kết OneDrive hoặc Excel Online."
            };
        }

        var url = request.OneDriveUrl.Trim();
        var sem = _oneDriveUrlSemaphores.GetOrAdd(NormalizeUrlKey(url), _ => new SemaphoreSlim(1, 1));
        bool isWaitingInQueue = sem.CurrentCount == 0;
        if (isWaitingInQueue)
        {
            _logger.LogInformation("File {Url} đang có robot chạy, tiến trình mới xếp hàng chờ...", url);
        }
        _logger.LogInformation("Chờ khóa hàng đợi đồng bộ OneDrive cho file {Url}...", url);
        var acquired = await sem.WaitAsync(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        if (!acquired)
        {
            return new AutoTickSyncSummaryResponse
            {
                Success = false,
                Message = "Robot hiện đang bận đồng bộ file này cho một đồng đội khác trong phòng. Vui lòng thử lại sau giây lát."
            };
        }

        try
        {
            if (isWaitingInQueue && onLockAcquired is not null)
            {
                try
                {
                    await onLockAcquired().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi gọi onLockAcquired callback");
                }
            }

            // 1. Download & inspect workbook to map headers and row coordinates
            var downloaded = await DownloadOneDriveFileAsync(url, ct).ConfigureAwait(false);
            using var workbook = new XLWorkbook(downloaded.FilePath);

            IXLWorksheet? targetSheet = null;

            // 1.1 Thử tìm sheet theo đúng tên request.SheetName (bỏ qua hoa thường và khoảng trắng thừa)
            if (!string.IsNullOrWhiteSpace(request.SheetName))
            {
                var cleanReqSheet = request.SheetName.Trim();
                targetSheet = workbook.Worksheets.FirstOrDefault(w =>
                    string.Equals(w.Name.Trim(), cleanReqSheet, StringComparison.OrdinalIgnoreCase));
            }

            // Nếu các row gửi kèm có SheetName đồng nhất (ví dụ "file nhập" hoặc "1996"), ưu tiên sheet đó
            var rowSheetGroup = request.Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.SheetName))
                .Select(r => r.SheetName!.Trim())
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (rowSheetGroup is not null)
            {
                var rowWs = workbook.Worksheets.FirstOrDefault(w =>
                    string.Equals(w.Name.Trim(), rowSheetGroup.Key, StringComparison.OrdinalIgnoreCase));
                if (rowWs is not null)
                {
                    targetSheet = rowWs;
                }
            }

            int targetMatches = targetSheet is not null ? CountMatchingRowsInSheet(targetSheet, request.Rows) : 0;

            // 1.2 Tự động kiểm tra độ khớp: nếu targetSheet chưa thấy hoặc chưa khớp đủ số dòng, quét toàn bộ workbook
            if (targetSheet is null || targetMatches < request.Rows.Count)
            {
                IXLWorksheet? bestSheet = targetSheet;
                int maxMatches = targetMatches;

                foreach (var ws in workbook.Worksheets)
                {
                    var wsName = ws.Name.Trim();
                    if (wsName.Equals("file dán", StringComparison.OrdinalIgnoreCase) ||
                        wsName.Equals("form", StringComparison.OrdinalIgnoreCase) ||
                        wsName.Equals("BANG_IN_HANG_LOAT", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int matches = CountMatchingRowsInSheet(ws, request.Rows);
                    if (matches > maxMatches)
                    {
                        maxMatches = matches;
                        bestSheet = ws;
                    }
                }

                if (bestSheet is not null && maxMatches > targetMatches)
                {
                    _logger.LogInformation("Tự động nhận diện sheet tối ưu: '{SheetName}' chứa {Matches}/{Total} hồ sơ cần đồng bộ (thay cho sheet cũ {OldSheet})",
                        bestSheet.Name, maxMatches, request.Rows.Count, targetSheet?.Name ?? request.SheetName);
                    targetSheet = bestSheet;
                }
            }

            targetSheet ??= workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("1997", StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.FirstOrDefault(w => !w.Name.Trim().Equals("file dán", StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.First();

            var sheet = targetSheet;
            var sheetName = targetSheet.Name;
            var originalHeaders = ExtractHeaders(sheet);
            var headers = originalHeaders.ToList();

            // Đảm bảo tuyệt đối: Cột mới luôn được thêm ở ngoài cùng bên phải (sau tất cả các cột của bảng gốc),
            // không chèn vào giữa, không dịch chuyển cột cũ, không làm xáo trộn dữ liệu hay hỏng các công thức sẵn có.
            int maxUsedCol = 0;
            if (sheet.LastColumnUsed() is not null)
            {
                maxUsedCol = Math.Max(maxUsedCol, sheet.LastColumnUsed()!.ColumnNumber());
            }
            if (sheet.Row(4).LastCellUsed() is not null)
            {
                maxUsedCol = Math.Max(maxUsedCol, sheet.Row(4).LastCellUsed()!.Address.ColumnNumber);
            }
            if (headers.Count > 0)
            {
                maxUsedCol = Math.Max(maxUsedCol, headers.Max(h => h.ColumnIndex));
            }
            int lastColNumber = maxUsedCol > 0 ? maxUsedCol : 95;

            bool skipTicking = request.SkipTicking;
            string defaultStatusA = !string.IsNullOrWhiteSpace(request.StatusA) ? request.StatusA : "đã nhập";

            // Người dùng yêu cầu: Tuyệt đối không thêm cột mới, có tiêu đề nào thì đánh tiêu đề đó
            var newHeadersToAdd = new List<object>();
            var newHeaderTitles = new List<string>();

            string lastColLetter = sheet.Column(lastColNumber).ColumnLetter();

            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 500;
            var numToRowMap = new Dictionary<int, int>();
            var labelToRowMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var citizenNameToRowMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int r = 5; r <= lastRow; r++)
            {
                var numStr = GetCellText(sheet.Cell(r, 2));
                if (!string.IsNullOrWhiteSpace(numStr))
                {
                    labelToRowMap.TryAdd(numStr.Trim(), r);
                    var m = Regex.Match(numStr, @"^\d+");
                    if (m.Success && int.TryParse(m.Value, out int n))
                    {
                        numToRowMap.TryAdd(n, r);
                    }
                }

                var summaryInSheet = GetCellText(sheet.Cell(r, 4));
                if (!string.IsNullOrWhiteSpace(summaryInSheet))
                {
                    var names = ExtractCitizenNames(summaryInSheet);
                    if (!string.IsNullOrWhiteSpace(names))
                    {
                        citizenNameToRowMap.TryAdd(NormalizeText(names), r);
                    }
                }
            }

            var processedRows = new List<object>();
            foreach (var rItem in request.Rows)
            {
                int rowIndex = 0;

                // Tầng 1: Khớp theo nhãn chuỗi đầy đủ cột B (ví dụ "36A", "14")
                if (!string.IsNullOrWhiteSpace(rItem.NumberLabel) && labelToRowMap.TryGetValue(rItem.NumberLabel.Trim(), out int mappedByLabel))
                {
                    rowIndex = mappedByLabel;
                }

                // Tầng 2: Khớp theo số nguyên bắt đầu cột B (ví dụ 14, 36)
                if (rowIndex == 0 && !string.IsNullOrWhiteSpace(rItem.NumberLabel))
                {
                    var numMatch = Regex.Match(rItem.NumberLabel, @"^\d+");
                    if (numMatch.Success && int.TryParse(numMatch.Value, out int n) && numToRowMap.TryGetValue(n, out int mappedByNum))
                    {
                        rowIndex = mappedByNum;
                    }
                }

                // Tầng 3: Khớp theo tên công dân trích xuất từ trích yếu (Cột D)
                if (rowIndex == 0 && !string.IsNullOrWhiteSpace(rItem.SummaryText))
                {
                    var names = ExtractCitizenNames(rItem.SummaryText);
                    if (!string.IsNullOrWhiteSpace(names) && citizenNameToRowMap.TryGetValue(NormalizeText(names), out int mappedByName))
                    {
                        rowIndex = mappedByName;
                    }
                }

                // Tầng 4: Dùng trực tiếp RowIndex nếu nằm trong khoảng dòng hợp lệ (>= 5)
                if (rowIndex == 0 && rItem.RowIndex >= 5 && rItem.RowIndex <= lastRow)
                {
                    rowIndex = rItem.RowIndex;
                }

                // Tầng 5: Nếu RowIndex là 1-based (ví dụ 1..N) chuyển về dòng Excel (rItem.RowIndex + 4)
                if (rowIndex == 0 && rItem.RowIndex >= 1 && (rItem.RowIndex + 4) <= lastRow)
                {
                    rowIndex = rItem.RowIndex + 4;
                }

                if (rowIndex <= 0)
                {
                    _logger.LogWarning("Không tìm thấy dòng tương ứng trên sheet {Sheet} cho hồ sơ {NumberLabel} (RowIndex: {RowIndex})",
                        sheetName, rItem.NumberLabel, rItem.RowIndex);
                    continue;
                }

                var targetCells = new List<string>();
                var untickCells = new List<string>();

                if (!skipTicking)
                {
                    if (rItem.TargetCells is not null && rItem.TargetCells.Count > 0)
                    {
                        foreach (var c in rItem.TargetCells)
                        {
                            var cell = sheet.Cell(c);
                            if (!IsCellTicked(cell)) targetCells.Add(c);
                        }
                    }
                    else
                    {
                        var desiredDocs = new List<string>();
                        // Người dùng yêu cầu: "đánh tick thì nhìn vào chữ mà làm" - trích xuất trực tiếp giấy tờ từ trích yếu chữ
                        if (!string.IsNullOrWhiteSpace(rItem.SummaryText))
                        {
                            var docs = ExtractDocumentItemsFromSummary(rItem.SummaryText);
                            if (docs.Count > 0)
                            {
                                desiredDocs.AddRange(docs);
                            }
                        }

                        if (desiredDocs.Count == 0 && rItem.Items.Count > 0)
                        {
                            desiredDocs.AddRange(rItem.Items);
                        }

                        // Khớp với toàn bộ headers (bao gồm cả các cột mới vừa thêm) để tick thành TRUE theo yêu cầu
                        var desiredHeaders = FindMatchingHeaders(desiredDocs, headers);
                        foreach (var h in headers)
                        {
                            var cell = sheet.Cell(rowIndex, h.ColumnIndex);
                            bool isCurrentlyTicked = IsCellTicked(cell);
                            bool isDesired = desiredHeaders.Contains(h);

                            if (isDesired && !isCurrentlyTicked)
                            {
                                targetCells.Add($"{h.ColumnLetter}{rowIndex}");
                            }
                            else if (!isDesired && isCurrentlyTicked && originalHeaders.Contains(h))
                            {
                                untickCells.Add($"{h.ColumnLetter}{rowIndex}");
                            }
                        }
                    }
                }

                string statusA = !string.IsNullOrWhiteSpace(rItem.StatusA) ? rItem.StatusA : defaultStatusA;
                string? formulaH = null;
                if (!string.IsNullOrWhiteSpace(rItem.FormulaH))
                {
                    formulaH = rItem.FormulaH;
                }
                else if (request.EnsureFormulaH)
                {
                    bool needsFormulaUpdate = false;
                    var cellH = sheet.Cell(rowIndex, 8);
                    if (!cellH.HasFormula)
                    {
                        needsFormulaUpdate = true;
                    }
                    else
                    {
                        try
                        {
                            string existingFormula = cellH.FormulaA1;
                            // Kiểm tra thông minh: chỉ điền lại nếu công thức bị thiếu cột cuối hoặc tham chiếu sai dòng
                            if (string.IsNullOrWhiteSpace(existingFormula) ||
                                !existingFormula.Contains(lastColLetter, StringComparison.OrdinalIgnoreCase) ||
                                !existingFormula.Contains($"D{rowIndex}", StringComparison.OrdinalIgnoreCase))
                            {
                                needsFormulaUpdate = true;
                            }
                        }
                        catch
                        {
                            // Nếu ClosedXML không đọc được formula phức tạp, an toàn là làm mới lại
                            needsFormulaUpdate = true;
                        }
                    }

                    if (needsFormulaUpdate)
                    {
                        formulaH = $"=\"Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn với người nước ngoài của \"&TEXTBEFORE(TEXTAFTER(D{rowIndex}, \"của \"), \" và\")&\" và  \"&TEXTBEFORE(TEXTAFTER(D{rowIndex}, \"và \"), \".\")&\" (\" & TEXTJOIN(\", \", TRUE, REPT(I$4:{lastColLetter}$4, I{rowIndex}:{lastColLetter}{rowIndex}=TRUE)) & \")\"";
                    }
                }
                else
                {
                    // Người dùng yêu cầu: "chỉ cần đánh cột D và H , lấy dữ liệu vừa xong trong web , đẩy dữ liệu vào 2 cột đó"
                    formulaH = rItem.SummaryText;
                }

                processedRows.Add(new
                {
                    rowIndex,
                    numberLabel = rItem.NumberLabel,
                    summaryText = rItem.SummaryText,
                    formulaH,
                    statusA,
                    targetCells,
                    untickCells
                });
            }

            if (processedRows.Count == 0)
            {
                return new AutoTickSyncSummaryResponse
                {
                    Success = false,
                    Message = "Không xác định được số hàng tương ứng trên sheet để đồng bộ."
                };
            }

            // Nếu là file cục bộ (không bắt đầu bằng http/https), áp dụng thay đổi trực tiếp qua ClosedXML và lưu file
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(downloaded.FilePath))
            {
                int localSynced = 0;
                int localTicked = 0;
                var localDetails = new List<AutoTickSyncSummaryRowResult>();

                foreach (var pObj in processedRows)
                {
                    var pType = pObj.GetType();
                    int rIdx = (int)pType.GetProperty("rowIndex")!.GetValue(pObj)!;
                    string nLabel = (string)pType.GetProperty("numberLabel")!.GetValue(pObj)!;
                    string sText = (string)pType.GetProperty("summaryText")!.GetValue(pObj)!;
                    string? fH = (string?)pType.GetProperty("formulaH")!.GetValue(pObj);
                    string? sA = (string?)pType.GetProperty("statusA")?.GetValue(pObj);
                    var tCells = (List<string>)pType.GetProperty("targetCells")!.GetValue(pObj)!;
                    var uCells = (List<string>)pType.GetProperty("untickCells")!.GetValue(pObj)!;

                    if (!string.IsNullOrWhiteSpace(sA))
                    {
                        sheet.Cell(rIdx, 1).SetValue(sA);
                    }
                    if (!string.IsNullOrWhiteSpace(sText))
                    {
                        sheet.Cell(rIdx, 4).SetValue(sText);
                    }
                    if (!string.IsNullOrWhiteSpace(fH))
                    {
                        if (fH.StartsWith("="))
                        {
                            sheet.Cell(rIdx, 8).FormulaA1 = fH;
                        }
                        else
                        {
                            sheet.Cell(rIdx, 8).SetValue(fH);
                        }
                    }
                    if (!skipTicking)
                    {
                        foreach (var c in tCells)
                        {
                            sheet.Cell(c).SetValue(true);
                            localTicked++;
                        }
                        foreach (var c in uCells)
                        {
                            sheet.Cell(c).SetValue(false);
                        }
                    }

                    localSynced++;
                    localDetails.Add(new AutoTickSyncSummaryRowResult
                    {
                        RowIndex = rIdx,
                        NumberLabel = nLabel,
                        StatusAFilled = !string.IsNullOrWhiteSpace(sA),
                        SummaryFilled = !string.IsNullOrWhiteSpace(sText),
                        FormulaHFilled = !string.IsNullOrWhiteSpace(fH),
                        TickedColumns = tCells,
                        UntickedColumns = uCells
                    });
                }

                workbook.SaveAs(downloaded.FilePath);

                string localMsg = skipTicking
                    ? $"Đã đồng bộ thành công {localSynced} hồ sơ: điền Cột D, H và đánh dấu Cột A là '{defaultStatusA}' vào file!"
                    : $"Đã đồng bộ thành công {localSynced} hồ sơ, tick {localTicked} ô" +
                        (newHeadersToAdd.Count > 0 ? $", tự động thêm {newHeadersToAdd.Count} cột mới" : "") + " vào file!";

                return new AutoTickSyncSummaryResponse
                {
                    Success = true,
                    SyncedRowsCount = localSynced,
                    TickedCellsCount = localTicked,
                    NewHeadersCount = newHeadersToAdd.Count,
                    NewHeaderTitles = newHeaderTitles,
                    Message = localMsg,
                    Details = localDetails
                };
            }

            // 2. Prepare JSON for Python Playwright worker
            var tempDir = Path.Combine(Path.GetTempPath(), "stp_autotick_sync");
            Directory.CreateDirectory(tempDir);
            var inputPath = Path.Combine(tempDir, $"in_{Guid.NewGuid():N}.json");
            var outputPath = Path.Combine(tempDir, $"out_{Guid.NewGuid():N}.json");

            var payloadObj = new
            {
                oneDriveUrl = url,
                sheetName,
                skipTicking,
                newHeaders = newHeadersToAdd,
                rows = processedRows
            };

            await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(payloadObj), ct).ConfigureAwait(false);

            // 3. Locate worker script
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "AutoTick", "sync_onedrive_worker.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Stp.Infrastructure", "AutoTick", "sync_onedrive_worker.py"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Stp.Infrastructure", "AutoTick", "sync_onedrive_worker.py")
            };
            var scriptPath = candidates.FirstOrDefault(File.Exists) ?? candidates[0];

            var psi = new ProcessStartInfo
            {
                FileName = ResolvePythonExecutable(),
                Arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            ConfigurePlaywrightEnvironment(psi);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return new AutoTickSyncSummaryResponse
                {
                    Success = false,
                    Message = "Không thể khởi động trình duyệt robot đồng bộ."
                };
            }

            var stdoutSb = new StringBuilder();
            var stderrSb = new StringBuilder();

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data is not null)
                {
                    lock (stdoutSb) stdoutSb.AppendLine(e.Data);
                    _logger.LogDebug("[Robot-Out] {Data}", e.Data);
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data is not null)
                {
                    lock (stderrSb) stderrSb.AppendLine(e.Data);
                    _logger.LogWarning("[Robot-Err] {Data}", e.Data);
                }
            };

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var procCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            procCts.CancelAfter(TimeSpan.FromMinutes(6)); // Safety timeout 6 mins

            try
            {
                await proc.WaitForExitAsync(procCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new AutoTickSyncSummaryResponse
                {
                    Success = false,
                    Message = "Robot chạy quá thời gian tối đa (6 phút) nên đã tự động dừng để giải phóng hàng đợi."
                };
            }

            if (File.Exists(outputPath))
            {
                var json = await File.ReadAllTextAsync(outputPath, ct).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<AutoTickSyncSummaryResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                try { File.Delete(inputPath); File.Delete(outputPath); } catch { }

                if (result != null)
                {
                    if (result.NewHeadersCount == 0 && newHeadersToAdd.Count > 0)
                    {
                        result.NewHeadersCount = newHeadersToAdd.Count;
                        result.NewHeaderTitles = newHeaderTitles;
                    }
                    return result;
                }

                return new AutoTickSyncSummaryResponse
                {
                    Success = false,
                    Message = "Không đọc được kết quả đồng bộ từ robot."
                };
            }

            string errText;
            lock (stderrSb) errText = stderrSb.ToString();
            if (string.IsNullOrWhiteSpace(errText))
            {
                lock (stdoutSb) errText = stdoutSb.ToString();
            }

            return new AutoTickSyncSummaryResponse
            {
                Success = false,
                Message = $"Robot gặp lỗi: {errText}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi đồng bộ trích yếu và tick OneDrive");
            return new AutoTickSyncSummaryResponse
            {
                Success = false,
                Message = $"Lỗi thực thi đồng bộ: {ex.Message}"
            };
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task ExecuteJobAsync(Guid jobId, AutoTickStartRequest request, CancellationToken ct)
    {
        if (!_jobs.TryGetValue(jobId, out var state)) return;

        var downloaded = await DownloadOneDriveFileAsync(request.OneDriveUrl, ct).ConfigureAwait(false);
        state.ResultFileName = downloaded.FileName;

        using var workbook = new XLWorkbook(downloaded.FilePath);
        var sheetName = request.SheetName;
        if (string.IsNullOrWhiteSpace(sheetName) || !workbook.Worksheets.Contains(sheetName))
        {
            sheetName = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("1997", StringComparison.OrdinalIgnoreCase))?.Name
                ?? workbook.Worksheets.First().Name;
        }

        var sheet = workbook.Worksheet(sheetName);
        var allHeaders = ExtractHeaders(sheet);

        // Đảm bảo tuyệt đối: Thêm cột mới ở ngoài cùng bên phải, không chèn vào giữa
        int maxUsedColJob = 0;
        if (sheet.LastColumnUsed() is not null)
        {
            maxUsedColJob = Math.Max(maxUsedColJob, sheet.LastColumnUsed()!.ColumnNumber());
        }
        if (sheet.Row(4).LastCellUsed() is not null)
        {
            maxUsedColJob = Math.Max(maxUsedColJob, sheet.Row(4).LastCellUsed()!.Address.ColumnNumber);
        }
        if (allHeaders.Count > 0)
        {
            maxUsedColJob = Math.Max(maxUsedColJob, allHeaders.Max(h => h.ColumnIndex));
        }
        int jobLastColNumber = maxUsedColJob > 0 ? maxUsedColJob : 95;

        // Filter selected headers if provided
        var selectedHeaderSet = request.SelectedHeaders is not null && request.SelectedHeaders.Count > 0
            ? new HashSet<string>(request.SelectedHeaders.Select(NormalizeText), StringComparer.OrdinalIgnoreCase)
            : null;

        var targetHeaders = selectedHeaderSet is not null
            ? allHeaders.Where(h => selectedHeaderSet.Contains(NormalizeText(h.Title))).ToList()
            : allHeaders;

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 4;
        var eligibleRowNumbers = new List<int>();

        for (int r = 5; r <= lastRow; r++)
        {
            var summary = sheet.Cell(r, 4).GetString().Trim();
            if (string.IsNullOrWhiteSpace(summary)) continue;

            // Number filter
            if (request.NumberFrom.HasValue || request.NumberTo.HasValue)
            {
                var numStr = sheet.Cell(r, 2).GetString().Trim();
                var numMatch = Regex.Match(numStr, @"^\d+");
                if (numMatch.Success && int.TryParse(numMatch.Value, out int num))
                {
                    if (request.NumberFrom.HasValue && num < request.NumberFrom.Value) continue;
                    if (request.NumberTo.HasValue && num > request.NumberTo.Value) continue;
                }
            }

            eligibleRowNumbers.Add(r);
        }

        state.TotalRows = eligibleRowNumbers.Count;

        DateTime lastEventTime = DateTime.UtcNow;
        var cloudRows = new List<object>();
        var newHeadersToAddJob = new List<object>();
        bool isOnlineOneDrive = request.OneDriveUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        bool skipTicking = request.SkipTicking;
        string statusA = !string.IsNullOrWhiteSpace(request.StatusA) ? request.StatusA : "đã nhập";

        foreach (var r in eligibleRowNumbers)
        {
            if (ct.IsCancellationRequested)
            {
                state.Status = "Cancelled";
                state.FinishedAt = DateTimeOffset.UtcNow;
                break;
            }

            var summary = sheet.Cell(r, 4).GetString().Trim();
            var citizenNames = ExtractCitizenNames(summary);
            var numLabel = sheet.Cell(r, 2).GetString().Trim();

            var newlyTickedCols = new List<string>();
            var alreadyTickedCols = new List<string>();
            var unmatchedItems = new List<string>();
            var items = new List<string>();
            string rowStatus;

            if (skipTicking)
            {
                // Người dùng yêu cầu: "thôi bây giờ thay đổi toàn bộ cách làm , bỏ tick , chỉ cần đánh cột D và H , lấy dữ liệu vừa xong trong web , đẩy dữ liệu vào 2 cột đó , và đánh cột A là đã nhập"
                sheet.Cell(r, 1).SetValue(statusA);
                sheet.Cell(r, 4).SetValue(summary);
                sheet.Cell(r, 8).SetValue(summary);

                newlyTickedCols.Add("Cột A, D, H");
                state.NewlyTickedRowsCount++;

                cloudRows.Add(new
                {
                    rowIndex = r,
                    numberLabel = numLabel,
                    summaryText = summary,
                    formulaH = summary,
                    statusA
                });

                rowStatus = "Ticked";
            }
            else
            {
                items = ExtractDocumentItemsFromSummary(summary);
                if (items.Count == 0)
                {
                    state.ProcessedRows++;
                    state.SkippedRowsCount++;
                    continue;
                }

                var matchingHeaders = FindMatchingHeaders(items, targetHeaders);

                foreach (var h in matchingHeaders)
                {
                    var cell = sheet.Cell(r, h.ColumnIndex);
                    if (IsCellTicked(cell))
                    {
                        alreadyTickedCols.Add(h.Title);
                    }
                    else
                    {
                        // Tick the missing cell
                        cell.SetValue(true);
                        newlyTickedCols.Add(h.Title);
                        state.NewlyTickedCellsCount++;
                    }
                }

                if (newlyTickedCols.Count > 0)
                {
                    rowStatus = "Ticked";
                    state.NewlyTickedRowsCount++;

                    var targetCells = allHeaders
                        .Where(h => newlyTickedCols.Contains(h.Title))
                        .Select(h => $"{h.ColumnLetter}{r}")
                        .ToList();

                    cloudRows.Add(new
                    {
                        rowIndex = r,
                        numberLabel = numLabel,
                        targetCells
                    });
                }
                else
                {
                    rowStatus = "SkippedFullyTicked";
                    state.SkippedRowsCount++;
                }
            }

            state.ProcessedRows++;

            var rowResult = new AutoTickRowResult
            {
                RowIndex = r,
                NumberLabel = numLabel,
                CitizenNames = citizenNames,
                SummaryText = summary,
                ExtractedItems = items,
                NewlyTickedColumns = newlyTickedCols,
                AlreadyTickedColumns = alreadyTickedCols,
                UnmatchedItems = unmatchedItems,
                Status = rowStatus
            };

            state.RecentRowResults.Enqueue(rowResult);
            while (state.RecentRowResults.Count > 50) state.RecentRowResults.TryDequeue(out _);

            // Emit live progress every row or at least 200ms
            if (!isOnlineOneDrive && ((DateTime.UtcNow - lastEventTime).TotalMilliseconds >= 180 || state.ProcessedRows == state.TotalRows))
            {
                lastEventTime = DateTime.UtcNow;
                int percent = state.TotalRows > 0 ? (state.ProcessedRows * 100 / state.TotalRows) : 0;
                await _eventPublisher.PublishProgressAsync(new AutoTickProgressEvent
                {
                    JobId = jobId,
                    SheetName = sheetName,
                    ProcessedRows = state.ProcessedRows,
                    TotalRows = state.TotalRows,
                    NewlyTickedRowsCount = state.NewlyTickedRowsCount,
                    NewlyTickedCellsCount = state.NewlyTickedCellsCount,
                    SkippedRowsCount = state.SkippedRowsCount,
                    Percent = percent,
                    CurrentRow = rowResult,
                    IsCompleted = false
                }).ConfigureAwait(false);
            }
        }

        // Save result file
        var outDir = Path.Combine(Path.GetTempPath(), "stp_autotick");
        Directory.CreateDirectory(outDir);
        var resultPath = Path.Combine(outDir, $"{jobId}.xlsx");
        workbook.SaveAs(resultPath);
        state.ResultFilePath = resultPath;

        // Nếu là link OneDrive Online và có các ô cần cập nhật: Chạy Robot Playwright thực tế trên cloud
        if (isOnlineOneDrive && (cloudRows.Count > 0 || newHeadersToAddJob.Count > 0) && !ct.IsCancellationRequested)
        {
            await RunCloudPlaywrightJobAsync(jobId, request.OneDriveUrl, sheetName, cloudRows, newHeadersToAddJob, skipTicking, state, ct).ConfigureAwait(false);
        }

        if (state.Status != "Cancelled")
        {
            state.Status = "Completed";
            state.FinishedAt = DateTimeOffset.UtcNow;

            // Tự động ghi vào OneDrive máy để sync lên cloud ngay lập tức (nếu có thư mục máy)
            try
            {
                await SaveToLocalOneDriveAsync(jobId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể tự động đồng bộ OneDrive máy sau khi hoàn thành");
            }
        }

        await _eventPublisher.PublishProgressAsync(new AutoTickProgressEvent
        {
            JobId = jobId,
            SheetName = sheetName,
            ProcessedRows = state.ProcessedRows,
            TotalRows = state.TotalRows,
            NewlyTickedRowsCount = state.NewlyTickedRowsCount,
            NewlyTickedCellsCount = state.NewlyTickedCellsCount,
            SkippedRowsCount = state.SkippedRowsCount,
            Percent = 100,
            IsCompleted = true
        }).ConfigureAwait(false);
    }

    private async Task RunCloudPlaywrightJobAsync(
        Guid jobId,
        string oneDriveUrl,
        string sheetName,
        List<object> cloudRows,
        List<object> newHeaders,
        bool skipTicking,
        JobState state,
        CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "stp_autotick_job");
        Directory.CreateDirectory(tempDir);
        var inputPath = Path.Combine(tempDir, $"in_{jobId:N}.json");
        var outputPath = Path.Combine(tempDir, $"out_{jobId:N}.json");

        var payloadObj = new
        {
            oneDriveUrl,
            sheetName,
            skipTicking,
            newHeaders,
            rows = cloudRows
        };

        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(payloadObj), ct).ConfigureAwait(false);

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "AutoTick", "sync_onedrive_worker.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Stp.Infrastructure", "AutoTick", "sync_onedrive_worker.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Stp.Infrastructure", "AutoTick", "sync_onedrive_worker.py")
        };
        var scriptPath = candidates.FirstOrDefault(File.Exists) ?? candidates[0];

        var psi = new ProcessStartInfo
        {
            FileName = ResolvePythonExecutable(),
            Arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ConfigurePlaywrightEnvironment(psi);

        var sem = _oneDriveUrlSemaphores.GetOrAdd(NormalizeUrlKey(oneDriveUrl), _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.LogWarning("Không thể khởi động trình duyệt robot Playwright cho job {JobId}", jobId);
                return;
            }

            try
            {
                int cloudDoneRows = 0;
                while (await proc.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    if (line.StartsWith("PROGRESS_TICK:", StringComparison.Ordinal))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 5)
                        {
                            var cellCoord = parts[3];
                            var numLabel = parts[2];
                            var matchRow = state.RecentRowResults.FirstOrDefault(r => r.NumberLabel == numLabel);

                            int pct = Math.Min(98, 10 + (cloudDoneRows * 85 / Math.Max(1, cloudRows.Count)));
                            await _eventPublisher.PublishProgressAsync(new AutoTickProgressEvent
                            {
                                JobId = jobId,
                                SheetName = sheetName,
                                ProcessedRows = cloudDoneRows,
                                TotalRows = cloudRows.Count,
                                NewlyTickedRowsCount = cloudDoneRows,
                                NewlyTickedCellsCount = state.NewlyTickedCellsCount,
                                SkippedRowsCount = state.SkippedRowsCount,
                                Percent = pct,
                                CurrentRow = matchRow,
                                IsCompleted = false
                            }).ConfigureAwait(false);
                        }
                    }
                    else if (line.StartsWith("PROGRESS_ROW:", StringComparison.Ordinal))
                    {
                        cloudDoneRows++;
                        int pct = Math.Min(98, 10 + (cloudDoneRows * 85 / Math.Max(1, cloudRows.Count)));
                        await _eventPublisher.PublishProgressAsync(new AutoTickProgressEvent
                        {
                            JobId = jobId,
                            SheetName = sheetName,
                            ProcessedRows = cloudDoneRows,
                            TotalRows = cloudRows.Count,
                            NewlyTickedRowsCount = cloudDoneRows,
                            NewlyTickedCellsCount = state.NewlyTickedCellsCount,
                            SkippedRowsCount = state.SkippedRowsCount,
                            Percent = pct,
                            IsCompleted = false
                        }).ConfigureAwait(false);
                    }
                }

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong quá trình chạy Robot Playwright AutoTick");
            }
            finally
            {
                try { if (File.Exists(inputPath)) File.Delete(inputPath); } catch { }
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Tự động phát hiện dòng tiêu đề cột từ dòng 1 đến 8 dựa trên mật độ từ khóa tư pháp/hộ tịch,
    /// tránh lỗi gán chết dòng 4 khi người dùng chèn/xóa dòng hoặc tiêu đề bị đẩy lên/xuống.
    /// </summary>
    private static int DetectHeaderRow(IXLWorksheet sheet)
    {
        int bestRow = 4;
        int maxKeywordCount = 0;
        int lastCol = Math.Min(sheet.LastColumnUsed()?.ColumnNumber() ?? 20, 100);

        string[] headerKeywords = new[]
        {
            "khai sinh", "kết hôn", "hôn thú", "hộ khẩu", "nhân khẩu", "chứng minh",
            "căn cước", "cmnd", "cccd", "sức khỏe", "thị thực", "visa", "tuyên thệ",
            "độc thân", "hôn nhân", "công hàm", "trở ngại", "hộ tịch", "lý lịch",
            "quốc tịch", "phiếu", "tờ khai", "đơn", "giấy"
        };

        for (int r = 1; r <= 8; r++)
        {
            int kwCount = 0;
            for (int c = 9; c <= lastCol; c++)
            {
                var txt = sheet.Cell(r, c).GetString().Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(txt)) continue;
                foreach (var kw in headerKeywords)
                {
                    if (txt.Contains(kw))
                    {
                        kwCount++;
                        break;
                    }
                }
            }

            if (kwCount > maxKeywordCount)
            {
                maxKeywordCount = kwCount;
                bestRow = r;
            }
        }

        return maxKeywordCount >= 2 ? bestRow : 4;
    }

    private static List<AutoTickSheetHeader> ExtractHeaders(IXLWorksheet sheet)
    {
        var list = new List<AutoTickSheetHeader>();
        int lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 9;
        int headerRow = DetectHeaderRow(sheet);
        int lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 50, 500);

        for (int c = 9; c <= lastCol; c++) // Start at Col I
        {
            var cell = sheet.Cell(headerRow, c);
            var title = cell.GetString().Trim();

            // Xử lý ô gộp (Merged Cells): Nếu ô ở headerRow rỗng, kiểm tra dòng phía trên
            if (string.IsNullOrWhiteSpace(title) && headerRow > 1)
            {
                var cellAbove = sheet.Cell(headerRow - 1, c).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(cellAbove) && !cellAbove.All(char.IsDigit))
                {
                    title = cellAbove;
                }
            }

            if (string.IsNullOrWhiteSpace(title)) continue;

            title = NormalizeText(title);

            // Count existing ticks
            int tickCount = 0;
            int startCheckRow = headerRow + 1;
            for (int r = startCheckRow; r <= lastRow; r++)
            {
                if (IsCellTicked(sheet.Cell(r, c))) tickCount++;
            }

            list.Add(new AutoTickSheetHeader
            {
                ColumnIndex = c,
                ColumnLetter = sheet.Column(c).ColumnLetter(),
                Title = title,
                ExistingTicksCount = tickCount
            });
        }

        return list;
    }

    private static bool IsCellTicked(IXLCell cell)
    {
        if (cell.IsEmpty()) return false;
        if (cell.DataType == XLDataType.Boolean)
        {
            return cell.GetBoolean();
        }
        var str = cell.GetString().Trim();
        return str.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
               str.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               str == "1" ||
               str.Equals("x", StringComparison.OrdinalIgnoreCase) ||
               str.Equals("v", StringComparison.OrdinalIgnoreCase) ||
               str == "✓";
    }

    /// <summary>
    /// Trích xuất danh sách giấy tờ từ trích yếu một cách thông minh và linh hoạt:
    /// - Hỗ trợ mọi kiểu ngoặc đơn (...), ngoặc vuông [...], ngoặc kép, dấu chấm/phẩy theo sau.
    /// - Hỗ trợ các từ khóa liệt kê giấy tờ ("gồm:", "kèm theo:", "thành phần hồ sơ:", "giấy tờ:").
    /// - Quét ngược từ cuối câu (nơi danh sách giấy tờ thường xuất hiện).
    /// - Tự động tách các mục bằng dấu phẩy, chấm phẩy, dấu cộng, ngắt dòng.
    /// - Tự động sửa lỗi thiếu dấu phẩy giữa các giấy tờ phổ biến (CMND sơ yếu lý lịch -> CMND, sơ yếu lý lịch).
    /// </summary>
    private static List<string> ExtractDocumentItemsFromSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return new List<string>();

        string rawInside = string.Empty;

        // 1. Quét tìm khối ngoặc từ cuối câu trở lên: (...) hoặc [...]
        int lastCloseParen = summary.LastIndexOf(')');
        if (lastCloseParen > 0)
        {
            int openParen = summary.LastIndexOf('(', lastCloseParen - 1);
            if (openParen >= 0 && lastCloseParen > openParen)
            {
                var candidate = summary.Substring(openParen + 1, lastCloseParen - openParen - 1).Trim();
                if (!string.IsNullOrWhiteSpace(candidate) && (candidate.Contains(',') || candidate.Contains(';') || candidate.Length > 2))
                {
                    rawInside = candidate;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(rawInside))
        {
            int lastCloseBracket = summary.LastIndexOf(']');
            if (lastCloseBracket > 0)
            {
                int openBracket = summary.LastIndexOf('[', lastCloseBracket - 1);
                if (openBracket >= 0 && lastCloseBracket > openBracket)
                {
                    var candidate = summary.Substring(openBracket + 1, lastCloseBracket - openBracket - 1).Trim();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        rawInside = candidate;
                    }
                }
            }
        }

        // 2. Nếu không có ngoặc, tìm theo từ khóa liệt kê: "gồm:", "kèm theo:", "thành phần hồ sơ:"
        if (string.IsNullOrWhiteSpace(rawInside))
        {
            var matchKw = Regex.Match(summary, @"(?:gồm|kèm theo|thành phần hồ sơ|giấy tờ kèm|hồ sơ gồm)\s*[:;-]\s*(.+)$", RegexOptions.IgnoreCase);
            if (matchKw.Success)
            {
                rawInside = matchKw.Groups[1].Value.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(rawInside)) return new List<string>();

        // Loại bỏ chú thích đuôi nếu có (ví dụ: "kèm bản chính", "bản gốc đối chiếu", các ký tự chấm cuối...)
        rawInside = Regex.Replace(rawInside, @"[\.;\s]+$", "");

        return SplitDocumentItems(rawInside);
    }

    private static List<string> SplitDocumentItems(string insideParens)
    {
        if (string.IsNullOrWhiteSpace(insideParens)) return new List<string>();

        // Normalize newlines, tabs, and semicolons
        var cleaned = insideParens.Replace("\r", " ").Replace("\n", " ").Replace(";", ",");

        // Handle common OCR / typing errors where commas between items were missed:
        // E.g. "cmnd sơ yếu lí lịch" -> "cmnd, sơ yếu lí lịch"
        cleaned = Regex.Replace(cleaned, @"\b(cmnd|cccd|hộ chiếu)\s+(sơ yếu|giấy|đơn|tờ|phiếu|trích|bản)\b", "$1, $2", RegexOptions.IgnoreCase);

        return cleaned
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => NormalizeText(s))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static List<AutoTickSheetHeader> FindMatchingHeaders(
        IEnumerable<string> items,
        IReadOnlyList<AutoTickSheetHeader> headers)
    {
        var matched = new List<AutoTickSheetHeader>();
        foreach (var item in items)
        {
            var candidates = headers.Where(h => IsSmartMatch(h.Title, item)).ToList();
            if (candidates.Count == 0) continue;

            static string Clean(string s) => Regex.Replace(s.Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"[^\p{L}\p{Nd}]", "");
            var cleanItem = Clean(item);

            // Ưu tiên chọn cột:
            // 1. Trùng khớp chính xác tuyệt đối tên (Exact match)
            // 2. Chưa bị gán cho item nào khác trong cùng hồ sơ (!matched.Contains(h)) để tránh 2 giấy tờ khác nhau nuốt chung 1 cột
            // 3. Tên cột chứa trọn vẹn tên giấy tờ hoặc ngược lại
            // 4. Cột đã có dữ liệu tick thực tế trong sheet
            // 5. Cột xuất hiện sớm hơn trong bảng (vùng hồ sơ chuẩn Col 9..45)
            var best = candidates
                .OrderByDescending(h => Clean(h.Title) == cleanItem)
                .ThenByDescending(h => !matched.Contains(h))
                .ThenByDescending(h => Clean(h.Title).Contains(cleanItem) || cleanItem.Contains(Clean(h.Title)))
                .ThenByDescending(h => h.ExistingTicksCount)
                .ThenBy(h => h.ColumnIndex)
                .First();

            if (!matched.Contains(best))
            {
                matched.Add(best);
            }
        }
        return matched;
    }

    private static readonly Dictionary<string, string> AbbreviationMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "đkkh", "đăng ký kết hôn" },
        { "kkh", "kết hôn" },
        { "gcn", "giấy chứng nhận" },
        { "cn", "chứng nhận" },
        { "gks", "giấy khai sinh" },
        { "ksk", "khám sức khỏe" },
        { "shk", "sổ hộ khẩu" },
        { "hk", "hộ khẩu" },
        { "nxc", "nhập xuất cảnh" },
        { "xnc", "xuất nhập cảnh" },
        { "xn", "xác nhận" },
        { "xndt", "xác nhận độc thân" },
        { "ttdt", "tình trạng độc thân" },
        { "tthn", "tình trạng hôn nhân" },
        { "hntt", "hôn nhân thực tế" },
        { "cmnd", "chứng minh nhân dân" },
        { "cccd", "căn cước công dân" },
        { "syll", "sơ yếu lý lịch" },
        { "ll", "lý lịch" },
        { "uq", "ủy quyền" },
        { "lh", "ly hôn" },
        { "tl", "trích lục" },
        { "bs", "bản sao" },
        { "bc", "bản chính" },
        { "ch", "công hàm" },
        { "gtt", "giấy tờ" },
        { "gt", "giấy tờ" },
        { "tt", "thị thực" }
    };

    private static string ExpandAbbreviations(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var words = Regex.Split(text, @"(\b[a-zA-ZàáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđĐ]+\b)");
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (AbbreviationMap.TryGetValue(w, out var expanded))
            {
                sb.Append(expanded);
            }
            else
            {
                sb.Append(w);
            }
        }
        return sb.ToString();
    }

    private static string RemoveAccents(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('đ', 'd')
            .Replace('Đ', 'D');
    }

    /// <summary>
    /// So khớp thông minh tên giấy tờ trích yếu với tiêu đề cột hồ sơ theo ngữ cảnh hành chính tư pháp:
    /// 1. Chuẩn hóa Form C, chữ thường, loại bỏ ngắt dòng và dấu cách thừa.
    /// 2. Mở rộng từ viết tắt hành chính (ĐKKH, GCN, GKS, KSK, SHK, NXC, XNDT, HNTT, CMND...).
    /// 3. So khớp không phân biệt dấu tiếng Việt (Form D ASCII fallback).
    /// 4. Khử các lỗi chính tả/gõ nhầm phổ biến (sơ yếu lí/lý lịch, phiều/phiếu khám, đơn xi/đơn xin...).
    /// 5. So khớp ngữ nghĩa tương đương sâu của các giấy tờ tư pháp hộ tịch.
    /// </summary>
    private static bool IsSmartMatch(string headerTitle, string documentItem)
    {
        if (string.IsNullOrWhiteSpace(headerTitle) || string.IsNullOrWhiteSpace(documentItem)) return false;

        static string CleanAndNormalize(string s)
        {
            var t = s.Normalize(NormalizationForm.FormC).ToLowerInvariant();
            t = Regex.Replace(t, @"[\r\n\t]+", " ");
            t = Regex.Replace(t, @"\s+", " ");
            // Chuẩn hóa biến thể chính tả & lỗi gõ văn bản
            t = t.Replace("sơ yếu lí lịch", "sơ yếu lý lịch");
            t = t.Replace("lí lịch", "lý lịch");
            t = t.Replace("phiều khám", "phiếu khám");
            t = t.Replace("đơn xi ", "đơn xin ");
            t = t.Replace("thay đổitên", "thay đổi tên");
            return t.Trim();
        }

        var hRaw = CleanAndNormalize(headerTitle);
        var itRaw = CleanAndNormalize(documentItem);

        if (hRaw == itRaw) return true;

        var h = ExpandAbbreviations(hRaw);
        var it = ExpandAbbreviations(itRaw);

        if (h == it) return true;

        // So khớp không dấu
        var hNoAccent = RemoveAccents(h);
        var itNoAccent = RemoveAccents(it);
        if (hNoAccent == itNoAccent) return true;

        // Bỏ ký tự đặc biệt, chỉ giữ chữ và số
        var hClean = Regex.Replace(h, @"[^\p{L}\p{Nd}\s]", "");
        var itClean = Regex.Replace(it, @"[^\p{L}\p{Nd}\s]", "");
        if (hClean == itClean) return true;

        // 0. Phiếu đề xuất / Tờ đề xuất / Đề xuất
        if ((h.Contains("đề xuất") || hNoAccent.Contains("de xuat")) &&
            (it.Contains("đề xuất") || itNoAccent.Contains("de xuat"))) return true;

        // 1. Visa <-> Thị thực
        if ((h.Contains("visa") || hNoAccent.Contains("visa")) && (it.Contains("thị thực") || itNoAccent.Contains("thi thuc") || it.Contains("visa"))) return true;
        if ((it.Contains("visa") || itNoAccent.Contains("visa")) && (h.Contains("thị thực") || hNoAccent.Contains("thi thuc") || h.Contains("visa"))) return true;

        // 2. CMND / CCCD / Căn cước / Chứng minh nhân dân
        bool hCmnd = h.Contains("chứng minh") || h.Contains("cmnd") || h.Contains("cccd") || h.Contains("căn cước") || hNoAccent.Contains("chung minh");
        bool itCmnd = it.Contains("chứng minh") || it.Contains("cmnd") || it.Contains("cccd") || it.Contains("căn cước") || itNoAccent.Contains("chung minh");
        if (hCmnd && itCmnd) return true;

        // 3. Hộ chiếu / Passport
        if ((h.Contains("hộ chiếu") || h.Contains("passport") || hNoAccent.Contains("ho chieu")) &&
            (it.Contains("hộ chiếu") || it.Contains("passport") || itNoAccent.Contains("ho chieu"))) return true;

        // 4. Bộ công hàm / Bản công hàm / Công hàm
        if ((h.Contains("công hàm") || hNoAccent.Contains("cong ham")) &&
            (it.Contains("công hàm") || itNoAccent.Contains("cong ham"))) return true;

        // 5. Không trở ngại kết hôn
        if ((h.Contains("không") && h.Contains("trở ngại")) && (it.Contains("không") && it.Contains("trở ngại"))) return true;

        // 6. Tuyên thệ (Tờ khai tuyên thệ, Bản khai tuyên thệ, Cam kết tuyên thệ, Bản tuyên thệ sự thật...)
        if ((h.Contains("tuyên thệ") || hNoAccent.Contains("tuyen the")) &&
            (it.Contains("tuyên thệ") || itNoAccent.Contains("tuyen the"))) return true;

        // 7. Đăng ký hộ tịch (Tờ đăng ký hộ tịch, Tờ khai đăng ký hộ tịch...)
        if ((h.Contains("hộ tịch") || hNoAccent.Contains("ho tich")) &&
            (it.Contains("hộ tịch") || itNoAccent.Contains("ho tich")))
        {
            if (h.Contains("thay đổi") || h.Contains("cải chính")) return it.Contains("thay đổi") || it.Contains("cải chính");
            if (it.Contains("thay đổi") || it.Contains("cải chính")) return false;
            return true;
        }

        // 8. Độc thân & Hôn nhân thực tế
        bool hHonNhanThucTe = h.Contains("hôn nhân thực tế") || hNoAccent.Contains("hon nhan thuc te");
        bool itHonNhanThucTe = it.Contains("hôn nhân thực tế") || itNoAccent.Contains("hon nhan thuc te");
        if (hHonNhanThucTe != itHonNhanThucTe) return false;
        if (hHonNhanThucTe && itHonNhanThucTe) return true;

        // 9. Độc thân / Chưa kết hôn (Giấy xác nhận độc thân, Giấy chứng nhận độc thân, Đơn xin xác nhận độc thân...)
        bool hDocThan = h.Contains("độc thân") || h.Contains("chưa kết hôn") || hNoAccent.Contains("doc than") || hNoAccent.Contains("chua ket hon");
        bool itDocThan = it.Contains("độc thân") || it.Contains("chưa kết hôn") || itNoAccent.Contains("doc than") || itNoAccent.Contains("chua ket hon");
        if (hDocThan && itDocThan) return true;

        // 10. Tình trạng hôn nhân
        if ((h.Contains("tình trạng hôn nhân") || hNoAccent.Contains("tinh trang hon nhan")) &&
            (it.Contains("tình trạng hôn nhân") || itNoAccent.Contains("tinh trang hon nhan"))) return true;

        // 11. Đơn xin kết hôn / Tờ khai đăng ký kết hôn / Giấy khai đăng ký kết hôn / Cho phép kết hôn
        bool hKetHon = h.Contains("kết hôn") || h.Contains("hôn thú") || hNoAccent.Contains("ket hon") || hNoAccent.Contains("hon thu");
        bool itKetHon = it.Contains("kết hôn") || it.Contains("hôn thú") || itNoAccent.Contains("ket hon") || itNoAccent.Contains("hon thu");
        if (hKetHon && itKetHon)
        {
            bool hChungNhan = h.Contains("chứng nhận") || h.Contains("xác nhận") || hNoAccent.Contains("chung nhan") || hNoAccent.Contains("xac nhan");
            bool itChungNhan = it.Contains("chứng nhận") || it.Contains("xác nhận") || itNoAccent.Contains("chung nhan") || itNoAccent.Contains("xac nhan");
            bool hChoPhep = h.Contains("cho phép") || h.Contains("được phép") || hNoAccent.Contains("cho phep");
            bool itChoPhep = it.Contains("cho phép") || it.Contains("được phép") || itNoAccent.Contains("cho phep");

            if (hChoPhep || itChoPhep) return hChoPhep && itChoPhep;
            if (hChungNhan && itChungNhan) return true;
            if (!hChungNhan && !itChungNhan) return true;
            if ((h.Contains("tờ khai") || h.Contains("đơn xin") || h.Contains("giấy khai") || h.Contains("tờ báo cáo") || hNoAccent.Contains("to khai") || hNoAccent.Contains("don xin")) &&
                (it.Contains("tờ khai") || it.Contains("đơn xin") || it.Contains("giấy khai") || it.Contains("tờ báo cáo") || itNoAccent.Contains("to khai") || itNoAccent.Contains("don xin")))
            {
                return true;
            }
        }

        // 12. Khai sinh / Khai sanh / Trích bộ lục khai sinh
        bool hKhaiSinh = h.Contains("khai sinh") || h.Contains("khai sanh") || hNoAccent.Contains("khai sinh") || hNoAccent.Contains("khai sanh");
        bool itKhaiSinh = it.Contains("khai sinh") || it.Contains("khai sanh") || itNoAccent.Contains("khai sinh") || itNoAccent.Contains("khai sanh");
        if (hKhaiSinh && itKhaiSinh)
        {
            bool hDanhDu = h.Contains("danh dự") || hNoAccent.Contains("danh du");
            bool itDanhDu = it.Contains("danh dự") || itNoAccent.Contains("danh du");
            if (hDanhDu != itDanhDu) return false;
            return true;
        }

        // 13. Hộ khẩu / Nhân khẩu
        if ((h.Contains("nhân khẩu") || hNoAccent.Contains("nhan khau")) &&
            (it.Contains("nhân khẩu") || itNoAccent.Contains("nhan khau"))) return true;
        if ((h.Contains("hộ khẩu") || hNoAccent.Contains("ho khau")) &&
            (it.Contains("hộ khẩu") || itNoAccent.Contains("ho khau"))) return true;

        // 14. Khám sức khỏe & Chứng chỉ chuyên khoa (Tách biệt hoàn toàn - đây là 2 loại giấy tờ độc lập trong bộ đăng ký kết hôn)
        bool hChuyenKhoa = h.Contains("chuyên khoa") || hNoAccent.Contains("chuyen khoa");
        bool itChuyenKhoa = it.Contains("chuyên khoa") || itNoAccent.Contains("chuyen khoa");
        bool hSucKhoe = h.Contains("sức khỏe") || hNoAccent.Contains("suc khoe");
        bool itSucKhoe = it.Contains("sức khỏe") || itNoAccent.Contains("suc khoe");

        if (itChuyenKhoa) return hChuyenKhoa;
        if (itSucKhoe) return hSucKhoe;

        // 15. Nhập xuất cảnh / Xuất nhập cảnh
        bool hNxc = h.Contains("nhập xuất cảnh") || h.Contains("xuất nhập cảnh") || hNoAccent.Contains("nhap xuat canh") || hNoAccent.Contains("xuat nhap canh");
        bool itNxc = it.Contains("nhập xuất cảnh") || it.Contains("xuất nhập cảnh") || itNoAccent.Contains("nhap xuat canh") || itNoAccent.Contains("xuat nhap canh");
        if (hNxc && itNxc) return true;

        // 16. Ly hôn
        if ((h.Contains("ly hôn") || hNoAccent.Contains("ly hon")) &&
            (it.Contains("ly hôn") || itNoAccent.Contains("ly hon"))) return true;

        // 17. Khai tử / Chứng tử / Báo tử
        bool hTu = h.Contains("khai tử") || h.Contains("chứng tử") || h.Contains("báo tử") || hNoAccent.Contains("khai tu") || hNoAccent.Contains("chung tu") || hNoAccent.Contains("bao tu");
        bool itTu = it.Contains("khai tử") || it.Contains("chứng tử") || it.Contains("báo tử") || itNoAccent.Contains("khai tu") || itNoAccent.Contains("chung tu") || itNoAccent.Contains("bao tu");
        if (hTu && itTu) return true;

        // 18. Ủy quyền
        if ((h.Contains("ủy quyền") || hNoAccent.Contains("uy quyen")) &&
            (it.Contains("ủy quyền") || itNoAccent.Contains("uy quyen"))) return true;

        // 19. Sơ yếu lý lịch / Lý lịch
        if ((h.Contains("lý lịch") || hNoAccent.Contains("ly lich")) &&
            (it.Contains("lý lịch") || itNoAccent.Contains("ly lich"))) return true;

        return false;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Normalize(NormalizationForm.FormC).Trim();
        s = Regex.Replace(s, @"[\r\n\t]+", " ");
        return SpaceRegex.Replace(s, " ").Trim();
    }

    private static string? ExtractCitizenNames(string summary)
    {
        // Try extract "của Bà ... và Ông ..." or "của Ông ... và Bà ..."
        var match = Regex.Match(summary, @"của\s+(?:Bà|Ông)\s+([^.]+?)\s*(?:\.|\(|\n|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private async Task<OneDriveDownloadResult> DownloadOneDriveFileAsync(string oneDriveUrl, CancellationToken ct)
    {
        // If it's a local file path
        if (File.Exists(oneDriveUrl))
        {
            Stp.Infrastructure.Storage.FileStorage.SanitizeExcelPackage(oneDriveUrl);
            var info = new FileInfo(oneDriveUrl);
            return new OneDriveDownloadResult(oneDriveUrl, info.Name, true, null);
        }

        // Fetch landing page
        using var landingResp = await _http.GetAsync(oneDriveUrl, ct).ConfigureAwait(false);
        landingResp.EnsureSuccessStatusCode();

        var html = await landingResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string fileName = "DK KET HON.xlsx";
        bool userCanWrite = true;
        string? etag = null;
        string? downloadUrl = null;

        var personalMatch = Regex.Match(oneDriveUrl, @"personal/([a-f0-9]+)", RegexOptions.IgnoreCase);
        var personalId = personalMatch.Success ? personalMatch.Groups[1].Value : "52960fb7b88aa4ef";

        var wopiMatch = Regex.Match(html, @"var _wopiContextJson\s*=\s*(\{.*?\});", RegexOptions.Singleline);
        if (wopiMatch.Success)
        {
            try
            {
                using var doc = JsonDocument.Parse(wopiMatch.Groups[1].Value);
                var root = doc.RootElement;
                if (root.TryGetProperty("FileName", out var fn)) fileName = fn.GetString() ?? fileName;
                if (root.TryGetProperty("UserCanWrite", out var ucw)) userCanWrite = ucw.GetBoolean();
                if (root.TryGetProperty("ETag", out var et)) etag = et.GetString();

                // LUÔN LUÔN dùng onedrive.live.com trên cùng domain để giữ cookie phiên chia sẻ.
                // TUYỆT ĐỐI không dùng FileGetUrl (my.microsoftpersonalcontent.com) vì domain khác sẽ bị redirect sang trang login.live.com!
                if (root.TryGetProperty("FileId", out var fid) && !string.IsNullOrWhiteSpace(fid.GetString()))
                {
                    downloadUrl = $"https://onedrive.live.com/personal/{personalId}/_layouts/15/download.aspx?UniqueId={fid.GetString()}";
                }
                else if (root.TryGetProperty("DocUniqueId", out var duid) && !string.IsNullOrWhiteSpace(duid.GetString()))
                {
                    downloadUrl = $"https://onedrive.live.com/personal/{personalId}/_layouts/15/download.aspx?UniqueId={duid.GetString()}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không parse được _wopiContextJson từ OneDrive landing page");
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            var uidMatch = Regex.Match(html, @"UniqueId=([a-f0-9\-]+)", RegexOptions.IgnoreCase);
            if (uidMatch.Success)
            {
                downloadUrl = $"https://onedrive.live.com/personal/{personalId}/_layouts/15/download.aspx?UniqueId={uidMatch.Groups[1].Value}";
            }
            else
            {
                downloadUrl = oneDriveUrl.Contains('?') ? $"{oneDriveUrl}&download=1" : $"{oneDriveUrl}?download=1";
            }
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "stp_onedrive");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{fileName}");

        using var fileResp = await _http.GetAsync(downloadUrl, ct).ConfigureAwait(false);
        fileResp.EnsureSuccessStatusCode();

        if (fileResp.Headers.ETag is not null && string.IsNullOrWhiteSpace(etag))
        {
            etag = fileResp.Headers.ETag.Tag;
        }

        await using var fs = File.Create(tempFile);
        await fileResp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        await fs.DisposeAsync().ConfigureAwait(false);

        Stp.Infrastructure.Storage.FileStorage.SanitizeExcelPackage(tempFile);

        return new OneDriveDownloadResult(tempFile, fileName, userCanWrite, etag);
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _oneDriveUrlSemaphores = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, int> _activeJobCounts = new(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeUrlKey(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Path).ToLowerInvariant();
        }
        catch
        {
            return url.Trim().ToLowerInvariant();
        }
    }

    private static void ConfigurePlaywrightEnvironment(ProcessStartInfo psi)
    {
        var existing = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrEmpty(existing) && Directory.Exists(existing))
        {
            psi.EnvironmentVariables["PLAYWRIGHT_BROWSERS_PATH"] = existing;
            return;
        }

        var candidates = new List<string>
        {
            @"C:\ProgramData\ms-playwright",
            @"C:\WINDOWS\system32\config\systemprofile\AppData\Local\ms-playwright"
        };

        try
        {
            const string usersDir = @"C:\Users";
            if (Directory.Exists(usersDir))
            {
                foreach (var userDir in Directory.GetDirectories(usersDir))
                {
                    candidates.Add(Path.Combine(userDir, "AppData", "Local", "ms-playwright"));
                }
            }
        }
        catch
        {
            // Ignore access or IO exceptions when inspecting user directories
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
                {
                    psi.EnvironmentVariables["PLAYWRIGHT_BROWSERS_PATH"] = candidate;
                    break;
                }
            }
            catch
            {
                // Ignore per-directory access errors
            }
        }
    }

    private sealed record OneDriveDownloadResult(string FilePath, string FileName, bool UserCanWrite, string? ETag);

    private sealed class JobState
    {
        public Guid JobId { get; init; }
        public string Status { get; set; } = "Running";
        public string SheetName { get; init; } = string.Empty;
        public int ProcessedRows { get; set; }
        public int TotalRows { get; set; }
        public int NewlyTickedRowsCount { get; set; }
        public int NewlyTickedCellsCount { get; set; }
        public int SkippedRowsCount { get; set; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResultFilePath { get; set; }
        public string? ResultFileName { get; set; }
        public CancellationTokenSource? Cts { get; init; }
        public ConcurrentQueue<AutoTickRowResult> RecentRowResults { get; } = new();
    }

    private static string ResolvePythonExecutable()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(userProfile, @".stp_python\Scripts\python.exe"),
            @"C:\Python314\python.exe",
            @"C:\Python313\python.exe",
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python314\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python313\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python311\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python310\python.exe"),
            @"C:\Program Files\Python314\python.exe",
            @"C:\Program Files\Python313\python.exe",
            @"C:\Program Files\Python312\python.exe",
            @"C:\Program Files\Python311\python.exe",
            @"C:\Program Files\Python310\python.exe"
        };

        try
        {
            const string usersDir = @"C:\Users";
            if (Directory.Exists(usersDir))
            {
                foreach (var u in Directory.GetDirectories(usersDir))
                {
                    candidates.Add(Path.Combine(u, @".stp_python\Scripts\python.exe"));
                    var programs = Path.Combine(u, @"AppData\Local\Programs\Python");
                    if (Directory.Exists(programs))
                    {
                        foreach (var pyDir in Directory.GetDirectories(programs))
                        {
                            candidates.Add(Path.Combine(pyDir, "python.exe"));
                        }
                    }
                    var windowsApps = Path.Combine(u, @"AppData\Local\Microsoft\WindowsApps");
                    if (Directory.Exists(windowsApps))
                    {
                        foreach (var pyDir in Directory.GetDirectories(windowsApps, "PythonSoftwareFoundation.Python*"))
                        {
                            candidates.Add(Path.Combine(pyDir, "python.exe"));
                        }
                    }
                }
            }
        }
        catch { }

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return "python";
    }

    private static string GetCellText(IXLCell cell)
    {
        try
        {
            if (cell.IsEmpty()) return string.Empty;
            if (cell.DataType == XLDataType.DateTime)
            {
                return cell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
            if (cell.DataType == XLDataType.Number)
            {
                var number = cell.GetDouble();
                return Math.Abs(number - Math.Truncate(number)) < 0.0000001
                    ? ((long)number).ToString(CultureInfo.InvariantCulture)
                    : number.ToString(CultureInfo.InvariantCulture);
            }
            var s = cell.GetString();
            return !string.IsNullOrWhiteSpace(s) ? s.Trim() : (cell.Value.ToString()?.Trim() ?? string.Empty);
        }
        catch
        {
            try { return cell.Value.ToString()?.Trim() ?? string.Empty; } catch { return string.Empty; }
        }
    }

    private static int CountMatchingRowsInSheet(IXLWorksheet ws, IReadOnlyList<AutoTickSyncSummaryItem> rows)
    {
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 4;
        if (lastRow < 5) return 0;

        var sheetNumbers = new HashSet<int>();
        var sheetStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var citizenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int r = 5; r <= Math.Min(lastRow, 3000); r++)
        {
            var cellText = GetCellText(ws.Cell(r, 2));
            if (!string.IsNullOrWhiteSpace(cellText))
            {
                sheetStrings.Add(cellText.Trim());
                var m = Regex.Match(cellText, @"^\d+");
                if (m.Success && int.TryParse(m.Value, out int n))
                {
                    sheetNumbers.Add(n);
                }
            }

            var sumText = GetCellText(ws.Cell(r, 4));
            if (!string.IsNullOrWhiteSpace(sumText))
            {
                var names = ExtractCitizenNames(sumText);
                if (!string.IsNullOrWhiteSpace(names))
                {
                    citizenNames.Add(NormalizeText(names));
                }
            }
        }

        int count = 0;
        foreach (var r in rows)
        {
            bool matched = false;
            if (!string.IsNullOrWhiteSpace(r.NumberLabel))
            {
                var label = r.NumberLabel.Trim();
                if (sheetStrings.Contains(label))
                {
                    count++;
                    matched = true;
                }
                else
                {
                    var m = Regex.Match(label, @"^\d+");
                    if (m.Success && int.TryParse(m.Value, out int n) && sheetNumbers.Contains(n))
                    {
                        count++;
                        matched = true;
                    }
                }
            }

            if (!matched && !string.IsNullOrWhiteSpace(r.SummaryText))
            {
                var names = ExtractCitizenNames(r.SummaryText);
                if (!string.IsNullOrWhiteSpace(names) && citizenNames.Contains(NormalizeText(names)))
                {
                    count++;
                    matched = true;
                }
            }

            // Fallback chỉ dành cho hồ sơ không có nhãn số và không có nội dung trích yếu
            if (!matched && string.IsNullOrWhiteSpace(r.NumberLabel) && string.IsNullOrWhiteSpace(r.SummaryText))
            {
                if (r.RowIndex >= 5 && r.RowIndex <= lastRow)
                {
                    count++;
                }
            }
        }

        return count;
    }
}

