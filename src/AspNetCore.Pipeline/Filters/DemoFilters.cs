using System.Collections.Concurrent;
using AspNetCore.Pipeline.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AspNetCore.Pipeline.Filters;

/// <summary>
/// IAlwaysRunResultFilter выполняется даже тогда, когда authorization- или resource-фильтр замкнул конвейер
/// (обычные result-фильтры в этом случае пропускаются). Удобно для заголовков, которые нужны в любом ответе.
/// </summary>
public sealed class AlwaysRunResultTraceFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context) =>
        context.HttpContext.Trace($"→ [global IAlwaysRunResultFilter] Result ({context.Result.GetType().Name})");

    public void OnResultExecuted(ResultExecutedContext context) =>
        context.HttpContext.Trace("← [global IAlwaysRunResultFilter] Result");
}

/// <summary>
/// Resource-фильтр — кэш: при попадании конвейер замыкается до model binding, action-фильтров и action.
/// Подключается через [ServiceFilter], поэтому зарегистрирован в DI (singleton — кэш общий).
/// В реальном проекте — встроенный Output Caching (.NET 7+, [OutputCache]).
/// </summary>
public sealed class CacheResourceFilter : IAsyncResourceFilter
{
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var key = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (_cache.TryGetValue(key, out var cached))
        {
            context.HttpContext.Trace($"✔ CacheResourceFilter: HIT {key} — short-circuit: model binding, action-фильтры и action не выполняются");
            context.Result = new OkObjectResult(cached);
            return;
        }

        context.HttpContext.Trace($"→ CacheResourceFilter: MISS {key}");
        var executed = await next();
        if (executed.Result is ObjectResult { Value: { } value })
            _cache[key] = value;
        context.HttpContext.Trace("← CacheResourceFilter: результат сохранён в кэш");
    }
}

/// <summary>
/// Фильтр с зависимостями из DI. Обычный атрибут не может получать сервисы в конструктор —
/// поэтому [ServiceFilter(typeof(AuditFilter))]: экземпляр берётся из контейнера (должен быть зарегистрирован).
/// </summary>
public sealed class AuditFilter(RequestScopedService scoped, ILogger<AuditFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.Trace($"→ AuditFilter ([ServiceFilter], зарегистрирован как scoped): scoped-сервис #{scoped.Id}");
        logger.LogInformation("Audit: {Action}", context.ActionDescriptor.DisplayName);
        await next();
        context.HttpContext.Trace("← AuditFilter");
    }
}

/// <summary>
/// [TypeFilter(typeof(TaggedFilter), Arguments = ...)]: создаётся через ActivatorUtilities — регистрировать не нужно,
/// а часть аргументов конструктора можно передать прямо из атрибута, остальные придут из DI.
/// </summary>
public sealed class TaggedFilter(string tag, RequestScopedService scoped) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) =>
        context.HttpContext.Trace($"→ TaggedFilter ([TypeFilter]): tag = '{tag}' из атрибута, scoped-сервис #{scoped.Id} из DI");

    public void OnActionExecuted(ActionExecutedContext context) =>
        context.HttpContext.Trace("← TaggedFilter");
}

/// <summary>Exception-фильтр: превращает исключение action в 400 и помечает его обработанным.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HandleExceptionAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.HttpContext.Trace($"✔ HandleException (exception filter): обработал {context.Exception.GetType().Name}");
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Обработано exception-фильтром",
            Detail = context.Exception.Message,
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
        context.ExceptionHandled = true;
    }
}

/// <summary>Result-фильтр, который бросает исключение: exception-фильтры его уже не видят.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ThrowInResultFilterAttribute : Attribute, IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Trace("✘ ThrowInResultFilter: бросаю исключение в result-фильтре");
        throw new InvalidOperationException("Исключение в result-фильтре");
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}

/// <summary>Action-фильтр, который задаёт Result до вызова action — action не выполняется.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ShortCircuitActionAttribute : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        context.HttpContext.Trace("✘ ShortCircuitAction: задаю context.Result — action и следующие action-фильтры не вызываются");
        context.Result = new OkObjectResult(new
        {
            from = "ShortCircuitAction filter",
            note = "Action не вызывался. Result-фильтры при этом выполняются.",
        });
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}

/// <summary>Authorization-фильтр, который всегда запрещает доступ.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DenyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        context.HttpContext.Trace("✘ Deny (authorization filter): 403 — дальше ни resource, ни action, ни обычные result-фильтры");
        context.Result = new ObjectResult(new { error = "Доступ запрещён authorization-фильтром" })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
