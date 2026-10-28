using Async.Web.Services;

namespace Async.Web.Endpoints;

public static class CancellationEndpoints
{
    public static void MapCancellationEndpoints(this IEndpointRouteBuilder app)
    {
        // CancellationToken в параметрах minimal API = HttpContext.RequestAborted.
        // Проверка: curl --max-time 2 http://localhost:5145/cancellation/slow, затем GET /log.
        app.MapGet("/cancellation/slow", async (int? seconds, DemoLog log, CancellationToken ct) =>
        {
            var total = seconds ?? 10;
            try
            {
                for (var i = 1; i <= total; i++)
                {
                    await Task.Delay(1000, ct);
                    log.Write($"[slow] шаг {i}/{total}");
                }

                return Results.Ok($"Готово за {total} с");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                log.Write("[slow] клиент отключился — прекращаем работу, не тратим ресурсы впустую");
                return Results.Empty; // ответ всё равно никто не получит
            }
        });
    }
}
