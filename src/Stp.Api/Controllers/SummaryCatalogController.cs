using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stp.Contracts;
using Stp.Core.Summary;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

/// <summary>
/// "Danh mục học được" — loại giấy tờ người dùng thêm lưu bền database:
/// thêm lần đầu, các phiên sau không gõ lại. Mục User do người dùng tự thêm
/// (tự lưu ngay khi gõ, không cần bước riêng); seed Learned từ kho hồ sơ.
/// Endpoint mở như mọi endpoint công cụ trích yếu.
/// </summary>
[ApiController]
[Route("api/summary/catalog")]
public sealed class SummaryCatalogController : ControllerBase
{
    private const int MaxItemLength = 300;

    private readonly ICatalogDictionary _catalog;

    public SummaryCatalogController(ICatalogDictionary catalog)
    {
        _catalog = catalog;
    }

    /// <summary>Danh sách mục đang hoạt động, sắp theo tần suất "hay gặp nhất".</summary>
    [HttpGet]
    public ActionResult<SummaryCatalogListResponse> List() =>
        Ok(new SummaryCatalogListResponse(
            _catalog.GetActive()
                .Select(ToResponse)
                .ToList()));

    /// <summary>
    /// Thêm mục mới hay tăng bộ đếm nếu đã có (qua khoá chuẩn hoá không dấu).
    /// Trả về mục canonical — caller dùng cách viết này cho chip.
    /// </summary>
    [HttpPost("items")]
    public ActionResult<SummaryCatalogItemResponse> Upsert(
        [FromBody] SummaryCatalogUpsertRequest request)
    {
        var display = NormalizeDisplay(request.DisplayName);
        if (display.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tên loại giấy tờ không hợp lệ",
                Detail = "Nhập tên loại giấy tờ không rỗng và không chỉ khoảng trắng.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (display.Length > MaxItemLength)
        {
            display = display[..MaxItemLength];
        }

        var entry = _catalog.Upsert(display, request.DossierTypeHint, CatalogEntrySource.User);
        return Ok(ToResponse(entry));
    }

    /// <summary>Đổi tên canonical hoặc ẩn mục khỏi gợi ý (không xoá dữ liệu).</summary>
    [HttpPatch("items/{id:guid}")]
    public ActionResult<SummaryCatalogItemResponse> Patch(
        Guid id,
        [FromBody] SummaryCatalogPatchRequest request)
    {
        if (request.IsDisabled.HasValue)
        {
            _catalog.SetDisabled(id, request.IsDisabled.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            try
            {
                _catalog.Rename(id, request.DisplayName);
            }
            catch (InvalidOperationException caught)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Không đổi được tên mục",
                    Detail = caught.Message,
                    Status = StatusCodes.Status409Conflict
                });
            }
            catch (ArgumentException caught)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Tên loại giấy tờ không hợp lệ",
                    Detail = caught.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        var entry = _catalog
            .GetActive()
            .FirstOrDefault(item => item.Id == id);
        if (entry is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy mục",
                Detail = "Mục đã bị ẩn hoặc không tồn tại.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(ToResponse(entry));
    }

    /// <summary>
    /// Nạp danh mục học được từ kho hồ sơ (mức MinCount, mặc định 50): idempotent —
    /// chạy lại cũng chỉ cập nhật UsageCount khi dữ liệu mới nhiều hơn.
    /// </summary>
    [HttpPost("seed")]
    public ActionResult<SummaryCatalogSeedResponse> Seed(
        [FromBody] SummaryCatalogSeedRequest? request)
    {
        var minCount = request?.MinCount ?? 50;
        if (minCount < 2)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ngưỡng không hợp lệ",
                Detail = "Ngưỡng minCount tối thiểu là 2 để tránh nạp nhiễu.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var (added, updated) = CatalogSeeder.SeedFromArchive(
            HttpContext.RequestServices.GetRequiredService<IDbContextFactory<StpDbContext>>(),
            minCount);
        return Ok(new SummaryCatalogSeedResponse(added, updated, minCount));
    }

    private static SummaryCatalogItemResponse ToResponse(CatalogEntry entry) =>
        new(
            entry.Id,
            entry.DisplayName,
            entry.UsageCount,
            entry.Source == CatalogEntrySource.User ? "user" : "learned",
            new DateTimeOffset(entry.LastUsedAt, TimeSpan.Zero),
            entry.DossierTypeHint);

    private static string NormalizeDisplay(string value) =>
        Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
}
