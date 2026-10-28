using System.ComponentModel.DataAnnotations;
using AspNetCore.Pipeline.Infrastructure;

namespace AspNetCore.Pipeline.Filters;

/// <summary>Endpoint-фильтр валидации: при ошибке возвращает 400 и не вызывает обработчик (short-circuit).</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var model = context.Arguments.OfType<T>().FirstOrDefault();
        if (model is null)
            return Results.BadRequest(new { error = $"Нет аргумента типа {typeof(T).Name}" });

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true))
        {
            context.HttpContext.Trace("✘ ValidationFilter: модель невалидна — short-circuit, обработчик не вызывается");
            return Results.ValidationProblem(errors
                .GroupBy(e => e.MemberNames.FirstOrDefault() ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? string.Empty).ToArray()));
        }

        context.HttpContext.Trace("→ ValidationFilter: модель валидна");
        return await next(context);
    }
}
