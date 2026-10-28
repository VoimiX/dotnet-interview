using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Middleware;

/// <summary>
/// Чтение тела запроса в middleware. Request.Body — однонаправленный поток из сети:
/// без EnableBuffering после чтения здесь эндпоинт получит пустое тело.
/// </summary>
public sealed class BodyLoggingMiddleware(RequestDelegate next)
{
    public const string LoggedBodyKey = "LoggedBody";

    public async Task InvokeAsync(HttpContext context)
    {
        var buffering = context.Request.Query["buffering"] != "false";
        if (buffering)
            context.Request.EnableBuffering(); // буфер в памяти, при большом теле — во временном файле

        using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            context.Items[LoggedBodyKey] = await reader.ReadToEndAsync(context.RequestAborted);

        if (buffering)
            context.Request.Body.Position = 0; // перемотка для следующих читателей

        context.Trace($"→ BodyLoggingMiddleware: прочитал тело, EnableBuffering = {buffering}");
        await next(context);
    }
}
