namespace AspNetCore.Pipeline.Infrastructure;

/// <summary>Scoped-сервис с номером экземпляра: видно, где он один на запрос, а где "захвачен" навсегда.</summary>
public sealed class RequestScopedService
{
    private static int _instances;

    public int Id { get; } = Interlocked.Increment(ref _instances);
}
