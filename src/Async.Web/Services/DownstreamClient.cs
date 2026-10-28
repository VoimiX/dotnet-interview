namespace Async.Web.Services;

/// <summary>Typed HttpClient к "внешним" сервисам (имитируются эндпоинтом /fake/{service} этого же приложения).</summary>
public sealed class DownstreamClient(HttpClient http)
{
    public Task<string> GetAsync(string service, int delayMs, CancellationToken ct) =>
        http.GetStringAsync($"/fake/{service}?delayMs={delayMs}", ct);
}
