using Stp.Contracts;

namespace Stp.Core.AutoTick;

public interface IAutoTickEventPublisher
{
    Task PublishProgressAsync(AutoTickProgressEvent progress, CancellationToken ct = default);

    Task PublishWatcherAlertAsync(AutoTickWatcherCheckResponse alert, CancellationToken ct = default);
}

