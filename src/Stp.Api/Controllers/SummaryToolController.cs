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
    private const int MaxSummaryLength = 4000;
    private const int MaxSheetNumberLength = 40;

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
        [FromQuery] string? numberMode = null,
        [FromQuery] int? numberFrom = null,
        [FromQuery] int? numberTo = null,
        [FromQuery] int? number = null,
        [FromQuery] int? yearFrom = null,
        [FromQuery] int? yearTo = null,
        [FromQuery] bool onlyMissingSheetNumber = false,
        [FromQuery] bool onlyMissingDocumentNumber = false,
        [FromQuery] bool onlyMissingNumber = false,
        [FromQuery] bool missingSheetNumberFirst = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryCreateScope(
                new SummaryScopeRequest(
                    year,
                    type,
                    search,
                    onlyExisting,
                    numberMode,
                    numberFrom,
                    numberTo,
                    number,
                    yearFrom,
                    yearTo,
                    onlyMissingSheetNumber,
                    onlyMissingDocumentNumber,
                    onlyMissingNumber,
                    missingSheetNumberFirst),
                out var scope,
                out var scopeError))
        {
            return InvalidProblem(scopeError!);
        }

        try
        {
            var rows = _summary.GetRows(sessionId, scope, page, pageSize);

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
                .Select(ToChange)
                .ToList();

            var preview = _summary
                .Preview(sessionId, changes)
                .Select(ToResponse)
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
        var requestedChanges = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(requestedChanges, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var changes = requestedChanges
                .Select(ToChange)
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

    /// <summary>Xem trước bản xuất lọc: số hồ sơ trong phạm vi và các thay đổi được áp dụng.</summary>
    [HttpPost("{sessionId:guid}/export-filtered/preview")]
    public ActionResult<SummaryFilteredPreviewResponse> PreviewFiltered(
        Guid sessionId,
        [FromBody] SummaryFilteredExportRequest request)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        if (!TryCreateScope(request.Scope, out var scope, out var scopeError))
        {
            return InvalidProblem(scopeError!);
        }

        try
        {
            var preview = _summary.PreviewFiltered(
                sessionId,
                changes.Select(ToChange).ToList(),
                scope);

            return Ok(new SummaryFilteredPreviewResponse(
                preview.TotalRows,
                preview.ChangedRows,
                preview.IgnoredChanges,
                preview.NormalizedRows,
                preview.Truncated,
                preview.Items.Select(ToResponse).ToList()));
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

    /// <summary>
    /// Xuất bản lọc: chỉ giữ sheet trích yếu và các hồ sơ trong phạm vi,
    /// áp dụng các thay đổi thuộc phạm vi rồi xoá phần còn lại.
    /// </summary>
    [HttpPost("{sessionId:guid}/export-filtered")]
    public async Task<IActionResult> ExportFiltered(
        Guid sessionId,
        [FromBody] SummaryFilteredExportRequest request,
        CancellationToken cancellationToken)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        if (!TryCreateScope(request.Scope, out var scope, out var scopeError))
        {
            return InvalidProblem(scopeError!);
        }

        try
        {
            var result = await _summary.ExportFilteredAsync(
                sessionId,
                changes.Select(ToChange).ToList(),
                scope,
                cancellationToken);

            Response.Headers["X-Summary-Total-Rows"] =
                result.TotalRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Changed-Rows"] =
                result.ChangedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Ignored-Changes"] =
                result.IgnoredChanges.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Normalized-Rows"] =
                result.NormalizedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

    private ActionResult? ValidateChanges(
        IReadOnlyList<SummaryChangeRequest>? changes,
        bool allowEmpty = false)
    {
        if (changes is null || changes.Count == 0)
        {
            return allowEmpty ? null : InvalidProblem("Chưa có thay đổi nào.");
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

            if (change.SummaryText is { Length: > MaxSummaryLength })
            {
                return InvalidProblem(
                    $"Trích yếu không được dài quá {MaxSummaryLength} ký tự.");
            }

            if (change.SheetNumber is { Length: > MaxSheetNumberLength })
            {
                return InvalidProblem(
                    $"Số tờ không được dài quá {MaxSheetNumberLength} ký tự.");
            }
        }

        return null;
    }

    private static SummaryChange ToChange(SummaryChangeRequest change) =>
        new(change.RowNumber, change.Items, change.SummaryText, change.SheetNumber);

    private static bool TryCreateScope(
        SummaryScopeRequest? request,
        out SummaryScope scope,
        out string? error)
    {
        scope = SummaryScope.All;
        error = null;

        if (request is null)
        {
            return true;
        }

        if (request.Year.HasValue && request.Year.Value is < 1900 or > 2200)
        {
            error = "Năm lọc không hợp lệ.";
            return false;
        }

        if ((request.YearFrom.HasValue && request.YearFrom.Value is < 1900 or > 2200)
            || (request.YearTo.HasValue && request.YearTo.Value is < 1900 or > 2200))
        {
            error = "Khoảng năm không hợp lệ.";
            return false;
        }

        if (request.YearFrom.HasValue
            && request.YearTo.HasValue
            && request.YearFrom.Value > request.YearTo.Value)
        {
            error = "Năm bắt đầu không được lớn hơn năm kết thúc.";
            return false;
        }

        var mode = SummaryNumberMode.All;
        if (!string.IsNullOrWhiteSpace(request.NumberMode)
            && !Enum.TryParse(request.NumberMode, ignoreCase: true, out mode))
        {
            error = "Kiểu lọc số hồ sơ không hợp lệ.";
            return false;
        }

        if (mode == SummaryNumberMode.Range)
        {
            if (!request.NumberFrom.HasValue && !request.NumberTo.HasValue)
            {
                error = "Nhập ít nhất một đầu của khoảng số hồ sơ.";
                return false;
            }

            if ((request.NumberFrom.HasValue && request.NumberFrom.Value <= 0)
                || (request.NumberTo.HasValue && request.NumberTo.Value <= 0))
            {
                error = "Số hồ sơ phải lớn hơn 0.";
                return false;
            }

            if (request.NumberFrom.HasValue
                && request.NumberTo.HasValue
                && request.NumberFrom.Value > request.NumberTo.Value)
            {
                error = "Số bắt đầu không được lớn hơn số kết thúc.";
                return false;
            }
        }
        else if (mode == SummaryNumberMode.Single)
        {
            if (!request.Number.HasValue || request.Number.Value <= 0)
            {
                error = "Nhập số hồ sơ cần lọc.";
                return false;
            }
        }

        scope = new SummaryScope(
            request.Year,
            request.Type,
            request.Search,
            request.OnlyExisting,
            mode,
            mode == SummaryNumberMode.Range ? request.NumberFrom : null,
            mode == SummaryNumberMode.Range ? request.NumberTo : null,
            mode == SummaryNumberMode.Single ? request.Number : null,
            request.YearFrom,
            request.YearTo,
            request.OnlyMissingSheetNumber,
            request.OnlyMissingDocumentNumber,
            request.OnlyMissingNumber,
            request.MissingSheetNumberFirst);

        return true;
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
            workbook.SheetNumberColumn,
            workbook.SheetNumberHeader,
            workbook.DocumentNumberColumn,
            workbook.Rows.Count,
            workbook.ExistingListCount,
            workbook.BrokenSummaryCount,
            workbook.FormulaSummaryCount,
            workbook.MissingSheetNumberCount,
            workbook.MissingDocumentNumberCount,
            workbook.MissingNumberCount,
            workbook.NormalizableCount,
            workbook.Catalog,
            workbook.Years
                .Select(item => new SummaryYearResponse(item.Year, item.Count))
                .ToList(),
            workbook.Types
                .Select(item => new SummaryTypeResponse(item.Name, item.Count))
                .ToList());

    private static SummaryPreviewRowResponse ToResponse(SummaryPreviewRow row) =>
        new(
            row.RowNumber,
            row.Summary,
            row.Changed,
            row.OriginalSummary,
            row.SheetNumber,
            row.OriginalSheetNumber,
            row.SummaryChanged,
            row.SheetNumberChanged);

    private static SummaryRowResponse ToResponse(SummaryRow row) =>
        new(
            row.RowNumber,
            row.Year,
            row.NumberLabel,
            row.DocumentNumber,
            row.SheetNumber,
            row.OriginalSummary,
            row.BaseSummary,
            row.Items,
            row.HasDocumentList,
            row.HasFormula,
            row.IsBrokenSummary,
            row.Group,
            row.Type);
}
