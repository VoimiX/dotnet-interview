using System.Collections.Concurrent;
using Async.Web.Services;

namespace Async.Web.Endpoints;

public static class CacheEndpoints
{
    public static void MapCacheEndpoints(this IEndpointRouteBuilder app)
    {
        // Вопрос: 50 запросов одновременно не нашли значение в кэше. Сколько раз выполнится тяжёлый запрос к БД?
        app.MapGet("/cache/stampede", async (AsyncCache cache, int? n) =>
        {
            var count = n ?? 50;

            // Наивный кэш: check-then-act → каждая параллельная задача видит промах.
            var naive = new ConcurrentDictionary<string, string>();
            var naiveCalls = 0;
            await Task.WhenAll(Enumerable.Range(0, count).Select(async _ =>
            {
                if (!naive.ContainsKey("report"))
                    naive["report"] = await LoadReportAsync(() => Interlocked.Increment(ref naiveCalls));
            }));

            // Кэш на Lazy<Task<T>>: все ждут одну и ту же Task.
            var key = $"report-{Guid.NewGuid()}";
            var lazyCalls = 0;
            await Task.WhenAll(Enumerable.Range(0, count).Select(_ =>
                cache.GetOrCreateAsync(key, () => LoadReportAsync(() => Interlocked.Increment(ref lazyCalls)))));
            cache.Remove(key);

            return new
            {
                concurrentRequests = count,
                naiveFactoryCalls = naiveCalls,
                lazyTaskFactoryCalls = lazyCalls,
                explanation = "Кэшируйте Task, а не результат: первый запрос запускает загрузку, остальные await'ят ту же Task. " +
                              "В .NET 9+ это делает HybridCache.GetOrCreateAsync.",
            };
        });
    }

    private static async Task<string> LoadReportAsync(Action onCall)
    {
        onCall();
        await Task.Delay(300); // "тяжёлый запрос к БД"
        return "отчёт";
    }
}
