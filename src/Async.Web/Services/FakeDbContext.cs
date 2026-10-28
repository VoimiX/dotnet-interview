namespace Async.Web.Services;

/// <summary>Имитация EF Core DbContext: scoped, не потокобезопасен, уничтожается в конце запроса.</summary>
public sealed class FakeDbContext(DemoLog log) : IAsyncDisposable
{
    private readonly string _id = Guid.NewGuid().ToString("N")[..6];
    private bool _disposed;
    private int _activeOperations;

    public async Task SaveChangesAsync(string what, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Как в EF Core: параллельные операции на одном контексте запрещены.
        if (Interlocked.Increment(ref _activeOperations) > 1)
        {
            Interlocked.Decrement(ref _activeOperations);
            throw new InvalidOperationException("A second operation was started on this context instance before a previous operation completed.");
        }

        try
        {
            await Task.Delay(50, ct);
            log.Write($"DbContext {_id}: сохранено '{what}'");
        }
        finally
        {
            Interlocked.Decrement(ref _activeOperations);
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        log.Write($"DbContext {_id}: DisposeAsync (конец scope)");
        return ValueTask.CompletedTask;
    }
}
