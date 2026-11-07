using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stp.Core.Domain;
using Stp.Core.Jobs;
using Stp.Infrastructure.Persistence;

namespace Stp.Infrastructure.Jobs;

/// <summary>
/// Chạy job tuần tự. Khi khởi động lại, job đang chạy dở được đưa về Queued
/// và xếp lại hàng đợi.
/// </summary>
public sealed class BatchJobWorker : BackgroundService
{
    private readonly IBatchJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BatchJobWorker> _logger;

    public BatchJobWorker(
        IBatchJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BatchJobWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStaleJobsAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IBatchJobProcessor>();
                await processor.ProcessAsync(jobId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} lỗi ngoài dự kiến", jobId);
            }
        }
    }

    private async Task RecoverStaleJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StpDbContext>();

            var staleJobs = await db.BatchJobs
                .Where(job => job.Status == BatchJobStatus.Queued
                              || job.Status == BatchJobStatus.Running)
                .OrderBy(job => job.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (staleJobs.Count == 0)
            {
                return;
            }

            foreach (var job in staleJobs)
            {
                if (job.Status == BatchJobStatus.Running)
                {
                    job.Status = BatchJobStatus.Queued;
                    job.StartedAt = null;
                    db.JobLogs.Add(new JobLog
                    {
                        JobId = job.Id,
                        Level = "Warning",
                        Message = "Worker khởi động lại; job được xếp lại hàng đợi."
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var job in staleJobs)
            {
                await _queue.EnqueueAsync(job.Id, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Xếp lại job {JobId} ({Type})", job.Id, job.Type);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không khôi phục được job dang dở khi khởi động");
        }
    }
}
