using Microsoft.AspNetCore.Mvc;
using Stp.Api.Mapping;
using Stp.Contracts;
using Stp.Core.Excel;
using Stp.Core.Storage;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/inspect")]
public sealed class InspectController : ControllerBase
{
    private const long UploadRequestLimit = 210L * 1024 * 1024;

    private readonly IExcelInspector _inspector;
    private readonly IFileStorage _storage;

    public InspectController(IExcelInspector inspector, IFileStorage storage)
    {
        _inspector = inspector;
        _storage = storage;
    }

    /// <summary>Kiểm tra nhanh một workbook trước khi chuyển đổi.</summary>
    [HttpPost]
    [RequestSizeLimit(UploadRequestLimit)]
    public async Task<ActionResult<InspectionResponse>> Post(
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

        var uploadId = Guid.NewGuid();
        try
        {
            await using var stream = file.OpenReadStream();
            var stored = await _storage.SaveUploadAsync(
                stream,
                file.FileName,
                uploadId,
                cancellationToken);

            var inspection = _inspector.Inspect(stored.FullPath);
            return Ok(inspection.ToResponse());
        }
        catch (Exception ex) when (ex is InvalidDataException
                                       or NotSupportedException
                                       or IOException)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Không đọc được file",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        finally
        {
            _storage.DeleteDirectory(_storage.GetJobUploadDirectory(uploadId));
        }
    }
}
