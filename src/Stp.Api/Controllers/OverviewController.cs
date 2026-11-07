using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Mapping;
using Stp.Contracts;
using Stp.Core.Excel;
using Stp.Infrastructure.Persistence;

namespace Stp.Api.Controllers;

[ApiController]
[Route("api/overview")]
public sealed class OverviewController : ControllerBase
{
    private readonly StpDbContext _db;
    private readonly IExcelConversionService _excel;

    public OverviewController(StpDbContext db, IExcelConversionService excel)
    {
        _db = db;
        _excel = excel;
    }

    /// <summary>Số liệu tổng quan cho trang chủ của người dùng nghiệp vụ.</summary>
    [HttpGet]
    public async Task<ActionResult<OverviewResponse>> Get(CancellationToken cancellationToken)
    {
        var databaseConnected = await _db.Database.CanConnectAsync(cancellationToken);

        var workbookCount = await _db.ArchiveWorkbooks.CountAsync(cancellationToken);
        var dossierCount = await _db.Dossiers.CountAsync(cancellationToken);
        var yearCount = await _db.ArchiveYears.CountAsync(cancellationToken);
        var typeCount = await _db.Dossiers
            .Where(dossier => dossier.Type != string.Empty)
            .Select(dossier => dossier.Type)
            .Distinct()
            .CountAsync(cancellationToken);

        var jobCount = await _db.BatchJobs.CountAsync(cancellationToken);
        var runningJobCount = await _db.BatchJobs.CountAsync(
            job => job.Status == Stp.Core.Domain.BatchJobStatus.Queued
                   || job.Status == Stp.Core.Domain.BatchJobStatus.Running,
            cancellationToken);
        var syncSessionCount = await _db.SyncSessions.CountAsync(cancellationToken);
        var lastSyncAt = await _db.SyncSessions
            .OrderByDescending(session => session.UploadedAt)
            .Select(session => (DateTimeOffset?)session.UploadedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var recentJobs = await _db.BatchJobs
            .AsNoTracking()
            .OrderByDescending(job => job.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        var recentWorkbooks = await _db.ArchiveWorkbooks
            .AsNoTracking()
            .OrderByDescending(workbook => workbook.LastSyncedAt ?? workbook.ImportedAt)
            .Take(4)
            .Select(workbook => new WorkbookResponse(
                workbook.Id,
                workbook.Name,
                workbook.SourceFileName,
                workbook.Kind.ToString(),
                workbook.ImportedAt,
                workbook.LastSyncedAt,
                workbook.Version,
                workbook.IsMaster,
                workbook.Notes,
                workbook.Years.Count,
                workbook.Years.Sum(year => year.Dossiers.Count)))
            .ToListAsync(cancellationToken);

        return Ok(new OverviewResponse(
            workbookCount,
            dossierCount,
            yearCount,
            typeCount,
            jobCount,
            runningJobCount,
            syncSessionCount,
            lastSyncAt,
            ResolveExcelHost(_excel.ExcelPath),
            _excel.ExcelPath,
            _excel.IsAvailable,
            databaseConnected,
            DateTimeOffset.UtcNow,
            recentJobs.Select(ApiMapping.ToSummary).ToList(),
            recentWorkbooks));
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
