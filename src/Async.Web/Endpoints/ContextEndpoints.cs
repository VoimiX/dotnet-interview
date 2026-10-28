namespace Async.Web.Endpoints;

public static class ContextEndpoints
{
    public static void MapContextEndpoints(this IEndpointRouteBuilder app)
    {
        // Вопрос: нужен ли ConfigureAwait(false) в ASP.NET Core? Может ли быть deadlock на .Result?
        app.MapGet("/context", async () =>
        {
            var context = SynchronizationContext.Current?.GetType().Name ?? "null";
            var threadBefore = Environment.CurrentManagedThreadId;

            await Task.Delay(50);

            return new
            {
                synchronizationContext = context,
                threadBefore,
                threadAfter = Environment.CurrentManagedThreadId,
                explanation =
                    "В ASP.NET Core нет SynchronizationContext: продолжение после await выполняется на любом потоке пула. " +
                    "Классического deadlock на .Result нет, но блокировка потока ведёт к thread pool starvation. " +
                    "ConfigureAwait(false) в коде приложения ничего не меняет. " +
                    "HttpContext не потокобезопасен — не трогайте его из параллельных задач.",
            };
        });

        // "Внешний сервис" для демо параллельных вызовов.
        app.MapGet("/fake/{service}", async (string service, int? delayMs, CancellationToken ct) =>
        {
            await Task.Delay(delayMs ?? 200, ct);
            return $"{service}: ok за {delayMs ?? 200} мс";
        });
    }
}
