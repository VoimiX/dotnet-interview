using System.Diagnostics;

namespace Async.Demos;

public sealed class ThreadPoolStarvationDemo : IDemo
{
    public string Title => "Thread pool starvation: почему sync-over-async убивает пропускную способность";

    public string Question =>
        "Сервис под нагрузкой отвечает секундами, CPU низкий. Что такое thread pool starvation и как его диагностировать?";

    private const int DelayMs = 500;

    public async Task RunAsync()
    {
        ThreadPool.GetMinThreads(out var minWorkers, out _);
        var count = Math.Min(Environment.ProcessorCount * 8, 128);
        Log.Info($"ProcessorCount = {Environment.ProcessorCount}, MinThreads = {minWorkers}, задач = {count}, каждая ждёт {DelayMs} мс");

        Log.Section("1. Асинхронное ожидание");
        await MeasureAsync(count, blocking: false);

        Log.Section("2. Блокирующее ожидание (Task.Delay(...).Wait() — как .Result в обработчике запроса)");
        await MeasureAsync(count, blocking: true);

        Log.Note("Каждая блокирующая задача держит поток пула. Когда занятых потоков больше MinThreads, " +
                 "пул добавляет новые медленно (hill climbing, ~1-2 в секунду; с .NET 6 быстрее для Task.Wait), " +
                 "и даже короткие задачи стоят в очереди. Симптомы: растут ThreadPool.ThreadCount и QueueLength, CPU низкий. " +
                 "Диагностика: dotnet-counters (threadpool-queue-length, threadpool-thread-count), dotnet-dump + dumpasync. " +
                 "Лечение: async all the way. SetMinThreads — лишь временный костыль.");
    }

    private static async Task MeasureAsync(int count, bool blocking)
    {
        var threadsBefore = ThreadPool.ThreadCount;
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, count)
            .Select(_ => blocking
                ? Task.Run(() => Task.Delay(DelayMs).Wait())
                : Task.Run(async () => await Task.Delay(DelayMs)))
            .ToArray();

        await Task.WhenAll(tasks);
        Log.Info($"общее время: {sw.ElapsedMilliseconds} мс (идеал ≈ {DelayMs}), " +
                 $"потоков в пуле: {threadsBefore} → {ThreadPool.ThreadCount}");
    }
}
