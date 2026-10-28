using System.Diagnostics;

namespace Async.Demos;

public sealed class TaskCompletionSourceDemo : IDemo
{
    public string Title => "TaskCompletionSource: обёртка над событиями/колбэками";

    public string Question =>
        "Как превратить event-based/callback API в Task? Зачем TaskCreationOptions.RunContinuationsAsynchronously?";

    public async Task RunAsync()
    {
        Log.Section("1. Event-based API → Task");
        var downloader = new LegacyDownloader();
        Log.Good($"результат: {await downloader.DownloadAsync("https://example.com/ok")}");
        try
        {
            await downloader.DownloadAsync("https://example.com/bad");
        }
        catch (IOException ex)
        {
            Log.Good($"ошибка пришла через Task: {ex.Message}");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        try
        {
            await downloader.DownloadAsync("https://example.com/slow", cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Good("отмена через CancellationToken.Register");
        }
        Log.Note("Используйте TrySet* (а не Set*) — при гонке успеха/ошибки/отмены Set* бросит InvalidOperationException. " +
                 "Не забывайте отписываться от событий.");

        Log.Section("2. Без RunContinuationsAsynchronously продолжение выполняется ВНУТРИ SetResult()");
        await MeasureSetResultAsync(TaskCreationOptions.None);
        await MeasureSetResultAsync(TaskCreationOptions.RunContinuationsAsynchronously);
        Log.Note("По умолчанию продолжения await выполняются синхронно в потоке, вызвавшем SetResult. " +
                 "Если там держится lock или это поток I/O-библиотеки — получим зависания и deadlock'и. " +
                 "Почти всегда создавайте TCS с RunContinuationsAsynchronously.");
    }

    private static async Task MeasureSetResultAsync(TaskCreationOptions options)
    {
        var tcs = new TaskCompletionSource(options);
        var waiter = Task.Run(async () =>
        {
            await tcs.Task;
            Log.Info("    продолжение: тяжёлая работа 300 мс");
            Thread.Sleep(300);
        });

        await Task.Delay(100); // ждём, пока waiter подпишется на tcs.Task
        var sw = Stopwatch.StartNew();
        tcs.SetResult();
        Log.Info($"  [{options}] SetResult() вернул управление через {sw.ElapsedMilliseconds} мс");
        await waiter;
    }
}

/// <summary>Старый API на событиях (EAP), как WebClient.DownloadStringAsync.</summary>
public sealed class LegacyDownloader
{
    public event EventHandler<string>? Completed;
    public event EventHandler<Exception>? Failed;

    public void StartDownload(string url) =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(url.EndsWith("slow") ? 1000 : 100);
            if (url.EndsWith("bad"))
                Failed?.Invoke(this, new IOException($"404 для {url}"));
            else
                Completed?.Invoke(this, $"контент {url}");
        });
}

public static class LegacyDownloaderExtensions
{
    public static async Task<string> DownloadAsync(this LegacyDownloader downloader, string url, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<string> onCompleted = (_, result) => tcs.TrySetResult(result);
        EventHandler<Exception> onFailed = (_, error) => tcs.TrySetException(error);

        downloader.Completed += onCompleted;
        downloader.Failed += onFailed;
        try
        {
            await using (ct.Register(() => tcs.TrySetCanceled(ct)))
            {
                downloader.StartDownload(url);
                return await tcs.Task;
            }
        }
        finally
        {
            downloader.Completed -= onCompleted;
            downloader.Failed -= onFailed;
        }
    }
}
