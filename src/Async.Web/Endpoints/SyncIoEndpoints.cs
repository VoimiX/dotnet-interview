namespace Async.Web.Endpoints;

public static class SyncIoEndpoints
{
    public static void MapSyncIoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync-io");

        // Kestrel по умолчанию запрещает синхронный I/O (AllowSynchronousIO = false) —
        // именно чтобы чтение тела запроса не блокировало потоки пула.
        group.MapPost("/wrong", (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body);
                var body = reader.ReadToEnd();
                return Results.Ok(new { length = body.Length });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost("/right", async (HttpRequest request, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            return Results.Ok(new { length = body.Length });
        });
    }
}
