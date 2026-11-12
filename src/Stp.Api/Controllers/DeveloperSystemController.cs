using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stp.Api.Mapping;
using Stp.Contracts;
using Stp.Core.Excel;
using Stp.Core.Macros;
using Stp.Core.Storage;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dev/system")]
public sealed class DeveloperSystemController : ControllerBase
{
    private readonly StpDbContext _db;
    private readonly IExcelConversionService _excel;
    private readonly IMacroService _macros;
    private readonly StorageOptions _storage;
    private readonly IFileStorage _fileStorage;

    public DeveloperSystemController(
        StpDbContext db,
        IExcelConversionService excel,
        IMacroService macros,
        IOptions<StorageOptions> storage,
        IFileStorage fileStorage)
    {
        _db = db;
        _excel = excel;
        _macros = macros;
        _storage = storage.Value;
        _fileStorage = fileStorage;
    }

    /// <summary>L1: Quét dọn các tệp tạm (uploads, outputs, backups) cũ hơn số ngày chỉ định (mặc định 30 ngày).</summary>
    [HttpPost("sweep")]
    public ActionResult<object> Sweep([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var removed = _fileStorage.SweepOldArtifacts(TimeSpan.FromDays(days));
        return Ok(new { cleaned = removed, cutoffDays = days, serverTime = DateTimeOffset.UtcNow });
    }

    /// <summary>Thông tin môi trường cho trang developer.</summary>
    [HttpGet]
    public async Task<ActionResult<SystemInfoResponse>> Get(CancellationToken cancellationToken)
    {
        var databaseConnected = await _db.Database.CanConnectAsync(cancellationToken);

        var activeMacro = await _macros.GetActiveAsync(cancellationToken);

        var workbookCount = await _db.ArchiveWorkbooks.CountAsync(cancellationToken);
        var dossierCount = await _db.Dossiers.CountAsync(cancellationToken);
        var jobCount = await _db.BatchJobs.CountAsync(cancellationToken);
        var syncCount = await _db.SyncSessions.CountAsync(cancellationToken);

        return Ok(new SystemInfoResponse(
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? "Development",
            DateTimeOffset.UtcNow,
            _storage.RootPath,
            ResolveExcelHost(_excel.ExcelPath),
            _excel.ExcelPath,
            _excel.IsAvailable,
            databaseConnected,
            activeMacro.ToSummary(),
            workbookCount,
            dossierCount,
            jobCount,
            syncCount));
    }

    private static string ResolveExcelHost(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Không tìm thấy Excel/WPS";
        }

        if (path.Contains("Kingsoft", StringComparison.OrdinalIgnoreCase)
            || path.Contains("WPS", StringComparison.OrdinalIgnoreCase))
        {
            return "WPS Office";
        }

        if (path.Contains("Microsoft Office", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("EXCEL.EXE", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft Excel";
        }

        return "Excel không xác định";
    }
}
