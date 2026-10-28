namespace Async.Web.Services;

/// <summary>
/// Обрабатывает очередь фоновых задач. Для каждой задачи создаётся СВОЙ DI-scope:
/// BackgroundService — singleton, scoped-сервисы (DbContext) напрямую в него внедрять нельзя.
/// </summary>
public sealed class QueuedWorker(BackgroundTaskQueue queue, IServiceScopeFactory scopeFactory, DemoLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                try
                {
                    await workItem(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Ошибка одной задачи не должна убивать весь сервис:
                    // с .NET 6 необработанное исключение в BackgroundService останавливает хост.
                    log.Write($"[queue] задача упала: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // штатная остановка приложения
        }
    }
}
