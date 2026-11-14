using Microsoft.AspNetCore.SignalR;
using Stp.Api.Hubs;
using Stp.Contracts;
using Stp.Core.AutoTick;

namespace Stp.Api.Services;

public sealed class AutoTickEventPublisher : IAutoTickEventPublisher
{
    private readonly IHubContext<TeamHub> _hub;
    private readonly ILogger<AutoTickEventPublisher> _logger;

    public AutoTickEventPublisher(IHubContext<TeamHub> hub, ILogger<AutoTickEventPublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishProgressAsync(AutoTickProgressEvent progress, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.All.SendAsync("autoTickProgress", progress, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không broadcast được autoTickProgress cho job {JobId}", progress.JobId);
        }
    }

    public async Task PublishWatcherAlertAsync(AutoTickWatcherCheckResponse alert, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.All.SendAsync("autoTickWatcherAlert", alert, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không broadcast được autoTickWatcherAlert");
        }
    }
}

