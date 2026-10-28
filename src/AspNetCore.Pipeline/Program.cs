using AspNetCore.Pipeline.Endpoints;
using AspNetCore.Pipeline.Filters;
using AspNetCore.Pipeline.Infrastructure;
using AspNetCore.Pipeline.Middleware;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    // Глобальные фильтры: выполняются для всех контроллеров, самые "внешние" по scope.
    options.Filters.Add(new PipelineTraceAttribute("global"));
    options.Filters.Add<AlwaysRunResultTraceFilter>();
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddScoped<RequestScopedService>();
builder.Services.AddScoped<FactoryMiddleware>();       // IMiddleware: экземпляр из DI на каждый запрос
builder.Services.AddSingleton<CacheResourceFilter>();  // для [ServiceFilter] фильтр обязан быть зарегистрирован
builder.Services.AddScoped<AuditFilter>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", limiter =>
    {
        limiter.PermitLimit = 2;
        limiter.Window = TimeSpan.FromSeconds(10);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();

// ---------- Порядок регистрации = порядок обработки запроса (ответ идёт в обратном порядке) ----------

app.UseMiddleware<TraceMiddleware>();          // самый внешний: собирает трассировку и оборачивает ответ
app.UseExceptionHandler();                     // ловит всё, что выброшено ниже, и вызывает GlobalExceptionHandler
app.UseMiddleware<TimingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

app.Use(async (context, next) =>
{
    context.Trace("→ inline-middleware (app.Use)");
    await next(context);
    context.Trace("← inline-middleware (app.Use)");
});

// MapWhen: отдельная ветка, в основной конвейер НЕ возвращается.
app.MapWhen(context => context.Request.Path.StartsWithSegments("/mapwhen"), branch =>
    branch.Run(async context =>
    {
        context.Trace("★ MapWhen-ветка: app.Run — терминальный middleware, роутинг и эндпоинты не выполняются");
        await context.Response.WriteAsJsonAsync(new
        {
            note = "MapWhen создаёт отдельную ветку конвейера. В отличие от UseWhen, она не возвращается в основной конвейер.",
        });
    }));

// UseWhen: ветка выполняется и возвращается в основной конвейер.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/branch"), branch =>
    branch.Use(async (context, next) =>
    {
        context.Trace("→ UseWhen-ветка (только для /branch)");
        await next(context);
        context.Trace("← UseWhen-ветка");
    }));

app.UseWhen(context => context.Request.Path.StartsWithSegments("/secure"), branch =>
    branch.UseMiddleware<ApiKeyMiddleware>());

app.UseWhen(context => HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/middleware/echo"), branch =>
    branch.UseMiddleware<BodyLoggingMiddleware>());

app.UseMiddleware<EndpointInspectorMiddleware>("до UseRouting");
app.UseRouting();
app.UseMiddleware<EndpointInspectorMiddleware>("после UseRouting");

app.UseRateLimiter();                          // после UseRouting, чтобы видеть политику эндпоинта
app.UseMiddleware<ConventionMiddleware>();
app.UseMiddleware<FactoryMiddleware>();

// ---------- Эндпоинты (выполняются в самом конце конвейера) ----------

app.MapGet("/", () => new
{
    title = "Middleware и фильтры в ASP.NET Core — примеры для собеседования",
    hint = "Каждый ответ обёрнут: trace — порядок прохождения конвейера, response — исходный ответ. ?raw=1 отключает обёртку.",
    middleware = new[]
    {
        "GET  /middleware/order                    — порядок middleware: запрос внутрь, ответ наружу",
        "GET  /branch/usewhen                      — UseWhen: ветка возвращается в основной конвейер",
        "GET  /mapwhen/anything                    — MapWhen: ветка не возвращается",
        "GET  /secure/data  (X-Api-Key: demo-key)  — short-circuit: middleware не вызывает next",
        "GET  /middleware/lifetimes                — convention-middleware (singleton) vs IMiddleware (scoped)",
        "GET  /middleware/response-started?mode=   — wrong|right: заголовки после начала ответа",
        "POST /middleware/echo?buffering=false     — повторное чтение тела запроса и EnableBuffering",
        "GET  /middleware/throw                    — исключение → UseExceptionHandler + IExceptionHandler",
        "GET  /middleware/rate-limited             — встроенный rate limiter (2 запроса за 10 с)",
    },
    mvcFilters = new[]
    {
        "GET  /filters/order                       — порядок типов фильтров и scope (global → controller → action)",
        "GET  /filters/order-override              — IOrderedFilter.Order меняет порядок",
        "GET  /filters/cached/1                    — resource filter: кэш, action не выполняется (вызовите дважды)",
        "POST /filters/validate                    — [ApiController]: автоматический 400",
        "GET  /filters/throw-in-action             — exception filter обрабатывает исключение action",
        "GET  /filters/throw-in-result             — exception filter НЕ ловит исключения result filter",
        "GET  /filters/short-circuit-action        — action filter задаёт Result, action не вызывается",
        "GET  /filters/forbidden                   — authorization filter: короткое замыкание",
        "GET  /filters/di                          — DI в фильтрах: ServiceFilter и TypeFilter",
    },
    endpointFilters = new[]
    {
        "GET  /minimal/order                       — порядок endpoint-фильтров (группа → A → B)",
        "POST /minimal/orders                      — валидирующий endpoint-фильтр",
        "GET  /minimal/wrap                        — фильтр изменяет результат обработчика",
    },
});

app.MapControllers();
app.MapMiddlewareEndpoints();
app.MapMinimalFilterEndpoints();

app.Run();
