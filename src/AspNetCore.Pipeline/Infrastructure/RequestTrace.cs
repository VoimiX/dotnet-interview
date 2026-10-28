namespace AspNetCore.Pipeline.Infrastructure;

/// <summary>
/// Трассировка прохождения запроса через middleware и фильтры.
/// Хранится в HttpContext.Items, поэтому доступна везде, где есть HttpContext, без DI.
/// </summary>
public static class RequestTrace
{
    private const string Key = "__request_trace";

    public static List<string> StartTrace(this HttpContext context)
    {
        var trace = new List<string>();
        context.Items[Key] = trace;
        return trace;
    }

    public static void Trace(this HttpContext context, string message)
    {
        if (context.Items[Key] is List<string> trace)
            lock (trace)
                trace.Add(message);
    }
}
