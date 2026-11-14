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

    public bool IsOneDriveBusy(string oneDriveUrl)
    {
        if (string.IsNullOrWhiteSpace(oneDriveUrl)) return false;
        var key = NormalizeUrlKey(oneDriveUrl);
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

        try
        {
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

            // 1.2 Tự động kiểm tra độ khớp: nếu targetSheet chưa thấy hoặc có 0 dòng khớp, quét toàn bộ workbook
            if (targetSheet is null || CountMatchingRowsInSheet(targetSheet, request.Rows) == 0)
            {
                IXLWorksheet? bestSheet = null;
                int maxMatches = 0;

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

                if (bestSheet is not null && maxMatches > 0)
                {
                    targetSheet = bestSheet;
                    _logger.LogInformation("Tự động nhận diện sheet tối ưu: '{SheetName}' chứa {Matches}/{Total} hồ sơ cần đồng bộ",
                        bestSheet.Name, maxMatches, request.Rows.Count);
                }
            }

            targetSheet ??= workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals("1997", StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.FirstOrDefault(w => !w.Name.Trim().Equals("file dán", StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.First();

            var sheet = targetSheet;
            var sheetName = targetSheet.Name;
            var headers = ExtractHeaders(sheet);
            int lastColNumber = sheet.LastColumnUsed()?.ColumnNumber() ?? (headers.Count > 0 ? headers.Max(h => h.ColumnIndex) : 95);
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
                FileName = ResolvePythonExecutable(),
                Arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            ConfigurePlaywrightEnvironment(psi);

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

            try
            {
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
            finally
            {
                sem.Release();
            }
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
            var unmatchedItems = items.Where(it => !matchingHeaders.Any(h => IsSmartMatch(h.Title, it))).ToList();

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

            // Nếu có nhiều cột cùng khớp (ví dụ sheet 1997 có 2 cột cùng tên 'tờ khai đăng ký kết hôn' ở Col 18 R và Col 49 AW):
            // Ưu tiên chọn cột đã có dữ liệu tick thực tế trong sheet.
            var best = candidates.OrderByDescending(h => h.ExistingTicksCount).First();
            if (!matched.Contains(best))
            {
                matched.Add(best);
            }
        }
        return matched;
    }

    /// <summary>
    /// So khớp thông minh tên giấy tờ trích yếu với tiêu đề cột hồ sơ theo ngữ cảnh hành chính tư pháp:
    /// 1. Chuẩn hóa Form C, chữ thường, loại bỏ ngắt dòng và dấu cách thừa.
    /// 2. Khử các lỗi chính tả/gõ nhầm phổ biến (sơ yếu lí/lý lịch, phiều/phiếu khám, đơn xi/đơn xin, thay đổitên...).
    /// 3. So khớp các biến thể tương đương chính xác của giấy tờ hộ tịch (Visa/Thị thực, Đơn xin kết hôn/Tờ khai đăng ký kết hôn,
    ///    Không trở ngại kết hôn, Tuyên thệ, Hộ tịch, Độc thân, Khai sinh/Trích lục khai sinh...).
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

        var h = CleanAndNormalize(headerTitle);
        var it = CleanAndNormalize(documentItem);

        if (h == it) return true;

        // Bỏ ký tự đặc biệt, chỉ giữ chữ và số
        var hClean = Regex.Replace(h, @"[^\p{L}\p{Nd}\s]", "");
        var itClean = Regex.Replace(it, @"[^\p{L}\p{Nd}\s]", "");
        if (hClean == itClean) return true;

        // 1. Visa <-> Thị thực
        if ((h == "visa" || h.Contains("visa")) && (it.Contains("thị thực") || it == "visa")) return true;
        if ((it == "visa" || it.Contains("visa")) && (h.Contains("thị thực") || h == "visa")) return true;

        // 2. CMND / CCCD / Căn cước / Chứng minh nhân dân
        if ((h == "cmnd" || h.Contains("chứng minh nhân dân")) && (it == "cmnd" || it.Contains("chứng minh nhân dân") || it == "cccd" || it.Contains("căn cước"))) return true;
        if ((it == "cmnd" || it.Contains("chứng minh nhân dân")) && (h == "cmnd" || h.Contains("chứng minh nhân dân"))) return true;

        // 3. Hộ chiếu / Passport
        if (h.Contains("hộ chiếu") && (it == "hộ chiếu" || it.Contains("hộ chiếu") || it == "passport")) return true;

        // 4. Bộ công hàm / Bản công hàm / Công hàm
        if (h.Contains("công hàm") && it.Contains("công hàm")) return true;

        // 5. Không trở ngại kết hôn
        if ((h.Contains("không") && h.Contains("trở ngại")) && (it.Contains("không") && it.Contains("trở ngại"))) return true;

        // 6. Tuyên thệ (Tờ khai tuyên thệ, Bản khai tuyên thệ, Cam kết tuyên thệ, Bản tuyên thệ sự thật...)
        if (h.Contains("tuyên thệ") && it.Contains("tuyên thệ")) return true;

        // 7. Đăng ký hộ tịch (Tờ đăng ký hộ tịch, Tờ khai đăng ký hộ tịch...)
        if (h.Contains("hộ tịch") && it.Contains("hộ tịch"))
        {
            if (h.Contains("thay đổi") || h.Contains("cải chính")) return it.Contains("thay đổi") || it.Contains("cải chính");
            if (it.Contains("thay đổi") || it.Contains("cải chính")) return false;
            return true;
        }

        // 8. Độc thân & Hôn nhân thực tế
        bool hHonNhanThucTe = h.Contains("hôn nhân thực tế");
        bool itHonNhanThucTe = it.Contains("hôn nhân thực tế");
        if (hHonNhanThucTe != itHonNhanThucTe) return false;
        if (hHonNhanThucTe && itHonNhanThucTe) return true;

        // 9. Độc thân / Chưa kết hôn (Giấy xác nhận độc thân, Giấy chứng nhận độc thân, Đơn xin xác nhận độc thân...)
        if ((h.Contains("độc thân") || h.Contains("chưa kết hôn")) && (it.Contains("độc thân") || it.Contains("chưa kết hôn"))) return true;

        // 10. Tình trạng hôn nhân
        if (h.Contains("tình trạng hôn nhân") && it.Contains("tình trạng hôn nhân")) return true;

        // 11. Đơn xin kết hôn / Tờ khai đăng ký kết hôn / Giấy khai đăng ký kết hôn / Cho phép kết hôn
        bool hKetHon = h.Contains("kết hôn") || h.Contains("hôn thú");
        bool itKetHon = it.Contains("kết hôn") || it.Contains("hôn thú");
        if (hKetHon && itKetHon)
        {
            bool hChungNhan = h.Contains("chứng nhận") || h.Contains("xác nhận");
            bool itChungNhan = it.Contains("chứng nhận") || it.Contains("xác nhận");
            bool hChoPhep = h.Contains("cho phép") || h.Contains("được phép");
            bool itChoPhep = it.Contains("cho phép") || it.Contains("được phép");

            if (hChoPhep || itChoPhep) return hChoPhep && itChoPhep;
            if (hChungNhan && itChungNhan) return true;
            if (!hChungNhan && !itChungNhan) return true;
            if ((h.Contains("tờ khai") || h.Contains("đơn xin") || h.Contains("giấy khai") || h.Contains("tờ báo cáo")) &&
                (it.Contains("tờ khai") || it.Contains("đơn xin") || it.Contains("giấy khai") || it.Contains("tờ báo cáo")))
            {
                return true;
            }
        }

        // 12. Khai sinh / Khai sanh / Trích bộ lục khai sinh
        if ((h.Contains("khai sinh") || h.Contains("khai sanh")) && (it.Contains("khai sinh") || it.Contains("khai sanh")))
        {
            bool hDanhDu = h.Contains("danh dự");
            bool itDanhDu = it.Contains("danh dự");
            if (hDanhDu != itDanhDu) return false;
            return true;
        }

        // 13. Hộ khẩu / Nhân khẩu
        if (h.Contains("nhân khẩu") && it.Contains("nhân khẩu")) return true;
        if (h.Contains("hộ khẩu") && (it.Contains("hộ khẩu") || it.Contains("sổ hộ khẩu"))) return true;

        // 14. Khám sức khỏe / Chứng chỉ chuyên khoa
        if ((h.Contains("sức khỏe") || h.Contains("chuyên khoa")) && (it.Contains("sức khỏe") || it.Contains("chuyên khoa"))) return true;

        // 15. Nhập xuất cảnh / Xuất nhập cảnh
        if ((h.Contains("nhập xuất cảnh") || h.Contains("xuất nhập cảnh")) && (it.Contains("nhập xuất cảnh") || it.Contains("xuất nhập cảnh"))) return true;

        // 16. Ly hôn
        if (h.Contains("ly hôn") && it.Contains("ly hôn")) return true;

        // 17. Khai tử / Chứng tử / Báo tử
        if ((h.Contains("khai tử") || h.Contains("chứng tử") || h.Contains("báo tử")) &&
            (it.Contains("khai tử") || it.Contains("chứng tử") || it.Contains("báo tử"))) return true;

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
        }

        int count = 0;
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(r.NumberLabel))
            {
                var label = r.NumberLabel.Trim();
                if (sheetStrings.Contains(label))
                {
                    count++;
                    continue;
                }
                var m = Regex.Match(label, @"^\d+");
                if (m.Success && int.TryParse(m.Value, out int n) && sheetNumbers.Contains(n))
                {
                    count++;
                    continue;
                }
            }
            if (r.RowIndex >= 5 && r.RowIndex <= lastRow)
            {
                count++;
            }
        }

        return count;
    }
}

