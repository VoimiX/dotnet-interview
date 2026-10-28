using Async.Web.Services;

namespace Async.Web.Endpoints;

public static class FireAndForgetEndpoints
{
    public static void MapFireAndForgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fire-and-forget");

        // ПЛОХО: фоновая задача переживает запрос, но использует его scoped-зависимости и HttpContext.
        group.MapPost("/wrong", (FakeDbContext db, DemoLog log, HttpContext http) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    log.Write($"[wrong] HttpContext после ответа: {DescribeHttpContext(http)}");
                    await db.SaveChangesAsync("заказ из fire-and-forget");
                }
                catch (Exception ex)
                {
                    log.Write($"[wrong] {ex.GetType().Name}: {ex.Message}");
                }
            });

            return Results.Accepted(value: new
            {
                note = "Ответ отправлен, scope запроса уничтожен (DbContext.DisposeAsync), а Task.Run ещё работает. Через секунду смотрите GET /log.",
            });
        });

        // ХОРОШО: кладём работу в очередь; BackgroundService выполнит её в собственном DI-scope.
        group.MapPost("/right", async (BackgroundTaskQueue queue, CancellationToken ct) =>
        {
            var orderId = Guid.NewGuid().ToString("N")[..8];

            await queue.EnqueueAsync(async (services, token) =>
            {
                var db = services.GetRequiredService<FakeDbContext>(); // новый DbContext из scope воркера
                await Task.Delay(500, token);
                await db.SaveChangesAsync($"заказ {orderId}", token);
            }, ct);

            return Results.Accepted(value: new
            {
                orderId,
                note = "Работа в очереди. Нужны гарантии доставки (переживать рестарт) — outbox/брокер сообщений/Hangfire.",
            });
        });

        // ПЛОХО: параллельные запросы на одном DbContext.
        group.MapGet("/parallel-dbcontext", async (FakeDbContext db) =>
        {
            try
            {
                await Task.WhenAll(db.SaveChangesAsync("A"), db.SaveChangesAsync("B"));
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    detail: $"{ex.Message} DbContext не потокобезопасен: WhenAll на одном контексте нельзя — " +
                            "либо последовательно, либо IDbContextFactory и отдельный контекст на каждую задачу.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }

    private static string DescribeHttpContext(HttpContext http)
    {
        try
        {
            return $"Path = '{http.Request.Path}' (объект может быть уже переиспользован для другого запроса!)";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
