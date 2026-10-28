using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCore.Pipeline.Infrastructure;

/// <summary>
/// .NET 8+: IExceptionHandler вместо самописного try/catch-middleware.
/// Вызывается из UseExceptionHandler(); можно зарегистрировать несколько — они пробуются по очереди, пока один не вернёт true.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        context.Trace($"★ GlobalExceptionHandler (IExceptionHandler): {exception.GetType().Name}: {exception.Message}");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Необработанное исключение",
                Detail = exception.Message,
            },
        });
    }
}
