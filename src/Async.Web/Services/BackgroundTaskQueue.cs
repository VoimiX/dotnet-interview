using System.Threading.Channels;

namespace Async.Web.Services;

public delegate ValueTask BackgroundWorkItem(IServiceProvider services, CancellationToken ct);

/// <summary>Очередь фоновых задач на Channel с ограниченной ёмкостью (backpressure вместо OutOfMemory).</summary>
public sealed class BackgroundTaskQueue
{
    private readonly Channel<BackgroundWorkItem> _channel = Channel.CreateBounded<BackgroundWorkItem>(
        new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ValueTask EnqueueAsync(BackgroundWorkItem workItem, CancellationToken ct) =>
        _channel.Writer.WriteAsync(workItem, ct);

    public IAsyncEnumerable<BackgroundWorkItem> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
