using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Middleware;

/// <summary>
/// Convention-based middleware: создаётся ОДИН раз при построении конвейера (фактически singleton).
/// Scoped-зависимости — только параметрами InvokeAsync. В конструкторе scoped-сервис стал бы captive dependency
/// (а в Development проверка scope упадёт при старте).
/// </summary>
public sealed class ConventionMiddleware(RequestDelegate next)
{
    private static int _instances;
    private readonly int _id = Interlocked.Increment(ref _instances);

    public async Task InvokeAsync(HttpContext context, RequestScopedService scoped)
    {
        context.Trace($"→ ConventionMiddleware: экземпляр #{_id} (один на приложение), scoped-сервис #{scoped.Id} (параметр InvokeAsync)");
        await next(context);
        context.Trace("← ConventionMiddleware");
    }
}

/// <summary>
/// Factory-based middleware (IMiddleware): экземпляр берётся из DI на каждый запрос,
/// поэтому scoped-зависимости можно внедрять в конструктор. Требует регистрации в контейнере.
/// </summary>
public sealed class FactoryMiddleware(RequestScopedService scoped) : IMiddleware
{
    private static int _instances;
    private readonly int _id = Interlocked.Increment(ref _instances);

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Trace($"→ FactoryMiddleware (IMiddleware): экземпляр #{_id} (новый на запрос), scoped-сервис #{scoped.Id} (конструктор)");
        await next(context);
        context.Trace("← FactoryMiddleware");
    }
}
