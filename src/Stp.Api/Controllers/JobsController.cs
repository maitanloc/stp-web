using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stp.Api.Mapping;
using Stp.Api.Models;
using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Core.Jobs;
using Stp.Core.Storage;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController : ControllerBase
{
    private const long UploadRequestLimit = 420L * 1024 * 1024;

    private readonly StpDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IBatchJobQueue _queue;
    private readonly StorageOptions _storageOptions;

    public JobsController(
        StpDbContext db,
        IFileStorage storage,
        IBatchJobQueue queue,
        IOptions<StorageOptions> storageOptions)
    {
        _db = db;
        _storage = storage;
        _queue = queue;
        _storageOptions = storageOptions.Value;
    }

    /// <summary>Tạo job chuyển đổi hàng loạt (mỗi file upload thành một .xlsm).</summary>
    [HttpPost("convert")]
    [RequestSizeLimit(UploadRequestLimit)]
    public Task<ActionResult<JobCreatedResponse>> CreateConversion(
        [FromForm] JobUploadForm form,
        CancellationToken cancellationToken)
    {
        var options = new ConversionJobOptions
        {
            OriginalFileMode = ParseOriginalFileMode(form.OriginalFileMode),
            CreateZip = form.CreateZip ?? true,
            MergeOutput = form.MergeOutput ?? true,
            MergeOnly = false,
            GeneratePrintSheets = form.GeneratePrintSheets ?? true,
            InjectMacro = form.InjectMacro ?? true,
            HeaderText = form.HeaderText,
            FooterText = form.FooterText
        };

        return CreateJobAsync(BatchJobType.Convert, form.Files, options, cancellationToken);
    }

    /// <summary>Tạo job gộp nhiều workbook cùng cấu trúc thành một file.</summary>
    [HttpPost("merge")]
    [RequestSizeLimit(UploadRequestLimit)]
    public Task<ActionResult<JobCreatedResponse>> CreateMerge(
        [FromForm] JobUploadForm form,
        CancellationToken cancellationToken)
    {
        var options = new ConversionJobOptions
        {
            OriginalFileMode = OriginalFileMode.OutputFolder,
            CreateZip = false,
            MergeOutput = false,
            MergeOnly = true,
            GeneratePrintSheets = form.GeneratePrintSheets ?? true,
            InjectMacro = form.InjectMacro ?? true,
            HeaderText = form.HeaderText,
            FooterText = form.FooterText
        };

        return CreateJobAsync(BatchJobType.Merge, form.Files, options, cancellationToken);
    }

    /// <summary>Danh sách job gần đây, lọc theo trạng thái.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<JobSummaryResponse>>> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.BatchJobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BatchJobStatus>(status, ignoreCase: true, out var parsed))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Trạng thái không hợp lệ",
                    Detail = $"Không hiểu trạng thái '{status}'.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            query = query.Where(job => job.Status == parsed);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(job => job.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<JobSummaryResponse>(
            items.Select(ApiMapping.ToSummary).ToList(),
            total,
            page,
            pageSize));
    }

    /// <summary>Chi tiết job kèm tiến độ từng file và log.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobDetailResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await _db.BatchJobs
            .AsNoTracking()
            .Include(entity => entity.Items)
            .Include(entity => entity.Logs)
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (job is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy job",
                Detail = $"Job {id} không tồn tại.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(new JobDetailResponse(
            job.ToSummary(),
            ConversionJobOptions.FromJson(job.OptionsJson).ToResponse(),
            job.Items
                .OrderBy(item => item.InputFileName, StringComparer.OrdinalIgnoreCase)
                .Select(ApiMapping.ToResponse)
                .ToList(),
            job.Logs
                .OrderBy(log => log.Timestamp)
                .Select(ApiMapping.ToResponse)
                .ToList()));
    }

    /// <summary>Tải file kết quả của job (ZIP hoặc workbook gộp).</summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var job = await _db.BatchJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (job?.OutputPath is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Chưa có file kết quả",
                Detail = "Job chưa hoàn tất hoặc không tạo được file.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return DownloadFile(job.OutputPath);
    }

    /// <summary>Tải một file .xlsm cụ thể trong job.</summary>
    [HttpGet("{id:guid}/files/{itemId:guid}")]
    public async Task<IActionResult> DownloadItem(
        Guid id,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await _db.BatchJobItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entity => entity.Id == itemId && entity.JobId == id,
                cancellationToken);

        if (item?.OutputPath is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Chưa có file kết quả",
                Detail = "File này chưa được xử lý xong.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return DownloadFile(item.OutputPath);
    }

    /// <summary>Huỷ job đang chờ hoặc đang chạy.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var job = await _db.BatchJobs.FirstOrDefaultAsync(
            entity => entity.Id == id,
            cancellationToken);

        if (job is null)
        {
            return NotFound();
        }

        if (job.Status is BatchJobStatus.Completed or BatchJobStatus.Failed)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Job đã kết thúc",
                Detail = $"Job đang ở trạng thái {job.Status}.",
                Status = StatusCodes.Status409Conflict
            });
        }

        job.Status = BatchJobStatus.Cancelled;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(job.ToSummary());
    }

    private async Task<ActionResult<JobCreatedResponse>> CreateJobAsync(
        BatchJobType type,
        IFormFileCollection files,
        ConversionJobOptions options,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Chưa chọn file",
                Detail = "Cần chọn ít nhất một file Excel (.xlsx/.xlsm).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (files.Count > _storageOptions.MaxFilesPerJob)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Quá nhiều file",
                Detail = $"Mỗi job chỉ nhận tối đa {_storageOptions.MaxFilesPerJob} file.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var job = new BatchJob
        {
            Id = Guid.NewGuid(),
            Type = type,
            Status = BatchJobStatus.Queued,
            OptionsJson = options.ToJson(),
            TotalItems = files.Count
        };

        try
        {
            foreach (var file in files)
            {
                await using var stream = file.OpenReadStream();
                var stored = await _storage.SaveUploadAsync(
                    stream,
                    file.FileName,
                    job.Id,
                    cancellationToken);

                job.Items.Add(new BatchJobItem
                {
                    JobId = job.Id,
                    InputFileName = stored.FileName,
                    InputPath = stored.FullPath,
                    Status = BatchJobStatus.Queued
                });
            }

            _db.BatchJobs.Add(job);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException
                                       or NotSupportedException
                                       or IOException)
        {
            _storage.DeleteDirectory(_storage.GetJobUploadDirectory(job.Id));
            return BadRequest(new ProblemDetails
            {
                Title = "Không nhận được file",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        await _queue.EnqueueAsync(job.Id, cancellationToken);

        return AcceptedAtAction(
            nameof(Get),
            new { id = job.Id },
            new JobCreatedResponse(
                job.Id,
                job.Status.ToString(),
                type == BatchJobType.Merge
                    ? $"Đã nhận {files.Count} file, job gộp đang chờ xử lý."
                    : $"Đã nhận {files.Count} file, job chuyển đổi đang chờ xử lý."));
    }

    private IActionResult DownloadFile(string path)
    {
        if (!_storage.IsUnderRoot(path) || !System.IO.File.Exists(path))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy file",
                Detail = "File kết quả không còn trên máy chủ.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var fileName = Path.GetFileName(path);
        return PhysicalFile(path, GetContentType(fileName), fileName);
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".zip" => "application/zip",
            ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static OriginalFileMode ParseOriginalFileMode(int? value)
    {
        if (value is null)
        {
            return OriginalFileMode.Copy;
        }

        return Enum.IsDefined(typeof(OriginalFileMode), value.Value)
            ? (OriginalFileMode)value.Value
            : OriginalFileMode.Copy;
    }
}
