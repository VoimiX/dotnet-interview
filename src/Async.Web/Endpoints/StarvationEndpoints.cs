using System.Diagnostics;

namespace Async.Web.Endpoints;

public static class StarvationEndpoints
{
    private const int DefaultDelayMs = 500;

    public static void MapStarvationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/starvation");

        // ПЛОХО: sync-over-async — поток пула заблокирован на всё время ожидания.
        group.MapGet("/sync", (int? delayMs) =>
        {
            Task.Delay(delayMs ?? DefaultDelayMs).Wait();
            return Stats("sync");
        });

        // ХОРОШО: во время ожидания поток возвращается в пул и обслуживает другие запросы.
        group.MapGet("/async", async (int? delayMs, CancellationToken ct) =>
        {
            await Task.Delay(delayMs ?? DefaultDelayMs, ct);
            return Stats("async");
        });

        group.MapGet("/stats", () => Stats("stats"));

        // Нагрузочный тест: n одновременных запросов к /starvation/{mode}.
        // Запускайте async первым или перезапускайте приложение между прогонами: после sync пул уже "разогнан".
        group.MapGet("/load", async (string? mode, int? n, HttpContext http, IHttpClientFactory factory) =>
        {
            var path = mode == "sync" ? "sync" : "async";
            var count = n ?? 200;

            var client = factory.CreateClient();
            client.BaseAddress = new Uri($"{http.Request.Scheme}://{http.Request.Host}");
            client.Timeout = TimeSpan.FromMinutes(5);

            var threadsBefore = ThreadPool.ThreadCount;
            var sw = Stopwatch.StartNew();
            var responses = await Task.WhenAll(Enumerable.Range(0, count)
                .Select(_ => client.GetAsync($"/starvation/{path}?delayMs={DefaultDelayMs}")));
            var elapsed = sw.ElapsedMilliseconds;

            foreach (var response in responses)
                response.Dispose();

            return Results.Ok(new
            {
                mode = path,
                requests = count,
                totalMs = elapsed,
                idealMs = DefaultDelayMs,
                threadPoolThreads = $"{threadsBefore} → {ThreadPool.ThreadCount}",
                explanation = path == "sync"
                    ? "Каждый запрос держит поток. Пул добавляет потоки постепенно → запросы ждут в очереди, время растёт в разы при низком CPU."
                    : "Ожидание не занимает потоки: все запросы завершаются примерно за одно время задержки.",
            });
        });
    }

    private static object Stats(string label) => new
    {
        label,
        threadId = Environment.CurrentManagedThreadId,
        threadPoolThreads = ThreadPool.ThreadCount,
        pendingWorkItems = ThreadPool.PendingWorkItemCount,
    };
}
