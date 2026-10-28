using System.Diagnostics;
using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Middleware;

/// <summary>Классический пример: код до next — на входе запроса, после next — на выходе ответа.</summary>
public sealed class TimingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        context.Trace("→ TimingMiddleware");

        // Заголовок нельзя менять после начала отправки ответа → OnStarting вызывается прямо перед отправкой заголовков.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Elapsed-Ms"] = sw.ElapsedMilliseconds.ToString();
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            // finally: выполнится и при исключении, которое летит наружу.
            context.Trace($"← TimingMiddleware: {sw.Elapsed.TotalMilliseconds:F1} мс");
        }
    }
}
