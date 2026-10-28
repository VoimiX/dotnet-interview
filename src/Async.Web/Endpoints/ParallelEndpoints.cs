using System.Diagnostics;
using Async.Web.Services;

namespace Async.Web.Endpoints;

public static class ParallelEndpoints
{
    public static void MapParallelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/parallel");

        group.MapGet("/sequential", async (DownstreamClient client, CancellationToken ct) =>
        {
            var sw = Stopwatch.StartNew();
            var users = await client.GetAsync("users", 300, ct);
            var orders = await client.GetAsync("orders", 500, ct);
            var recommendations = await client.GetAsync("recommendations", 700, ct);

            return new { users, orders, recommendations, elapsedMs = sw.ElapsedMilliseconds, note = "Последовательно: ≈ 300 + 500 + 700 мс" };
        });

        // Агрегация данных из нескольких сервисов: параллельно + общий таймаут + отмена при отключении клиента.
        group.MapGet("/aggregate", async (DownstreamClient client, int? timeoutMs, CancellationToken requestAborted) =>
        {
            var timeout = timeoutMs ?? 1000;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            cts.CancelAfter(timeout);

            var sw = Stopwatch.StartNew();
            var users = client.GetAsync("users", 300, cts.Token);
            var orders = client.GetAsync("orders", 500, cts.Token);
            var recommendations = client.GetAsync("recommendations", 700, cts.Token);

            try
            {
                await Task.WhenAll(users, orders, recommendations);
                return Results.Ok(new
                {
                    users = await users,
                    orders = await orders,
                    recommendations = await recommendations,
                    elapsedMs = sw.ElapsedMilliseconds,
                    note = "Параллельно: ≈ max(300, 500, 700) мс. Попробуйте ?timeoutMs=600",
                });
            }
            catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
            {
                // Остальные запросы отменены тем же токеном — не "висят" в фоне.
                return Results.Problem(
                    detail: $"Downstream-сервисы не уложились в {timeout} мс. " +
                            $"Статусы: users={users.Status}, orders={orders.Status}, recommendations={recommendations.Status}",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        });
    }
}
