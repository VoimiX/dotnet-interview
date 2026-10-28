using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Middleware;

/// <summary>
/// Correlation id: берём из входящего заголовка или генерируем, кладём в ответ и в logging scope —
/// все логи этого запроса (в том числе из сервисов) получат поле CorrelationId.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault() ?? Guid.NewGuid().ToString("N")[..12];

        // Заголовок ставим ДО next: ответ ещё не начался, это безопасно.
        context.Response.Headers[HeaderName] = correlationId;
        context.Trace($"→ CorrelationIdMiddleware: {correlationId}");

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            await next(context);

        context.Trace("← CorrelationIdMiddleware");
    }
}
