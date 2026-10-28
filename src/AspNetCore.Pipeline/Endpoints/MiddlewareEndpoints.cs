using AspNetCore.Pipeline.Infrastructure;
using AspNetCore.Pipeline.Middleware;

namespace AspNetCore.Pipeline.Endpoints;

public static class MiddlewareEndpoints
{
    public static void MapMiddlewareEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/middleware/order", (HttpContext http) =>
        {
            http.Trace("★ ЭНДПОИНТ /middleware/order");
            return new
            {
                note = "Конвейер — 'луковица': код до await next(context) выполняется на пути запроса, после — на пути ответа, в обратном порядке. " +
                       "Порядок задаётся порядком app.Use...: ExceptionHandler — первым, чтобы ловить всё; UseRouting → UseCors → UseAuthentication → UseAuthorization → эндпоинты.",
            };
        });

        app.MapGet("/branch/usewhen", (HttpContext http) =>
        {
            http.Trace("★ ЭНДПОИНТ /branch/usewhen");
            return new { note = "UseWhen-ветка выполнилась и вернула запрос в основной конвейер: роутинг и остальные middleware отработали." };
        });

        app.MapGet("/secure/data", (HttpContext http) =>
        {
            http.Trace("★ ЭНДПОИНТ /secure/data");
            return new { secret = 42, note = "Без заголовка X-Api-Key: demo-key ApiKeyMiddleware отвечает 401 и не вызывает next." };
        });

        app.MapGet("/middleware/lifetimes", (HttpContext http, RequestScopedService scoped) =>
        {
            http.Trace($"★ ЭНДПОИНТ: scoped-сервис #{scoped.Id}");
            return new
            {
                scopedServiceId = scoped.Id,
                note = "Вызовите дважды: номер ConventionMiddleware не меняется (создан один раз), номер FactoryMiddleware и scoped-сервиса — новый на каждый запрос. " +
                       "Scoped-зависимость в конструкторе convention-middleware = captive dependency.",
            };
        });

        // Без обёртки TraceMiddleware: нужен настоящий Response.Body, чтобы ответ действительно "начался".
        app.MapGet("/middleware/response-started", async (HttpContext http, string? mode) =>
        {
            if (mode == "right")
            {
                http.Response.OnStarting(() =>
                {
                    http.Response.Headers["X-Added-In-OnStarting"] = "ok";
                    return Task.CompletedTask;
                });
                await http.Response.WriteAsync("Заголовок X-Added-In-OnStarting добавлен через Response.OnStarting — посмотрите заголовки (curl -i).\n");
                return;
            }

            await http.Response.WriteAsync("Часть тела отправлена клиенту.\n");
            await http.Response.WriteAsync($"Response.HasStarted = {http.Response.HasStarted}\n");
            try
            {
                http.Response.Headers["X-Too-Late"] = "1";
            }
            catch (InvalidOperationException ex)
            {
                await http.Response.WriteAsync($"Попытка поменять заголовок: {ex.GetType().Name}: {ex.Message}\n");
                await http.Response.WriteAsync("Статус-код тоже уже не поменять. Поэтому middleware обработки ошибок проверяет Response.HasStarted, " +
                                               "а заголовки 'на выходе' ставят через Response.OnStarting. Попробуйте ?mode=right\n");
            }
        });

        app.MapPost("/middleware/echo", async (HttpContext http) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            var readByEndpoint = await reader.ReadToEndAsync(http.RequestAborted);
            http.Trace($"★ ЭНДПОИНТ: прочитал тело ещё раз, длина = {readByEndpoint.Length}");
            return new
            {
                readByMiddleware = http.Items[BodyLoggingMiddleware.LoggedBodyKey],
                readByEndpoint,
                note = "С EnableBuffering тело можно перечитать после перемотки Position = 0. С ?buffering=false эндпоинт получает пустую строку: " +
                       "поток уже вычитан из сети.",
            };
        });

        app.MapGet("/middleware/throw", (HttpContext http) =>
        {
            http.Trace("★ ЭНДПОИНТ: бросаю исключение");
            throw new InvalidOperationException("Что-то сломалось в эндпоинте");
        });

        app.MapGet("/middleware/rate-limited", (HttpContext http) =>
        {
            http.Trace("★ ЭНДПОИНТ /middleware/rate-limited");
            return new { note = "Третий запрос за 10 секунд получит 429 от UseRateLimiter — эндпоинт даже не вызывается." };
        }).RequireRateLimiting("fixed");
    }
}
