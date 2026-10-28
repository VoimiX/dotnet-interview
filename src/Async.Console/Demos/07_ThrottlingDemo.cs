using System.Diagnostics;

namespace Async.Demos;

public sealed class ThrottlingDemo : IDemo
{
    public string Title => "Ограничение параллелизма: SemaphoreSlim, Parallel.ForEachAsync";

    public string Question =>
        "Нужно обработать 10 000 URL, но не более N одновременно. Как? Почему нельзя Parallel.ForEach с async-лямбдой?";

    public async Task RunAsync()
    {
        Log.Section("1. SemaphoreSlim: не более 3 одновременных операций");
        using var semaphore = new SemaphoreSlim(3);
        int current = 0, max = 0;
        var sw = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(1, 10).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                var now = Interlocked.Increment(ref current);
                InterlockedMax(ref max, now);
                await Task.Delay(100);
            }
            finally
            {
                Interlocked.Decrement(ref current);
                semaphore.Release();
            }
        }));
        Log.Info($"10 операций по 100 мс: {sw.ElapsedMilliseconds} мс, максимум одновременно: {max}");
        Log.Note("Все 10 задач создаются сразу, но 'ждут очереди' асинхронно, не блокируя потоки. " +
                 "Release — строго в finally.");

        Log.Section("2. Parallel.ForEachAsync (.NET 6+)");
        sw.Restart();
        await Parallel.ForEachAsync(Enumerable.Range(1, 10),
            new ParallelOptions { MaxDegreeOfParallelism = 3 },
            async (i, ct) => await Task.Delay(100, ct));
        Log.Info($"10 операций, MaxDegreeOfParallelism = 3: {sw.ElapsedMilliseconds} мс");
        Log.Note("Удобнее семафора для больших коллекций: не создаёт сразу 10 000 задач, поддерживает отмену и остановку при первой ошибке.");

        Log.Section("3. Ошибка: Parallel.ForEach с async-лямбдой");
        var done = 0;
        Parallel.ForEach(Enumerable.Range(1, 5), async i =>
        {
            await Task.Delay(100);
            Interlocked.Increment(ref done);
        });
        Log.Bad($"Parallel.ForEach вернул управление, обработано: {done} из 5");
        await Task.Delay(300);
        Log.Info($"через 300 мс обработано: {done} из 5");
        Log.Note("Parallel.ForEach принимает Action<T> → async-лямбда становится async void: " +
                 "ForEach не ждёт, исключения уронят процесс. Parallel.For/ForEach — только для CPU-bound кода.");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int snapshot;
        do
        {
            snapshot = target;
            if (value <= snapshot)
                return;
        } while (Interlocked.CompareExchange(ref target, value, snapshot) != snapshot);
    }
}
