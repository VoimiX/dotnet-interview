using System.Runtime.CompilerServices;

namespace Async.Web.Endpoints;

public static class StreamingEndpoints
{
    public static void MapStreamingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stream");

        // IAsyncEnumerable сериализуется System.Text.Json потоково: элементы уходят клиенту по мере генерации,
        // весь результат не буферизуется в памяти. Отключение клиента отменяет итератор через ct.
        group.MapGet("/json", (int? count, CancellationToken ct) => GenerateAsync(count ?? 10, ct));

        // .NET 10: Server-Sent Events из коробки.
        group.MapGet("/sse", (int? count, CancellationToken ct) =>
            TypedResults.ServerSentEvents(GenerateAsync(count ?? 10, ct), eventType: "tick"));
    }

    private static async IAsyncEnumerable<StreamItem> GenerateAsync(int count, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Delay(300, ct);
            yield return new StreamItem(i, DateTime.UtcNow, Environment.CurrentManagedThreadId);
        }
    }

    private sealed record StreamItem(int Index, DateTime At, int ThreadId);
}
