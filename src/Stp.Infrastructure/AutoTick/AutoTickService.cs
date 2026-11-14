using System.Collections.Concurrent;
using System.Diagnostics;
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
            var match = ParenthesesRegex.Match(summary);
            bool hasParens = match.Success;
            List<string> items = new();
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
                items = SplitDocumentItems(match.Groups[1].Value);

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
            int lastColNumber = sheet.LastColumnUsed()?.ColumnNumber() ?? (headers.Count > 0 ? headers.Max(h => h.ColumnIndex) : 95);
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

                var match = ParenthesesRegex.Match(summary);
                if (!match.Success) continue;

                var items = SplitDocumentItems(match.Groups[1].Value);
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

    public async Task<AutoTickSyncSummaryResponse> SyncSummaryRowsToOneDriveAsync(AutoTickSyncSummaryRequest request, CancellationToken ct = default)
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

        try
        {
            // 1. Download & inspect workbook to map headers and row coordinates
            var downloaded = await DownloadOneDriveFileAsync(url, ct).ConfigureAwait(false);
            using var workbook = new XLWorkbook(downloaded.FilePath);
            var sheetName = string.IsNullOrWhiteSpace(request.SheetName) ? "1997" : request.SheetName;
            if (!workbook.Worksheets.Contains(sheetName))
            {
                sheetName = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("1997", StringComparison.OrdinalIgnoreCase))?.Name
                    ?? workbook.Worksheets.First().Name;
            }

            var sheet = workbook.Worksheet(sheetName);
            var headers = ExtractHeaders(sheet);
            int lastColNumber = sheet.LastColumnUsed()?.ColumnNumber() ?? (headers.Count > 0 ? headers.Max(h => h.ColumnIndex) : 95);
            string lastColLetter = sheet.Column(lastColNumber).ColumnLetter();

            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 500;
            var rowNumberMap = new Dictionary<int, int>();
            for (int r = 5; r <= lastRow; r++)
            {
                var numStr = sheet.Cell(r, 2).GetString().Trim();
                var m = Regex.Match(numStr, @"^\d+");
                if (m.Success && int.TryParse(m.Value, out int n))
                {
                    rowNumberMap.TryAdd(n, r);
                }
            }

            var processedRows = new List<object>();
            foreach (var rItem in request.Rows)
            {
                int rowIndex = 0;
                var numMatch = Regex.Match(rItem.NumberLabel ?? "", @"^\d+");
                if (numMatch.Success && int.TryParse(numMatch.Value, out int n) && rowNumberMap.TryGetValue(n, out int mappedRow))
                {
                    rowIndex = mappedRow;
                }
                else if (rItem.RowIndex >= 5 && rItem.RowIndex <= lastRow)
                {
                    rowIndex = rItem.RowIndex;
                }

                if (rowIndex <= 0) continue;

                var targetCells = new List<string>();
                var untickCells = new List<string>();

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
                    // Người dùng yêu cầu: "đánh tick thì nhìn vào chữ mà làm" - trích xuất trực tiếp giấy tờ trong ngoặc đơn của trích yếu chữ
                    if (!string.IsNullOrWhiteSpace(rItem.SummaryText))
                    {
                        var parenMatch = ParenthesesRegex.Match(rItem.SummaryText);
                        if (parenMatch.Success)
                        {
                            desiredDocs.AddRange(SplitDocumentItems(parenMatch.Groups[1].Value));
                        }
                    }

                    if (desiredDocs.Count == 0 && rItem.Items.Count > 0)
                    {
                        desiredDocs.AddRange(rItem.Items);
                    }

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
                        else if (!isDesired && isCurrentlyTicked)
                        {
                            untickCells.Add($"{h.ColumnLetter}{rowIndex}");
                        }
                    }
                }

                string? formulaH = null;
                if (request.EnsureFormulaH)
                {
                    formulaH = $"=\"Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn với người nước ngoài của \"&TEXTBEFORE(TEXTAFTER(D{rowIndex}, \"của \"), \" và\")&\" và  \"&TEXTBEFORE(TEXTAFTER(D{rowIndex}, \"và \"), \".\")&\" (\" & TEXTJOIN(\", \", TRUE, REPT(I$4:{lastColLetter}$4, I{rowIndex}:{lastColLetter}{rowIndex}=TRUE)) & \")\"";
                }
                else if (!string.IsNullOrWhiteSpace(rItem.FormulaH))
                {
                    formulaH = rItem.FormulaH;
                }

                processedRows.Add(new
                {
                    rowIndex,
                    numberLabel = rItem.NumberLabel,
                    summaryText = rItem.SummaryText,
                    targetCells,
                    untickCells,
                    formulaH
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

            // 2. Prepare JSON for Python Playwright worker
            var tempDir = Path.Combine(Path.GetTempPath(), "stp_autotick_sync");
            Directory.CreateDirectory(tempDir);
            var inputPath = Path.Combine(tempDir, $"in_{Guid.NewGuid():N}.json");
            var outputPath = Path.Combine(tempDir, $"out_{Guid.NewGuid():N}.json");

            var payloadObj = new
            {
                oneDriveUrl = url,
                sheetName,
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
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return new AutoTickSyncSummaryResponse
                {
                    Success = false,
                    Message = "Không thể khởi động trình duyệt robot đồng bộ."
                };
            }

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            if (File.Exists(outputPath))
            {
                var json = await File.ReadAllTextAsync(outputPath, ct).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<AutoTickSyncSummaryResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                try { File.Delete(inputPath); File.Delete(outputPath); } catch { }

                return result ?? new AutoTickSyncSummaryResponse
                {
                    Success = false,
                    Message = "Không đọc được kết quả đồng bộ từ robot."
                };
            }

            var errText = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
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
        bool isOnlineOneDrive = request.OneDriveUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);

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

            var match = ParenthesesRegex.Match(summary);
            if (!match.Success)
            {
                state.ProcessedRows++;
                state.SkippedRowsCount++;
                continue;
            }

            var items = SplitDocumentItems(match.Groups[1].Value);
            var matchingHeaders = FindMatchingHeaders(items, targetHeaders);

            var newlyTickedCols = new List<string>();
            var alreadyTickedCols = new List<string>();
            var unmatchedItems = items.Where(it => !matchingHeaders.Any(h => IsStrictMatch(h.Title, it))).ToList();

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

            string rowStatus;
            if (newlyTickedCols.Count > 0)
            {
                rowStatus = "Ticked";
                state.NewlyTickedRowsCount++;

                var targetCells = matchingHeaders
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

        // Nếu là link OneDrive Online và có các ô cần tick: Chạy Robot Playwright thực tế trên cloud
        if (isOnlineOneDrive && cloudRows.Count > 0 && !ct.IsCancellationRequested)
        {
            await RunCloudPlaywrightJobAsync(jobId, request.OneDriveUrl, sheetName, cloudRows, state, ct).ConfigureAwait(false);
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
            FileName = "python",
            Arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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

    private static List<AutoTickSheetHeader> ExtractHeaders(IXLWorksheet sheet)
    {
        var list = new List<AutoTickSheetHeader>();
        int lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 9;
        int lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 50, 500);

        for (int c = 9; c <= lastCol; c++) // Start at Col I
        {
            var title = sheet.Cell(4, c).GetString().Trim();
            if (string.IsNullOrWhiteSpace(title)) continue;

            title = NormalizeText(title);

            // Count existing ticks
            int tickCount = 0;
            for (int r = 5; r <= lastRow; r++)
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

    private static List<string> SplitDocumentItems(string insideParens)
    {
        return insideParens
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
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
            foreach (var h in headers)
            {
                if (IsStrictMatch(h.Title, item))
                {
                    if (!matched.Contains(h)) matched.Add(h);
                }
            }
        }
        return matched;
    }

    /// <summary>
    /// Strict literal match according to user requirements:
    /// Does NOT loosely merge phrases if text differs.
    /// Matches exact normalized text and unambiguous administrative aliases.
    /// </summary>
    private static bool IsStrictMatch(string headerTitle, string documentItem)
    {
        var h = NormalizeText(headerTitle);
        var it = NormalizeText(documentItem);

        if (h.Equals(it, StringComparison.OrdinalIgnoreCase)) return true;

        // Strip non-letter formatting
        var hClean = Regex.Replace(h, @"[^\p{L}\p{Nd}\s]", "");
        var itClean = Regex.Replace(it, @"[^\p{L}\p{Nd}\s]", "");
        if (hClean.Equals(itClean, StringComparison.OrdinalIgnoreCase)) return true;

        // Unambiguous standard administrative aliases
        if (h.Equals("cmnd", StringComparison.OrdinalIgnoreCase) && it.Equals("chứng minh nhân dân", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("chứng minh nhân dân", StringComparison.OrdinalIgnoreCase) && it.Equals("cmnd", StringComparison.OrdinalIgnoreCase)) return true;

        if (h.Equals("hộ khẩu", StringComparison.OrdinalIgnoreCase) && it.Equals("sổ hộ khẩu", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("hộ khẩu", StringComparison.OrdinalIgnoreCase) && it.Equals("sổ hộ khẩu gia đình", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("sổ hộ khẩu", StringComparison.OrdinalIgnoreCase) && it.Equals("hộ khẩu", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("hộ khẩu", StringComparison.OrdinalIgnoreCase) && it.Equals("bản sao hộ khẩu", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("bản sao hộ khẩu", StringComparison.OrdinalIgnoreCase) && it.Equals("hộ khẩu", StringComparison.OrdinalIgnoreCase)) return true;

        if (h.Equals("giấy khai sinh", StringComparison.OrdinalIgnoreCase) && it.Equals("khai sinh", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("khai sinh", StringComparison.OrdinalIgnoreCase) && it.Equals("giấy khai sinh", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("giấy khai sinh", StringComparison.OrdinalIgnoreCase) && it.Equals("bản sao giấy khai sinh", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("bản sao giấy khai sinh", StringComparison.OrdinalIgnoreCase) && it.Equals("giấy khai sinh", StringComparison.OrdinalIgnoreCase)) return true;

        // Kết hôn: "giấy chứng nhận kết hôn" <-> "chứng nhận kết hôn"
        if (h.Equals("giấy chứng nhận kết hôn", StringComparison.OrdinalIgnoreCase) && it.Equals("chứng nhận kết hôn", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("chứng nhận kết hôn", StringComparison.OrdinalIgnoreCase) && it.Equals("giấy chứng nhận kết hôn", StringComparison.OrdinalIgnoreCase)) return true;

        // Hôn nhân thực tế: "đơn xin xác nhận độc thân và xác nhận hôn nhân thực tế" <-> "đơn xin xác nhận độc thân và hôn nhân thực tế"
        if (h.Equals("đơn xin xác nhận độc thân và xác nhận hôn nhân thực tế", StringComparison.OrdinalIgnoreCase) && it.Equals("đơn xin xác nhận độc thân và hôn nhân thực tế", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("đơn xin xác nhận độc thân và hôn nhân thực tế", StringComparison.OrdinalIgnoreCase) && it.Equals("đơn xin xác nhận độc thân và xác nhận hôn nhân thực tế", StringComparison.OrdinalIgnoreCase)) return true;

        // Độc thân
        if (h.Equals("giấy chứng nhận độc thân", StringComparison.OrdinalIgnoreCase) && it.Equals("chứng nhận độc thân", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("chứng nhận độc thân", StringComparison.OrdinalIgnoreCase) && it.Equals("giấy chứng nhận độc thân", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("giấy xác nhận độc thân", StringComparison.OrdinalIgnoreCase) && it.Equals("xác nhận độc thân", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("xác nhận độc thân", StringComparison.OrdinalIgnoreCase) && it.Equals("giấy xác nhận độc thân", StringComparison.OrdinalIgnoreCase)) return true;

        // Nhập xuất cảnh: "phiếu nhập xuất cảnh" <-> "phiếu xuất nhập cảnh"
        if (h.Equals("phiếu nhập xuất cảnh", StringComparison.OrdinalIgnoreCase) && it.Equals("phiếu xuất nhập cảnh", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("phiếu xuất nhập cảnh", StringComparison.OrdinalIgnoreCase) && it.Equals("phiếu nhập xuất cảnh", StringComparison.OrdinalIgnoreCase)) return true;

        // Tuyên thệ: "tờ khai tuyên thệ" <-> "bản khai tuyên thệ"
        if (h.Equals("tờ khai tuyên thệ", StringComparison.OrdinalIgnoreCase) && it.Equals("bản khai tuyên thệ", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("bản khai tuyên thệ", StringComparison.OrdinalIgnoreCase) && it.Equals("tờ khai tuyên thệ", StringComparison.OrdinalIgnoreCase)) return true;

        // Đăng ký khai sinh
        if (h.Equals("giấy chứng nhận đăng ký khai sinh", StringComparison.OrdinalIgnoreCase) && it.Equals("chứng nhận đăng ký khai sinh", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.Equals("chứng nhận đăng ký khai sinh", StringComparison.OrdinalIgnoreCase) && it.Equals("giấy chứng nhận đăng ký khai sinh", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Normalize(NormalizationForm.FormC).Trim();
        return SpaceRegex.Replace(s, " ");
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
}

