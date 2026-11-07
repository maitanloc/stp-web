using System.Threading.Channels;
using Stp.Core.Jobs;

namespace Stp.Infrastructure.Jobs;

/// <summary>Hàng đợi trong bộ nhớ; worker chạy tuần tự để tránh tranh chấp Excel COM.</summary>
public sealed class BatchJobQueue : IBatchJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
