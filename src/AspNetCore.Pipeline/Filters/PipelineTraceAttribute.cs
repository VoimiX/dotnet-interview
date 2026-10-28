using AspNetCore.Pipeline.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AspNetCore.Pipeline.Filters;

/// <summary>
/// Один атрибут реализует все пять типов MVC-фильтров и пишет в трассировку, когда вызывается каждый из них.
/// Повешенный на global, controller и action, он показывает и порядок типов, и порядок scope.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class PipelineTraceAttribute(string scope) : Attribute,
    IAsyncAuthorizationFilter, IAsyncResourceFilter, IAsyncActionFilter, IAsyncExceptionFilter, IAsyncResultFilter, IOrderedFilter
{
    /// <summary>Меньшее значение = раньше на входе и позже на выходе. По умолчанию 0 → порядок по scope.</summary>
    public int Order { get; set; }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        context.HttpContext.Trace($"  [{scope}] Authorization");
        return Task.CompletedTask;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        context.HttpContext.Trace($"→ [{scope}] Resource (model binding ещё не было)");
        await next();
        context.HttpContext.Trace($"← [{scope}] Resource");
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var arguments = string.Join(", ", context.ActionArguments.Select(a => $"{a.Key}={a.Value}"));
        context.HttpContext.Trace($"→ [{scope}] Action (аргументы уже привязаны: {(arguments.Length == 0 ? "нет" : arguments)})");

        var executed = await next();

        var exception = executed.Exception is not null && !executed.ExceptionHandled
            ? $" — наружу летит {executed.Exception.GetType().Name}"
            : string.Empty;
        context.HttpContext.Trace($"← [{scope}] Action{exception}");
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        context.HttpContext.Trace($"  [{scope}] Exception: {context.Exception.Message} (не обрабатываю)");
        return Task.CompletedTask;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        context.HttpContext.Trace($"→ [{scope}] Result ({context.Result.GetType().Name})");
        await next();
        context.HttpContext.Trace($"← [{scope}] Result");
    }
}
