using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Middleware;

/// <summary>
/// Показывает, что эндпоинт выбирается в UseRouting, а выполняется в конце конвейера.
/// Между ними middleware (авторизация, CORS, rate limiting) могут читать метаданные эндпоинта.
/// </summary>
public sealed class EndpointInspectorMiddleware(RequestDelegate next, string stage)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        context.Trace($"→ EndpointInspector ({stage}): endpoint = {endpoint?.DisplayName ?? "null — ещё не выбран"}");
        await next(context);
    }
}
