using Microsoft.AspNetCore.Mvc;
using Stp.Contracts;
using Stp.Core.Summary;
using Stp.Api.Services;

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
    private readonly TeamAudit _audit;

    public SummaryToolController(ISummaryToolService summary, TeamAudit audit)
    {
        _summary = summary;
        _audit = audit;
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

    /// <summary>Thông tin phiên để khôi phục sau F5 / mất kết nối.</summary>
    [HttpGet("{sessionId:guid}")]
    public ActionResult<SummaryInspectResponse> GetSession(Guid sessionId)
    {
        var session = _summary.GetSession(sessionId);
        return session is null
            ? NotFoundProblem("Phiên làm việc không tồn tại hoặc đã hết hạn.")
            : Ok(ToResponse(session));
    }

    /// <summary>Danh sách phiên đang dở để mở lại không cần import.</summary>
    [HttpGet("sessions")]
    public ActionResult<IReadOnlyList<SummarySessionInfoResponse>> ListSessions()
    {
        var sessions = _summary.ListSessions()
            .Select(item => new SummarySessionInfoResponse(
                item.SessionId,
                item.FileName,
                item.FileSizeBytes,
                item.CreatedAt,
                item.RowCount))
            .ToList();

        return Ok(sessions);
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
        [FromQuery] bool onlyNeedsNormalization = false,
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
                    missingSheetNumberFirst,
                    onlyNeedsNormalization),
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
        if (ValidateChanges(request.Changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var changes = (request.Changes ?? Array.Empty<SummaryChangeRequest>())
                .Select(ToChange)
                .ToList();

            var preview = _summary
                .Preview(sessionId, changes, request.OnlyRowNumbers)
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

            var result = await _summary.ExportAsync(
                sessionId,
                changes,
                cancellationToken,
                request.OutputFileName,
                request.OnlyRowNumbers);

            _audit.Write(
                await TeamAudit.ActorFromAsync(HttpContext),
                "export",
                "SummarySession",
                sessionId.ToString(),
                $"{result.FileName}: {result.ChangedRows} dòng sửa, {result.NormalizedRows} dòng chuẩn hoá");

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
                cancellationToken,
                request.OutputFileName);

            Response.Headers["X-Summary-Total-Rows"] =
                result.TotalRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Changed-Rows"] =
                result.ChangedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Ignored-Changes"] =
                result.IgnoredChanges.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Normalized-Rows"] =
                result.NormalizedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);

            _audit.Write(
                await TeamAudit.ActorFromAsync(HttpContext),
                "export-filtered",
                "SummarySession",
                sessionId.ToString(),
                $"{result.FileName}: {result.TotalRows} hồ sơ, {result.ChangedRows} dòng sửa, {result.NormalizedRows} dòng chuẩn hoá");

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

    /// <summary>Xem trước mức sẵn sàng in theo năm sau khi áp dụng chỉnh sửa hiện tại.</summary>
    [HttpPost("{sessionId:guid}/export-print/preview")]
    public ActionResult<SummaryPrintPreviewResponse> PreviewPrint(
        Guid sessionId,
        [FromBody] SummaryPrintPreviewRequest request)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        try
        {
            var preview = _summary.PreviewPrint(
                sessionId,
                changes.Select(ToChange).ToList(),
                request.Years);

            return Ok(new SummaryPrintPreviewResponse(
                preview.Years
                    .Select(item => new SummaryPrintYearStatusResponse(
                        item.Year,
                        item.Total,
                        item.Ready,
                        item.MissingNumber,
                        item.MissingDocumentCode,
                        item.MissingDate,
                        item.MissingSheetNumber,
                        item.MissingSummary))
                    .ToList(),
                preview.TotalRows,
                preview.HasIssues));
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
    /// Xuất bản in theo năm: sinh sheet BANG_IN_HANG_LOAT + macro in/lọc.
    /// perYear trả về .zip nhiều .xlsm; combined trả về một .xlsm.
    /// </summary>
    [HttpPost("{sessionId:guid}/export-print")]
    public async Task<IActionResult> ExportPrint(
        Guid sessionId,
        [FromBody] SummaryPrintExportRequest request,
        CancellationToken cancellationToken)
    {
        var changes = request.Changes ?? Array.Empty<SummaryChangeRequest>();
        if (ValidateChanges(changes, allowEmpty: true) is { } validationError)
        {
            return validationError;
        }

        var mode = SummaryPrintOutputMode.PerYear;
        if (!string.IsNullOrWhiteSpace(request.Mode)
            && !Enum.TryParse(request.Mode, ignoreCase: true, out mode))
        {
            return InvalidProblem("Kiểu xuất bản in không hợp lệ (perYear hoặc combined).");
        }

        try
        {
            var result = await _summary.ExportPrintAsync(
                sessionId,
                changes.Select(ToChange).ToList(),
                new SummaryPrintOptions(
                    request.Years,
                    mode,
                    request.AllowIncomplete,
                    request.OutputFileName),
                cancellationToken);

            Response.Headers["X-Summary-Print-Rows"] =
                result.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Print-Changed-Rows"] =
                result.ChangedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Print-Normalized-Rows"] =
                result.NormalizedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Summary-Print-Years"] =
                string.Join(',', result.Years);
            // Header HTTP chỉ nhận ASCII; mã hoá để vẫn chuyển được cảnh báo tiếng Việt.
            Response.Headers["X-Summary-Print-Warnings"] =
                Uri.EscapeDataString(string.Join(" | ", result.Warnings));

            _audit.Write(
                await TeamAudit.ActorFromAsync(HttpContext),
                "export-print",
                "SummarySession",
                sessionId.ToString(),
                $"{result.FileName}: {result.RowCount} hồ sơ in hàng loạt ({string.Join(", ", result.Years)})");

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

    /// <summary>Báo cáo dữ liệu thiếu theo năm cho phiên hiện tại.</summary>
    [HttpGet("{sessionId:guid}/report")]
    public ActionResult<IReadOnlyList<SummaryYearReportResponse>> GetReport(Guid sessionId)
    {
        try
        {
            var report = _summary.GetReport(sessionId)
                .Select(item => new SummaryYearReportResponse(
                    item.Year,
                    item.Total,
                    item.MissingSheetNumber,
                    item.MissingDocumentNumber,
                    item.MissingNumber,
                    item.NeedsNormalization))
                .ToList();

            return Ok(report);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
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
        new(
            change.RowNumber,
            change.Items,
            change.SummaryText,
            change.SheetNumber,
            change.DocumentNumber,
            change.NumberLabel);

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
            request.MissingSheetNumberFirst,
            request.OnlyNeedsNormalization);

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
                .ToList(),
            workbook.NumberColumn,
            workbook.CatalogMeta
                .Select(item => new SummaryCatalogMetaResponse(item.Name, item.UsageCount, item.Source))
                .ToList(),
            workbook.Sheets
                .Select(item => new SummarySheetResponse(item.Name, item.Year, item.RowCount))
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
            row.SheetNumberChanged,
            row.DocumentNumber,
            row.OriginalDocumentNumber,
            row.DocumentNumberChanged,
            row.NumberLabel,
            row.OriginalNumberLabel,
            row.NumberLabelChanged);

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
            row.Type,
            row.NeedsNormalization,
            row.NormalizedSummary,
            row.NormalizedBaseSummary,
            row.SheetName,
            row.SourceRowNumber);
}
