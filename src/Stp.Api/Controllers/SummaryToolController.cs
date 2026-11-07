using Microsoft.AspNetCore.Mvc;
using Stp.Contracts;
using Stp.Core.Summary;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/summary")]
public sealed class SummaryToolController : ControllerBase
{
    private const long UploadRequestLimit = 210L * 1024 * 1024;
    private const int MaxChanges = 5000;
    private const int MaxItemsPerRow = 80;
    private const int MaxItemLength = 200;

    private readonly ISummaryToolService _summary;

    public SummaryToolController(ISummaryToolService summary)
    {
        _summary = summary;
    }

    /// <summary>Mở workbook và tách danh mục giấy tờ trong cột trích yếu.</summary>
    [HttpPost("inspect")]
    [RequestSizeLimit(UploadRequestLimit)]
    public async Task<ActionResult<SummaryInspectResponse>> Inspect(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "File rỗng",
                Detail = "File upload không có nội dung.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var session = await _summary.CreateSessionAsync(
                stream,
                file.FileName,
                cancellationToken);

            return Ok(ToResponse(session));
        }
        catch (Exception ex) when (ex is InvalidDataException
                                       or NotSupportedException
                                       or IOException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Không đọc được workbook",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    /// <summary>Danh sách dòng hồ sơ, lọc theo năm/loại/nội dung.</summary>
    [HttpGet("{sessionId:guid}/rows")]
    public ActionResult<SummaryRowsResponse> GetRows(
        Guid sessionId,
        [FromQuery] int? year,
        [FromQuery] string? type,
        [FromQuery] string? search,
        [FromQuery] bool onlyExisting = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var rows = _summary.GetRows(
                sessionId,
                year,
                type,
                search,
                onlyExisting,
                page,
                pageSize);

            return Ok(new SummaryRowsResponse(
                rows.Items.Select(ToResponse).ToList(),
                rows.Total,
                rows.Page,
                rows.PageSize));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
    }

    /// <summary>Xem trước câu trích yếu sau khi bổ sung danh mục.</summary>
    [HttpPost("{sessionId:guid}/preview")]
    public ActionResult<IReadOnlyList<SummaryPreviewRowResponse>> Preview(
        Guid sessionId,
        [FromBody] SummaryPreviewRequest request)
    {
        if (ValidateChanges(request.Changes) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var changes = request.Changes
                .Select(change => new SummaryChange(change.RowNumber, change.Items))
                .ToList();

            var preview = _summary
                .Preview(sessionId, changes)
                .Select(row => new SummaryPreviewRowResponse(row.RowNumber, row.Summary, row.Changed))
                .ToList();

            return Ok(preview);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>Xuất lại đúng workbook gốc, chỉ thay các ô trích yếu đã chọn.</summary>
    [HttpPost("{sessionId:guid}/export")]
    public async Task<IActionResult> Export(
        Guid sessionId,
        [FromBody] SummaryExportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Changes.Count == 0)
        {
            return InvalidProblem("Chưa chọn dòng nào để xuất.");
        }

        if (ValidateChanges(request.Changes) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var changes = request.Changes
                .Select(change => new SummaryChange(change.RowNumber, change.Items))
                .ToList();

            var result = await _summary.ExportAsync(sessionId, changes, cancellationToken);
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or InvalidDataException)
        {
            return InvalidProblem(ex.Message);
        }
    }

    /// <summary>Kết thúc phiên làm việc và xoá file upload tạm.</summary>
    [HttpDelete("{sessionId:guid}")]
    public IActionResult Delete(Guid sessionId)
    {
        return _summary.DeleteSession(sessionId)
            ? NoContent()
            : NotFoundProblem("Phiên làm việc không tồn tại hoặc đã hết hạn.");
    }

    private ActionResult? ValidateChanges(IReadOnlyList<SummaryChangeRequest>? changes)
    {
        if (changes is null || changes.Count == 0)
        {
            return InvalidProblem("Chưa có thay đổi nào.");
        }

        if (changes.Count > MaxChanges)
        {
            return InvalidProblem($"Một lần chỉ xuất tối đa {MaxChanges} dòng.");
        }

        foreach (var change in changes)
        {
            if (change.RowNumber <= 0)
            {
                return InvalidProblem("Số dòng không hợp lệ.");
            }

            if (change.Items is null || change.Items.Count > MaxItemsPerRow)
            {
                return InvalidProblem(
                    $"Mỗi dòng chỉ chọn tối đa {MaxItemsPerRow} loại giấy tờ.");
            }

            if (change.Items.Any(item => item is null || item.Length > MaxItemLength))
            {
                return InvalidProblem(
                    $"Tên giấy tờ không được dài quá {MaxItemLength} ký tự.");
            }
        }

        return null;
    }

    private ObjectResult InvalidProblem(string detail) =>
        BadRequest(new ProblemDetails
        {
            Title = "Dữ liệu chưa hợp lệ",
            Detail = detail,
            Status = StatusCodes.Status400BadRequest
        });

    private ObjectResult NotFoundProblem(string detail) =>
        NotFound(new ProblemDetails
        {
            Title = "Không tìm thấy phiên làm việc",
            Detail = detail,
            Status = StatusCodes.Status404NotFound
        });

    private static SummaryInspectResponse ToResponse(SummaryWorkbook workbook) =>
        new(
            workbook.SessionId,
            workbook.FileName,
            workbook.FileSizeBytes,
            workbook.SheetName,
            workbook.SummaryColumn,
            workbook.SummaryHeader,
            workbook.Rows.Count,
            workbook.ExistingListCount,
            workbook.BrokenSummaryCount,
            workbook.FormulaSummaryCount,
            workbook.Catalog,
            workbook.Years
                .Select(item => new SummaryYearResponse(item.Year, item.Count))
                .ToList(),
            workbook.Types
                .Select(item => new SummaryTypeResponse(item.Name, item.Count))
                .ToList());

    private static SummaryRowResponse ToResponse(SummaryRow row) =>
        new(
            row.RowNumber,
            row.Year,
            row.NumberLabel,
            row.OriginalSummary,
            row.BaseSummary,
            row.Items,
            row.HasDocumentList,
            row.HasFormula,
            row.IsBrokenSummary,
            row.Group,
            row.Type);
}
