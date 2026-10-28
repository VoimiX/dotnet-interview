using Async.Web.Endpoints;
using Async.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DemoLog>();
builder.Services.AddScoped<FakeDbContext>();
builder.Services.AddSingleton<BackgroundTaskQueue>();
builder.Services.AddSingleton<AsyncCache>();
builder.Services.AddHostedService<QueuedWorker>();
builder.Services.AddHostedService<HeartbeatService>();

// Typed HttpClient через IHttpClientFactory: пул HttpMessageHandler'ов, без socket exhaustion и проблем с DNS.
builder.Services.AddHttpClient<DownstreamClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["SelfBaseUrl"] ?? "http://localhost:5145"));
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    title = "Async/await в ASP.NET Core — примеры для собеседования",
    endpoints = new[]
    {
        "GET  /context                       — SynchronizationContext в ASP.NET Core и потоки до/после await",
        "GET  /starvation/sync|async          — блокирующий vs асинхронный обработчик",
        "GET  /starvation/load?mode=sync&n=200 — нагрузочный тест: thread pool starvation",
        "GET  /starvation/stats               — состояние ThreadPool",
        "GET  /cancellation/slow?seconds=10   — RequestAborted: отмена при отключении клиента",
        "GET  /parallel/sequential            — последовательные вызовы downstream-сервисов",
        "GET  /parallel/aggregate?timeoutMs=  — параллельные вызовы + общий таймаут",
        "GET  /stream/json?count=10           — IAsyncEnumerable как потоковый JSON",
        "GET  /stream/sse?count=10            — Server-Sent Events (.NET 10)",
        "POST /fire-and-forget/wrong          — Task.Run со scoped DbContext после ответа",
        "POST /fire-and-forget/right          — очередь (Channel) + BackgroundService + новый scope",
        "GET  /cache/stampede?n=50            — защита от cache stampede через Lazy<Task<T>>",
        "POST /sync-io/wrong|right            — синхронное чтение тела запроса запрещено в Kestrel",
        "GET  /log                            — журнал фоновых событий",
    },
}));

app.MapContextEndpoints();
app.MapStarvationEndpoints();
app.MapCancellationEndpoints();
app.MapParallelEndpoints();
app.MapStreamingEndpoints();
app.MapFireAndForgetEndpoints();
app.MapCacheEndpoints();
app.MapSyncIoEndpoints();

app.MapGet("/log", (DemoLog log) => log.Entries);

app.Run();
