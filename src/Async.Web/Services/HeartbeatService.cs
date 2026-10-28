namespace Async.Web.Services;

/// <summary>
/// Периодическая фоновая работа: PeriodicTimer + stoppingToken.
/// Тики не накладываются друг на друга (в отличие от System.Threading.Timer).
/// </summary>
public sealed class HeartbeatService(DemoLog log) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // До .NET 10 синхронный код до первого await здесь блокировал запуск хоста
        // (поэтому писали await Task.Yield() первой строкой). В .NET 10 ExecuteAsync целиком запускается в фоне.
        log.Write("[heartbeat] запущен");

        using var timer = new PeriodicTimer(Period);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                log.Write($"[heartbeat] потоков в пуле: {ThreadPool.ThreadCount}, очередь: {ThreadPool.PendingWorkItemCount}");
        }
        catch (OperationCanceledException)
        {
            // остановка приложения
        }
    }
}
