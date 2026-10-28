using System.Text;
using System.Text.Json;
using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Middleware;

/// <summary>
/// Самый внешний middleware: подменяет Response.Body на буфер, пропускает запрос через конвейер,
/// а затем отдаёт клиенту { trace, response } — порядок, в котором отработали middleware и фильтры.
/// Тот же приём используют для логирования тел ответов (с осторожностью: буферизация = память).
/// </summary>
public sealed class TraceMiddleware(RequestDelegate next)
{
    // Демо, которым нужен "настоящий" Response.Body (с буфером ответ никогда не "начинается").
    private static readonly string[] RawPaths = ["/middleware/response-started"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Query.ContainsKey("raw") || RawPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }

        var trace = context.StartTrace();
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        context.Trace("→ TraceMiddleware (самый внешний)");
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
        context.Trace("← TraceMiddleware");

        var envelope = new
        {
            request = $"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}",
            statusCode = context.Response.StatusCode,
            headers = context.Response.Headers
                .Where(h => h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.ToString()),
            trace,
            response = ParseBody(buffer.ToArray()),
        };

        context.Response.ContentLength = null;
        await context.Response.WriteAsJsonAsync(envelope, JsonOptions);
    }

    private static object? ParseBody(byte[] bytes)
    {
        if (bytes.Length == 0)
            return null;

        var text = Encoding.UTF8.GetString(bytes);
        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
