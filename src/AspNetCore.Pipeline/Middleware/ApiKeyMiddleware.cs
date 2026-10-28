using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Middleware;

/// <summary>Короткое замыкание (short-circuit): без верного ключа next не вызывается, дальше конвейер не идёт.</summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    private const string ExpectedKey = "demo-key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers["X-Api-Key"] != ExpectedKey)
        {
            context.Trace("✘ ApiKeyMiddleware: ключа нет — short-circuit, next НЕ вызывается");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Нужен заголовок X-Api-Key: demo-key" });
            return;
        }

        context.Trace("→ ApiKeyMiddleware: ключ верный");
        await next(context);
        context.Trace("← ApiKeyMiddleware");
    }
}
