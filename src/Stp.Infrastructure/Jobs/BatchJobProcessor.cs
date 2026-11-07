using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stp.Core.Domain;
using Stp.Core.Excel;
using Stp.Core.Jobs;
using Stp.Core.Storage;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Persistence;

namespace Stp.Infrastructure.Jobs;

/// <summary>
/// Xử lý một job chuyển đổi hoặc gộp. Chạy tuần tự trong worker, cập nhật
/// trạng thái từng file và log để SPA hiển thị.
/// </summary>
public sealed class BatchJobProcessor : IBatchJobProcessor
{
    private readonly StpDbContext _db;
    private readonly IExcelInspector _inspector;
    private readonly IExcelConversionService _conversion;
    private readonly IMergedWorkbookService _merge;
    private readonly MacroTemplateProvider _macroProvider;
    private readonly IFileStorage _storage;
    private readonly ILogger<BatchJobProcessor> _logger;

    public BatchJobProcessor(
        StpDbContext db,
        IExcelInspector inspector,
        IExcelConversionService conversion,
        IMergedWorkbookService merge,
        MacroTemplateProvider macroProvider,
        IFileStorage storage,
        ILogger<BatchJobProcessor> logger)
    {
        _db = db;
        _inspector = inspector;
        _conversion = conversion;
        _merge = merge;
        _macroProvider = macroProvider;
        _storage = storage;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.BatchJobs
            .Include(entity => entity.Items)
            .Include(entity => entity.Logs)
            .FirstOrDefaultAsync(entity => entity.Id == jobId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
        {
            _logger.LogWarning("Không tìm thấy job {JobId}", jobId);
            return;
        }

        if (job.Status is BatchJobStatus.Completed
            or BatchJobStatus.Failed
            or BatchJobStatus.Cancelled)
        {
            return;
        }

        var options = ConversionJobOptions.FromJson(job.OptionsJson);

        try
        {
            job.Status = BatchJobStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = null;
            AddLog(
                job,
                "Information",
                job.Type == BatchJobType.Merge
                    ? $"Bắt đầu gộp {job.TotalItems} file."
                    : $"Bắt đầu chuyển đổi {job.TotalItems} file.");
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (job.Type == BatchJobType.Merge)
            {
                await ProcessMergeJobAsync(job, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ProcessConversionJobAsync(job, options, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = BatchJobStatus.Queued;
            AddLog(job, "Warning", "Server dừng; job sẽ tiếp tục sau khi khởi động lại.");
            await SaveIgnoringCancellationAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} thất bại", jobId);
            job.Status = BatchJobStatus.Failed;
            job.ErrorMessage = Truncate(ex.Message, 2000);
            job.CompletedAt = DateTimeOffset.UtcNow;
            AddLog(job, "Error", "Job thất bại: " + ex.Message);
            await SaveIgnoringCancellationAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessConversionJobAsync(
        BatchJob job,
        ConversionJobOptions options,
        CancellationToken cancellationToken)
    {
        var outputDirectory = _storage.GetJobOutputDirectory(job.Id);
        Directory.CreateDirectory(outputDirectory);

        var succeeded = new List<BatchJobItem>();
        job.SucceededItems = 0;
        job.FailedItems = 0;
        var orderedItems = job.Items
            .OrderBy(item => item.InputFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in orderedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Status == BatchJobStatus.Completed
                && !string.IsNullOrWhiteSpace(item.OutputPath)
                && File.Exists(item.OutputPath))
            {
                succeeded.Add(item);
                job.SucceededItems++;
                continue;
            }

            if (await IsCancelledAsync(job.Id, cancellationToken).ConfigureAwait(false))
            {
                job.Status = BatchJobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                AddLog(job, "Warning", "Job đã bị huỷ theo yêu cầu.");
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(item.InputPath) || !File.Exists(item.InputPath))
            {
                MarkFailed(job, item, "Không tìm thấy file upload.");
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            item.Status = BatchJobStatus.Running;
            item.StartedAt = DateTimeOffset.UtcNow;
            item.CompletedAt = null;
            item.ErrorMessage = null;
            AddLog(job, "Information", $"Đang xử lý {item.InputFileName}.");
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var inspection = _inspector.Inspect(item.InputPath);
                item.WorkbookKind = inspection.Kind;
                item.DossierCount = inspection.DossierCount;

                if (options.OriginalFileMode == OriginalFileMode.InPlaceWithBackup)
                {
                    var backup = await _storage.BackupAsync(
                        item.InputPath,
                        job.Id,
                        cancellationToken).ConfigureAwait(false);
                    AddLog(
                        job,
                        "Information",
                        $"Đã sao lưu {item.InputFileName} vào " +
                        $"{Path.GetRelativePath(_storage.RootPath, backup.FullPath)}.");
                }

                var outputPath = ResolveOutputPath(item, options);
                var outcome = await _conversion.ConvertAsync(new ConversionRequest
                {
                    InputPath = item.InputPath,
                    OutputPath = outputPath,
                    MacroCode = _macroProvider.DefaultMacroCode,
                    VerifyMacro = true,
                    ReplaceThisWorkbookCode = true,
                    InjectMacro = options.InjectMacro,
                    GeneratePrintSheets = options.GeneratePrintSheets,
                    WorkingDirectory = outputDirectory
                }, cancellationToken).ConfigureAwait(false);

                if (!outcome.Success)
                {
                    throw new InvalidOperationException(
                        outcome.ErrorMessage ?? "Chuyển đổi thất bại.");
                }

                item.OutputPath = outcome.OutputPath;
                item.Status = BatchJobStatus.Completed;
                item.CompletedAt = DateTimeOffset.UtcNow;
                job.SucceededItems++;
                succeeded.Add(item);

                var warning = string.IsNullOrWhiteSpace(outcome.Warning)
                    ? string.Empty
                    : $" Cảnh báo: {outcome.Warning}";
                AddLog(
                    job,
                    "Information",
                    $"Xong {item.InputFileName} trong {outcome.Duration.TotalSeconds:F1} giây." +
                    warning);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MarkFailed(job, item, ex.Message);
                _logger.LogWarning(
                    ex,
                    "File {File} trong job {JobId} thất bại",
                    item.InputFileName,
                    job.Id);
            }

            item.CompletedAt ??= DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        string? mergedPath = null;
        if (options.MergeOutput && succeeded.Count > 0)
        {
            mergedPath = await MergeInputsAsync(
                    job,
                    succeeded.Select(item => item.InputPath!).ToList(),
                    succeeded.Sum(item => item.DossierCount),
                    outputDirectory,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var outputFiles = succeeded
            .Select(item => item.OutputPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(mergedPath))
        {
            outputFiles.Add(mergedPath);
        }

        if (options.CreateZip && outputFiles.Count > 0)
        {
            var zipPath = _storage.GetUniquePath(
                outputDirectory,
                $"STP_{DateTime.Now:yyyyMMdd_HHmm}.zip");
            CreateZip(zipPath, outputFiles);
            job.OutputPath = zipPath;
            AddLog(job, "Information", $"Đã tạo ZIP: {Path.GetFileName(zipPath)}.");
        }
        else
        {
            job.OutputPath = mergedPath ?? outputFiles.FirstOrDefault();
        }

        if (await IsCancelledAsync(job.Id, cancellationToken).ConfigureAwait(false))
        {
            job.Status = BatchJobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            AddLog(job, "Warning", "Job đã bị huỷ theo yêu cầu.");
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        job.CompletedAt = DateTimeOffset.UtcNow;
        job.Status = job.SucceededItems == 0 && job.FailedItems > 0
            ? BatchJobStatus.Failed
            : BatchJobStatus.Completed;

        AddLog(
            job,
            job.FailedItems > 0 ? "Warning" : "Information",
            $"Hoàn tất: {job.SucceededItems} thành công, {job.FailedItems} lỗi.");

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessMergeJobAsync(
        BatchJob job,
        ConversionJobOptions options,
        CancellationToken cancellationToken)
    {
        var outputDirectory = _storage.GetJobOutputDirectory(job.Id);
        Directory.CreateDirectory(outputDirectory);

        var items = job.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.InputPath) && File.Exists(item.InputPath))
            .OrderBy(item => item.InputFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            MarkAllFailed(job, "Không có file hợp lệ để gộp.");
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var item in items)
        {
            item.Status = BatchJobStatus.Running;
            item.StartedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dossierCount = 0;
        foreach (var item in items)
        {
            try
            {
                var inspection = _inspector.Inspect(item.InputPath!);
                item.WorkbookKind = inspection.Kind;
                item.DossierCount = inspection.DossierCount;
                dossierCount += inspection.DossierCount;
            }
            catch (Exception ex)
            {
                item.ErrorMessage = ex.Message;
            }
        }

        var mergedPath = await MergeInputsAsync(
                job,
                items.Select(item => item.InputPath!).ToList(),
                dossierCount,
                outputDirectory,
                options,
                cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(mergedPath))
        {
            MarkAllFailed(job, "Không tạo được workbook gộp.");
            job.Status = BatchJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (await IsCancelledAsync(job.Id, cancellationToken).ConfigureAwait(false))
        {
            job.Status = BatchJobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            AddLog(job, "Warning", "Job đã bị huỷ theo yêu cầu.");
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var item in items)
        {
            item.Status = BatchJobStatus.Completed;
            item.OutputPath = mergedPath;
            item.CompletedAt = DateTimeOffset.UtcNow;
            job.SucceededItems++;
        }

        job.OutputPath = mergedPath;
        job.Status = BatchJobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        AddLog(
            job,
            "Information",
            $"Đã gộp {items.Count} file thành {Path.GetFileName(mergedPath)}.");
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> MergeInputsAsync(
        BatchJob job,
        IReadOnlyList<string> inputPaths,
        int dossierCount,
        string outputDirectory,
        ConversionJobOptions options,
        CancellationToken cancellationToken)
    {
        var mergedXlsxPath = _storage.GetUniquePath(
            outputDirectory,
            $"GOP_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");

        AddLog(job, "Information", $"Bắt đầu gộp {inputPaths.Count} file.");
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var merge = _merge.Merge(new MergeRequest
        {
            InputPaths = inputPaths,
            OutputPath = mergedXlsxPath,
            Overwrite = true,
            GeneratePrintSheets = options.GeneratePrintSheets
        }, cancellationToken);

        if (!merge.Success)
        {
            AddLog(
                job,
                "Warning",
                "Gộp workbook thất bại: " + (merge.ErrorMessage ?? "không rõ lỗi."));
            TryDelete(mergedXlsxPath);
            return null;
        }

        AddLog(
            job,
            "Information",
            $"Đã gộp {merge.FileCount} file, {merge.YearCount} năm, " +
            $"{merge.DossierCount} hồ sơ, {merge.DuplicateCount} hồ sơ trùng.");

        foreach (var warning in merge.Warnings)
        {
            AddLog(job, "Warning", warning);
        }

        var mergedXlsmPath = _storage.GetUniquePath(
            outputDirectory,
            Path.GetFileNameWithoutExtension(mergedXlsxPath) + ".xlsm");

        try
        {
            var conversion = await _conversion.ConvertAsync(new ConversionRequest
            {
                InputPath = mergedXlsxPath,
                OutputPath = mergedXlsmPath,
                MacroCode = _macroProvider.DefaultMacroCode,
                VerifyMacro = true,
                ReplaceThisWorkbookCode = true,
                InjectMacro = options.InjectMacro,
                GeneratePrintSheets = false,
                WorkingDirectory = outputDirectory
            }, cancellationToken).ConfigureAwait(false);

            if (!conversion.Success)
            {
                AddLog(
                    job,
                    "Warning",
                    "Không chuyển được workbook gộp sang .xlsm: " +
                    (conversion.ErrorMessage ?? "không rõ lỗi.") +
                    " Giữ lại file .xlsx.");
                return mergedXlsxPath;
            }

            TryDelete(mergedXlsxPath);
            return conversion.OutputPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddLog(
                job,
                "Warning",
                "Không chuyển được workbook gộp sang .xlsm: " + ex.Message +
                " Giữ lại file .xlsx.");
            return mergedXlsxPath;
        }
    }

    private string ResolveOutputPath(BatchJobItem item, ConversionJobOptions options)
    {
        var inputPath = item.InputPath!;
        var baseName = Path.GetFileNameWithoutExtension(item.InputFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = Path.GetFileNameWithoutExtension(inputPath);
        }

        if (options.OriginalFileMode == OriginalFileMode.InPlaceWithBackup)
        {
            // BackupAsync được gọi ngay sau khi resolve để giữ nguyên file gốc.
            var uploadDirectory = Path.GetDirectoryName(inputPath)!;
            return _storage.GetUniquePath(uploadDirectory, baseName + ".xlsm");
        }

        return _storage.GetUniquePath(
            _storage.GetJobOutputDirectory(item.JobId),
            baseName + ".xlsm");
    }

    private async Task<bool> IsCancelledAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var status = await _db.BatchJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => job.Status)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return status == BatchJobStatus.Cancelled;
    }

    private void MarkFailed(BatchJob job, BatchJobItem item, string message)
    {
        item.Status = BatchJobStatus.Failed;
        item.ErrorMessage = Truncate(message, 2000);
        item.CompletedAt = DateTimeOffset.UtcNow;
        job.FailedItems++;
        AddLog(job, "Error", $"Lỗi {item.InputFileName}: {message}");
    }

    private static void MarkAllFailed(BatchJob job, string message)
    {
        foreach (var item in job.Items)
        {
            item.Status = BatchJobStatus.Failed;
            item.ErrorMessage = message;
            item.CompletedAt = DateTimeOffset.UtcNow;
            job.FailedItems++;
        }

        job.ErrorMessage = message;
    }

    private void AddLog(
        BatchJob job,
        string level,
        string message,
        string? details = null)
    {
        var log = new JobLog
        {
            JobId = job.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Level = level,
            Message = Truncate(message, 2000),
            Details = details
        };

        // Thêm trực tiếp vào DbSet: log mới có Guid sẵn, nếu chỉ gắn qua
        // navigation job.Logs EF sẽ tưởng là entity cũ và phát sinh UPDATE.
        _db.JobLogs.Add(log);
        job.Logs.Add(log);
    }

    private static void CreateZip(string zipPath, IReadOnlyList<string> files)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in files
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var entry = archive.CreateEntry(
                Path.GetFileName(file),
                CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var source = File.OpenRead(file);
            source.CopyTo(entryStream);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private async Task SaveIgnoringCancellationAsync()
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không lưu được trạng thái job");
        }
    }
}
