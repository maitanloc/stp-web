#pragma warning disable CA1416 // COM/Registry chỉ chạy trên Windows

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using ClosedXML.Excel;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

public enum ExcelHostKind
{
    Wps,
    Microsoft
}

public sealed class ExcelComOptions
{
    /// <summary>Đường dẫn EXCEL.EXE/et.exe; để trống thì tự dò.</summary>
    public string? ExcelPath { get; set; }

    /// <summary>Auto (ưu tiên WPS), Wps hoặc Microsoft.</summary>
    public string PreferredHost { get; set; } = "Auto";

    public int StartupTimeoutSeconds { get; set; } = 120;

    /// <summary>Giữ một phiên Excel dùng lại cho nhiều file trong cùng job.</summary>
    public bool ReuseSession { get; set; } = true;

    /// <summary>Tự bật AccessVBOM cho phiên bản Office đang dùng.</summary>
    public bool EnableAccessVbom { get; set; } = true;
}

/// <summary>
/// Chạy Microsoft Excel qua COM. Không dùng ProgID <c>Excel.Application</c>
/// vì WPS có thể chiếm CLSID; mở EXCEL.EXE /automation với marker workbook
/// rồi tìm đúng instance qua Running Object Table (ROT).
/// </summary>
public sealed class ComExcelEngine : IExcelConversionService, IAsyncDisposable
{
    private const int XlPdf = 0;
    private const int XlCalculationManual = -4135;
    private const int MsoAutomationSecurityLow = 1;
    private const int XlTypeXlsm = 52;

    private readonly ILogger<ComExcelEngine> _logger;
    private readonly ExcelComOptions _options;
    private readonly VbaProjectInjector _vbaProjectInjector;
    private readonly IPrintSheetGenerator? _printSheetGenerator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ExcelSession? _session;
    private bool _disposed;

    public ComExcelEngine(
        IOptions<ExcelComOptions> options,
        VbaProjectInjector vbaProjectInjector,
        ILogger<ComExcelEngine> logger)
        : this(options, vbaProjectInjector, printSheetGenerator: null, logger)
    {
    }

    public ComExcelEngine(
        IOptions<ExcelComOptions> options,
        VbaProjectInjector vbaProjectInjector,
        IPrintSheetGenerator? printSheetGenerator,
        ILogger<ComExcelEngine> logger)
    {
        _options = options.Value;
        _vbaProjectInjector = vbaProjectInjector;
        _printSheetGenerator = printSheetGenerator;
        _logger = logger;

        var (path, kind) = ResolveHost(_options);
        ExcelPath = path;
        HostKind = kind;
    }

    public bool IsAvailable => ExcelPath is not null;

    public string? ExcelPath { get; }

    public ExcelHostKind HostKind { get; }

    private static (string? Path, ExcelHostKind Kind) ResolveHost(ExcelComOptions options)
    {
        if (string.Equals(options.ExcelPath?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            return (null, ExcelHostKind.Microsoft);
        }

        if (options.ExcelPath is { Length: > 0 } configured && File.Exists(configured))
        {
            var kind = IsWpsPath(configured) ? ExcelHostKind.Wps : ExcelHostKind.Microsoft;
            return (Path.GetFullPath(configured), kind);
        }

        var wps = FindWpsOffice();
        var microsoft = FindMicrosoftExcel();
        var preference = options.PreferredHost?.Trim() ?? "Auto";

        if (preference.Equals("Wps", StringComparison.OrdinalIgnoreCase))
        {
            return (wps ?? microsoft, wps is not null ? ExcelHostKind.Wps : ExcelHostKind.Microsoft);
        }

        if (preference.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return (microsoft ?? wps, microsoft is not null ? ExcelHostKind.Microsoft : ExcelHostKind.Wps);
        }

        // Auto: ưu tiên WPS vì môi trường hiện tại không có license Microsoft Office.
        return (wps ?? microsoft, wps is not null ? ExcelHostKind.Wps : ExcelHostKind.Microsoft);
    }

    private static string? FindWpsOffice()
    {
        var candidates = new List<string>();

        foreach (var root in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Kingsoft", "WPS Office"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Kingsoft", "WPS Office"),
                     @"C:\Program Files\Kingsoft\WPS Office",
                     @"C:\Program Files (x86)\Kingsoft\WPS Office"
                 })
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                candidates.AddRange(Directory
                    .GetDirectories(root)
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d => Path.Combine(d, "office6", "et.exe")));
                candidates.Add(Path.Combine(root, "office6", "et.exe"));
            }
            catch
            {
                // Bỏ qua thư mục không đọc được.
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var info = FileVersionInfo.GetVersionInfo(candidate);
                if ((info.FileDescription?.Contains("WPS", StringComparison.OrdinalIgnoreCase) ?? false)
                    || (info.CompanyName?.Contains("WPS", StringComparison.OrdinalIgnoreCase) ?? false)
                    || IsWpsPath(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Bỏ qua file lỗi.
            }
        }

        return null;
    }

    private static bool IsWpsPath(string path)
    {
        return path.Contains("Kingsoft", StringComparison.OrdinalIgnoreCase)
               || path.Contains("WPS Office", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ConversionOutcome> ConvertAsync(
        ConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsAvailable)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ConvertWithoutExcelAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            string? stagedInput = null;
            PrintGenerationOutcome? printOutcome = null;

            try
            {
                var effectiveRequest = request;

                if (request.GeneratePrintSheets && _printSheetGenerator is not null)
                {
                    printOutcome = await PreparePrintSheetsAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                    if (printOutcome.Generated
                        && !string.Equals(
                            printOutcome.OutputPath,
                            request.InputPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        stagedInput = printOutcome.OutputPath;
                        effectiveRequest = request.WithInputPath(stagedInput);
                        _logger.LogInformation(
                            "Đã sinh {Count} sheet in cho {Input}; chuyển tiếp file tạm {Staged}",
                            printOutcome.GeneratedSheetCount,
                            request.InputPath,
                            stagedInput);
                    }
                }

                var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);

                var outcome = await Task.Run(
                    () => session.Convert(effectiveRequest),
                    cancellationToken).ConfigureAwait(false);

                var macroLines = outcome.MacroLines;
                var macroVerified = outcome.MacroVerified;
                var warning = CombineWarnings(
                    outcome.Warning,
                    BuildPrintGenerationWarning(printOutcome));

                if (outcome.Success && effectiveRequest.InjectMacro && HostKind == ExcelHostKind.Wps)
                {
                    try
                    {
                        var injection = await Task.Run(
                            () => _vbaProjectInjector.Inject(outcome.OutputPath, cancellationToken),
                            cancellationToken).ConfigureAwait(false);

                        macroLines = injection.MacroLines;
                        macroVerified = false;
                        warning = CombineWarnings(
                            BuildWpsInjectionWarning(injection),
                            BuildPrintGenerationWarning(printOutcome));
                        _logger.LogInformation(
                            "Đã chèn VBA vào {Output}: donor {DonorSheets} module, gắn {Mapped}, " +
                            "{Without} sheet không module",
                            outcome.OutputPath,
                            injection.DonorSheetModules,
                            injection.MappedSheetModules,
                            injection.SheetsWithoutModules);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Không chèn được VBA vào file WPS {Output}",
                            outcome.OutputPath);

                        return new ConversionOutcome
                        {
                            Success = false,
                            InputPath = request.InputPath,
                            OutputPath = outcome.OutputPath,
                            MacroLines = outcome.MacroLines,
                            MacroVerified = false,
                            Duration = stopwatch.Elapsed,
                            ErrorMessage = "Không chèn được macro VBA: " + ex.Message,
                            Warning = "File đã được tạo nhưng chưa có macro chuẩn.",
                            GeneratedPrintYears = printOutcome?.GeneratedYears ?? Array.Empty<int>()
                        };
                    }
                }
                else if (!effectiveRequest.InjectMacro)
                {
                    warning = CombineWarnings(
                        warning,
                        "Không chèn macro VBA theo yêu cầu.");
                }

                stopwatch.Stop();
                return new ConversionOutcome
                {
                    Success = outcome.Success,
                    InputPath = request.InputPath,
                    OutputPath = outcome.OutputPath,
                    MacroLines = macroLines,
                    MacroVerified = macroVerified,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = outcome.ErrorMessage,
                    Warning = warning,
                    GeneratedPrintYears = printOutcome?.GeneratedYears ?? Array.Empty<int>()
                };
            }
            catch
            {
                // COM có thể đã hỏng; bỏ phiên hiện tại để lần sau khởi động lại.
                await ResetSessionAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                TryDelete(stagedInput);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Chế độ template: chuyển .xlsx thành .xlsm mà không cần WPS/Microsoft Excel.
    /// Cách làm: sinh sheet in bằng ClosedXML (nếu cần), chép nguyên byte sang .xlsm
    /// rồi chèn VBA donor bằng OpenXML (đã đổi content type workbook sang macroEnabled).
    /// </summary>
    private async Task<ConversionOutcome> ConvertWithoutExcelAsync(
        ConversionRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? stagedInput = null;
        PrintGenerationOutcome? printOutcome = null;

        try
        {
            var effectiveRequest = request;

            if (request.GeneratePrintSheets && _printSheetGenerator is not null)
            {
                printOutcome = await PreparePrintSheetsAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                if (printOutcome.Generated
                    && !string.Equals(printOutcome.OutputPath, request.InputPath, StringComparison.OrdinalIgnoreCase))
                {
                    stagedInput = printOutcome.OutputPath;
                    effectiveRequest = request.WithInputPath(stagedInput);
                    _logger.LogInformation(
                        "Template: đã sinh {Count} sheet in cho {Input}",
                        printOutcome.GeneratedSheetCount,
                        request.InputPath);
                }
            }

            if (!File.Exists(effectiveRequest.InputPath))
            {
                return new ConversionOutcome
                {
                    Success = false,
                    InputPath = request.InputPath,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = "Không tìm thấy file đầu vào để chuyển bằng template."
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(effectiveRequest.OutputPath)) ?? Path.GetTempPath());

            var tempXlsm = effectiveRequest.OutputPath + ".tmpl-" + Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(effectiveRequest.InputPath, tempXlsm, overwrite: true);

            try
            {
                var injection = await Task.Run(
                    () => _vbaProjectInjector.Inject(tempXlsm, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                File.Move(tempXlsm, effectiveRequest.OutputPath, overwrite: true);

                _logger.LogInformation(
                    "Template: đã tạo .xlsm không cần Excel {Output}: donor {Donor} module, gắn {Mapped}",
                    effectiveRequest.OutputPath,
                    injection.DonorSheetModules,
                    injection.MappedSheetModules);

                stopwatch.Stop();
                var warning = CombineWarnings(
                    "Chạy không cần Excel (template OpenXML + VBA donor). " +
                    "Mở file trên máy có Microsoft Excel và bấm 'Enable Content' để dùng macro.",
                    BuildPrintGenerationWarning(printOutcome));

                return new ConversionOutcome
                {
                    Success = true,
                    InputPath = request.InputPath,
                    OutputPath = effectiveRequest.OutputPath,
                    MacroLines = injection.MacroLines,
                    MacroVerified = false,
                    Duration = stopwatch.Elapsed,
                    Warning = warning,
                    GeneratedPrintYears = printOutcome?.GeneratedYears ?? Array.Empty<int>()
                };
            }
            catch (Exception ex)
            {
                TryDelete(tempXlsm);
                _logger.LogError(ex, "Template: không chèn được VBA donor {Output}", effectiveRequest.OutputPath);
                stopwatch.Stop();
                return new ConversionOutcome
                {
                    Success = false,
                    InputPath = request.InputPath,
                    OutputPath = effectiveRequest.OutputPath,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = "Không chèn được macro VBA bằng template: " + ex.Message,
                    Warning = "Không tạo được file .xlsm; hãy cài Microsoft Excel hoặc WPS.",
                    GeneratedPrintYears = printOutcome?.GeneratedYears ?? Array.Empty<int>()
                };
            }
        }
        finally
        {
            TryDelete(stagedInput);
        }
    }

    private async Task<PrintGenerationOutcome> PreparePrintSheetsAsync(
        ConversionRequest request,
        CancellationToken cancellationToken)
    {
        var workingDirectory = request.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetTempPath();
        }

        Directory.CreateDirectory(workingDirectory);

        var extension = Path.GetExtension(request.InputPath);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".xlsx";
        }

        var stagedPath = Path.Combine(
            workingDirectory,
            $"~stp-print-{Guid.NewGuid():N}{extension}");

        var generationRequest = new PrintGenerationRequest
        {
            InputPath = request.InputPath,
            OutputPath = stagedPath,
            Overwrite = true
        };

        var outcome = await Task.Run(
            () => _printSheetGenerator!.Generate(generationRequest, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Generated)
        {
            // Generator trả lại chính file nguồn khi không cần sinh; chỉ xoá
            // file tạm mà mình đã đặt tên, tuyệt đối không xoá file upload gốc.
            TryDelete(stagedPath);
        }

        return outcome;
    }

    private static string? BuildPrintGenerationWarning(PrintGenerationOutcome? outcome)
    {
        if (outcome is null || !outcome.Generated)
        {
            return null;
        }

        var years = string.Join(", ", outcome.GeneratedYears.OrderBy(y => y));
        var text = $"Đã sinh {outcome.GeneratedSheetCount} sheet in từ ML cho các năm: {years}.";
        if (outcome.Warnings.Count > 0)
        {
            text += " " + string.Join(" ", outcome.Warnings);
        }

        return text;
    }

    private static string? CombineWarnings(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return string.IsNullOrWhiteSpace(second) ? null : second;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return first + " " + second;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Bỏ qua file tạm không xóa được.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ResetSessionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<ExcelSession> GetSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return _session;
        }

        var excelPath = ExcelPath
                        ?? throw new InvalidOperationException("Không tìm thấy WPS Spreadsheets hoặc Microsoft Excel.");
        if (_options.EnableAccessVbom && HostKind == ExcelHostKind.Microsoft)
        {
            TryEnableAccessVbom(excelPath, _logger);
        }

        var session = new ExcelSession(excelPath, HostKind, _options.StartupTimeoutSeconds, _logger);
        await session.StartAsync(cancellationToken).ConfigureAwait(false);
        _session = session;
        return session;
    }

    private Task ResetSessionAsync()
    {
        var session = _session;
        _session = null;
        if (session is null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không đóng được phiên Excel COM");
            }
        });
    }

    private static string BuildWpsInjectionWarning(VbaInjectionResult injection)
    {
        var text = $"Đã chèn macro VBA ({injection.MacroLines} dòng) vào file .xlsm. " +
                   "Mở bằng Microsoft Excel và bấm 'Enable Content' để dùng macro. " +
                   "WPS không có VBA runtime nên không chạy thử được macro trên máy này.";
        if (injection.Warnings.Count > 0)
        {
            text += " Lưu ý: " + string.Join(" ", injection.Warnings);
        }

        return text;
    }

    private static void TryEnableAccessVbom(string excelPath, ILogger logger)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(excelPath).FileMajorPart;
            if (version <= 0)
            {
                version = 16;
            }

            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Microsoft\Office\{version}.0\Excel\Security");
            key?.SetValue("AccessVBOM", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không bật được AccessVBOM; chèn macro có thể thất bại");
        }
    }

    private static string? FindMicrosoftExcel()
    {
        var candidates = new List<string>();

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var appPath = hklm.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\EXCEL.EXE");
                if (appPath?.GetValue(null) is string path && path.Length > 0)
                {
                    candidates.Add(path.Trim('"'));
                }
            }
            catch
            {
                // Bỏ qua registry view không đọc được.
            }
        }

        candidates.AddRange(new[]
        {
            @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE",
            @"C:\Program Files (x86)\Microsoft Office\root\Office16\EXCEL.EXE",
            @"C:\Program Files\Microsoft Office\Office16\EXCEL.EXE",
            @"C:\Program Files (x86)\Microsoft Office\Office16\EXCEL.EXE"
        });

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var info = FileVersionInfo.GetVersionInfo(candidate);
                if (string.Equals(info.CompanyName, "Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
                    || (info.FileDescription?.Contains("Microsoft Excel", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Bỏ qua file lỗi.
            }
        }

        return null;
    }

    private sealed class ExcelSession : IAsyncDisposable
    {
        private readonly string _excelPath;
        private readonly ExcelHostKind _hostKind;
        private readonly int _startupTimeoutSeconds;
        private readonly ILogger _logger;
        private readonly string _tempDirectory;
        private readonly string _markerPath;
        private Process? _process;
        private dynamic? _application;
        private dynamic? _markerWorkbook;

        public ExcelSession(
            string excelPath,
            ExcelHostKind hostKind,
            int startupTimeoutSeconds,
            ILogger logger)
        {
            _excelPath = excelPath;
            _hostKind = hostKind;
            _startupTimeoutSeconds = startupTimeoutSeconds;
            _logger = logger;
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "stp-excel",
                Guid.NewGuid().ToString("N"));
            _markerPath = Path.Combine(_tempDirectory, "stp-marker.xlsx");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_tempDirectory);
            CreateMarkerWorkbook(_markerPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _excelPath,
                Arguments = $"/Automation \"{_markerPath}\"",
                UseShellExecute = false
            };

            _process = Process.Start(startInfo)
                       ?? throw new InvalidOperationException("Không khởi động được ứng dụng bảng tính.");

            var hostName = _hostKind == ExcelHostKind.Wps ? "WPS Spreadsheets" : "Microsoft Excel";
            _logger.LogInformation("Đang chờ {Host} khởi động (PID {Pid})", hostName, _process.Id);

            var deadline = DateTime.UtcNow.AddSeconds(_startupTimeoutSeconds);
            var processExited = false;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!processExited && _process.HasExited)
                {
                    // WPS có launcher riêng: tiến trình khởi chạy có thể thoát ngay
                    // trong khi cửa sổ WPS thật vẫn mở file. Tiếp tục chờ ROT.
                    processExited = true;
                    _logger.LogDebug(
                        "{Host} launcher đã thoát (mã {Code}); tiếp tục chờ cửa sổ chính",
                        hostName,
                        _process.ExitCode);
                }

                dynamic? application = TryAttachToMarker(_markerPath);
                if (application is not null)
                {
                    _application = application;
                    ConfigureApplication(application);
                    _markerWorkbook = FindMarkerWorkbook(application, _markerPath);
                    _logger.LogInformation(
                        "Đã gắn {Host} {Version} tại {Path}",
                        hostName,
                        SafeGet(() => (string)application.Version),
                        SafeGet(() => (string)application.Path));
                    return;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"{hostName} không sẵn sàng sau {_startupTimeoutSeconds} giây " +
                $"({(processExited ? "launcher đã thoát" : "process còn chạy")}). " +
                "Kiểm tra ứng dụng đã kích hoạt và không có hộp thoại chặn.");
        }

        public SessionConversionResult Convert(ConversionRequest request)
        {
            if (_application is null)
            {
                throw new InvalidOperationException("Phiên Excel chưa sẵn sàng.");
            }

            dynamic? workbook = null;
            var macroVerified = false;
            try
            {
                var inputPath = Path.GetFullPath(request.InputPath);
                var outputPath = Path.GetFullPath(request.OutputPath);
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException("Không tìm thấy file nguồn.", inputPath);
                }

                var outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                if (File.Exists(outputPath)
                    && !string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(outputPath);
                }

                workbook = _application.Workbooks.Open(inputPath);
                if ((bool)workbook.ReadOnly)
                {
                    throw new IOException(
                        $"File đang được mở ở chế độ chỉ đọc: {inputPath}. Hãy đóng file trong Excel rồi thử lại.");
                }

                var inputIsMacroEnabled = string.Equals(
                    Path.GetExtension(inputPath), ".xlsm", StringComparison.OrdinalIgnoreCase);
                var samePath = string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase);

                if (!samePath || !inputIsMacroEnabled)
                {
                    workbook.SaveAs(outputPath, XlTypeXlsm);
                }

                var macroLines = 0;
                string? warning = null;

                if (_hostKind == ExcelHostKind.Wps)
                {
                    // WPS không có VBA runtime nên không thể đọc VBProject hay chạy macro.
                    // File được lưu trước, sau đó ComExcelEngine chèn vbaProject.bin bằng OpenXML.
                    warning = null;
                }
                else if (request.InjectMacro)
                {
                    macroLines = InjectMacro(workbook, request.MacroCode, request.ReplaceThisWorkbookCode);

                    if (request.VerifyMacro)
                    {
                        try
                        {
                            _application.Run("ThisWorkbook.StpSelfTest");
                            macroVerified = true;
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                "Macro đã chèn nhưng không chạy được. Kiểm tra lỗi biên dịch VBA. " +
                                "Chi tiết: " + ex.Message,
                                ex);
                        }
                    }
                }
                else
                {
                    warning = "Không chèn macro VBA theo yêu cầu.";
                }

                workbook.Save();
                var savedPath = (string)workbook.FullName;
                workbook.Close(true);
                workbook = null;

                return new SessionConversionResult
                {
                    Success = true,
                    OutputPath = savedPath,
                    MacroLines = macroLines,
                    MacroVerified = macroVerified,
                    Warning = warning
                };
            }
            catch (Exception ex)
            {
                try
                {
                    workbook?.Close(false);
                }
                catch
                {
                    // Không che lỗi gốc.
                }

                _logger.LogError(ex, "Lỗi chuyển workbook {Input}", request.InputPath);
                return new SessionConversionResult
                {
                    Success = false,
                    OutputPath = request.OutputPath,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_markerWorkbook is not null)
                {
                    try { _markerWorkbook.Close(false); } catch { }
                    ReleaseCom(_markerWorkbook);
                    _markerWorkbook = null;
                }

                if (_application is not null)
                {
                    try { _application.DisplayAlerts = false; } catch { }
                    try { _application.Quit(); } catch { }
                    ReleaseCom(_application);
                    _application = null;
                }
            }
            finally
            {
                await WaitForExitAsync().ConfigureAwait(false);

                try
                {
                    if (Directory.Exists(_tempDirectory))
                    {
                        Directory.Delete(_tempDirectory, recursive: true);
                    }
                }
                catch
                {
                    // Không quan trọng.
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private async Task WaitForExitAsync()
        {
            var process = _process;
            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Excel không thoát sau 10 giây, sẽ kill process {Pid}", process.Id);
                    }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // Process có thể đã thoát.
            }
            finally
            {
                process.Dispose();
                _process = null;
            }
        }

        private static void CreateMarkerWorkbook(string path)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("STP_MARKER");
            sheet.Cell("A1").Value = "STP Excel COM marker";
            workbook.SaveAs(path);
        }

        private void ConfigureApplication(dynamic application)
        {
            TrySet(() => application.Visible = false, nameof(application.Visible));
            TrySet(() => application.DisplayAlerts = false, nameof(application.DisplayAlerts));
            TrySet(() => application.AutomationSecurity = MsoAutomationSecurityLow, "AutomationSecurity");
            TrySet(() => application.AskToUpdateLinks = false, "AskToUpdateLinks");
            TrySet(() => application.ScreenUpdating = false, "ScreenUpdating");
            TrySet(() => application.EnableEvents = false, "EnableEvents");
            TrySet(() => application.Calculation = XlCalculationManual, "Calculation");
            TrySet(() => application.EnableCancelKey = 0, "EnableCancelKey");
        }

        private void TrySet(Action action, string optionName)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ứng dụng bảng tính không hỗ trợ tùy chọn {Option}", optionName);
            }
        }

        private static object? FindMarkerWorkbook(dynamic application, string markerPath)
        {
            try
            {
                dynamic workbooks = application.Workbooks;
                var count = (int)workbooks.Count;
                for (var i = 1; i <= count; i++)
                {
                    dynamic workbook = workbooks.Item(i);
                    try
                    {
                        var fullName = (string)workbook.FullName;
                        if (string.Equals(
                                Path.GetFullPath(fullName),
                                Path.GetFullPath(markerPath),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return workbook;
                        }
                    }
                    finally
                    {
                        ReleaseCom(workbook);
                    }
                }
            }
            catch
            {
                // Workbook chưa mở xong.
            }

            return null;
        }

        private static int InjectMacro(dynamic workbook, string macroCode, bool replaceExisting)
        {
            if (string.IsNullOrWhiteSpace(macroCode))
            {
                throw new ArgumentException("Mã macro rỗng.", nameof(macroCode));
            }

            dynamic project;
            try
            {
                project = workbook.VBProject;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không truy cập được VBA project. Hãy bật Trust Center > Macro Settings > " +
                    "'Trust access to the VBA project object model' (AccessVBOM = 1).",
                    ex);
            }

            dynamic thisWorkbook;
            try
            {
                thisWorkbook = project.VBComponents.Item("ThisWorkbook");
            }
            catch
            {
                thisWorkbook = project.VBComponents.Add(100);
                thisWorkbook.Name = "ThisWorkbook";
            }

            dynamic codeModule = thisWorkbook.CodeModule;
            var existingLines = (int)codeModule.CountOfLines;
            if (replaceExisting && existingLines > 0)
            {
                codeModule.DeleteLines(1, existingLines);
            }

            var code = StripAttributeLines(macroCode).Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            codeModule.AddFromString(code);
            return (int)codeModule.CountOfLines;
        }

        private static string StripAttributeLines(string code)
        {
            return string.Join(
                "\r\n",
                code.Replace("\r\n", "\n")
                    .Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("Attribute VB_", StringComparison.Ordinal)));
        }

        private object? TryAttachToMarker(string markerPath)
        {
            IRunningObjectTable? rot = null;
            IEnumMoniker? enumMoniker = null;
            try
            {
                if (GetRunningObjectTable(0, out rot) != 0)
                {
                    return null;
                }

                rot.EnumRunning(out enumMoniker);
                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    if (moniker is null)
                    {
                        break;
                    }

                    object? candidate = null;
                    object? matchedApplication = null;
                    try
                    {
                        rot.GetObject(moniker, out candidate);
                        if (candidate is not null)
                        {
                            matchedApplication = MatchMarker(candidate, markerPath);
                            if (matchedApplication is not null)
                            {
                                // Không release application vừa trả về; session sẽ sở hữu RCW này.
                                return matchedApplication;
                            }
                        }
                    }
                    finally
                    {
                        if (!ReferenceEquals(candidate, matchedApplication))
                        {
                            ReleaseCom(candidate);
                        }

                        ReleaseCom(moniker);
                        monikers[0] = null!;
                    }
                }
            }
            finally
            {
                ReleaseCom(enumMoniker);
                ReleaseCom(rot);
            }

            return null;
        }

        private object? MatchMarker(object candidate, string markerPath)
        {
            try
            {
                dynamic item = candidate;

                try
                {
                    var fullName = (string)item.FullName;
                    if (PathsEqual(fullName, markerPath))
                    {
                        dynamic application = item.Application;
                        if (IsExpectedHost(application))
                        {
                            return application;
                        }
                    }
                }
                catch
                {
                    // Không phải Workbook.
                }

                try
                {
                    if (IsExpectedHost(item))
                    {
                        dynamic workbooks = item.Workbooks;
                        var count = (int)workbooks.Count;
                        for (var i = 1; i <= count; i++)
                        {
                            dynamic workbook = workbooks.Item(i);
                            try
                            {
                                if (PathsEqual((string)workbook.FullName, markerPath))
                                {
                                    return item;
                                }
                            }
                            finally
                            {
                                ReleaseCom(workbook);
                            }
                        }
                    }
                }
                catch
                {
                    // Không phải Application.
                }
            }
            catch
            {
                // Bỏ qua object không phải ứng dụng bảng tính.
            }

            return null;
        }

        private bool IsExpectedHost(dynamic application)
        {
            try
            {
                var path = (string)application.Path;
                return _hostKind == ExcelHostKind.Wps
                    ? IsWpsPath(path)
                    : path.Contains("Microsoft Office", StringComparison.OrdinalIgnoreCase)
                      && !IsWpsPath(path);
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left),
                    Path.GetFullPath(right),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string? SafeGet(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private static void ReleaseCom(object? value)
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                try
                {
                    Marshal.ReleaseComObject(value);
                }
                catch
                {
                    // Bỏ qua.
                }
            }
        }

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);
    }
}

internal sealed class SessionConversionResult
{
    public bool Success { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public int MacroLines { get; init; }

    public bool MacroVerified { get; init; }

    public string? ErrorMessage { get; init; }

    public string? Warning { get; init; }
}
