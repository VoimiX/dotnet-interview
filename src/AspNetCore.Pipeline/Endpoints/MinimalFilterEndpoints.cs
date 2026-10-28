using AspNetCore.Pipeline.Filters;
using AspNetCore.Pipeline.Infrastructure;
using AspNetCore.Pipeline.Models;

namespace AspNetCore.Pipeline.Endpoints;

/// <summary>
/// Endpoint-фильтры minimal API (.NET 7+): аналог action-фильтров, но одного типа — обёртка вокруг обработчика
/// с доступом к уже привязанным аргументам (context.Arguments) и результату.
/// </summary>
public static class MinimalFilterEndpoints
{
    public static void MapMinimalFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/minimal").AddEndpointFilter(TraceFilter("фильтр группы /minimal"));

        group.MapGet("/order", (HttpContext http) =>
            {
                http.Trace("★ ОБРАБОТЧИК /minimal/order");
                return new
                {
                    note = "Фильтры группы — внешние, затем фильтры эндпоинта в порядке AddEndpointFilter: первый добавленный — самый внешний.",
                };
            })
            .AddEndpointFilter(TraceFilter("endpoint-фильтр A"))
            .AddEndpointFilter(TraceFilter("endpoint-фильтр B"));

        group.MapPost("/orders", (CreateOrderRequest request, HttpContext http) =>
            {
                http.Trace("★ ОБРАБОТЧИК /minimal/orders: модель валидна");
                return Results.Created("/minimal/orders/1", request);
            })
            .AddEndpointFilter<ValidationFilter<CreateOrderRequest>>();

        group.MapGet("/wrap", (HttpContext http) =>
            {
                http.Trace("★ ОБРАБОТЧИК /minimal/wrap");
                return new { value = 42 };
            })
            .AddEndpointFilter(async (context, next) =>
            {
                var result = await next(context);
                context.HttpContext.Trace("← wrap-фильтр: оборачиваю результат обработчика");
                return new
                {
                    data = result,
                    wrappedBy = "endpoint-фильтр",
                    note = "Фильтр получает результат обработчика как object и может вернуть другой.",
                };
            });
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> TraceFilter(string name) =>
        async (context, next) =>
        {
            context.HttpContext.Trace($"→ {name}");
            var result = await next(context);
            context.HttpContext.Trace($"← {name}");
            return result;
        };
}
