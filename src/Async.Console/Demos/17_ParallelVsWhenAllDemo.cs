using System.Collections.Concurrent;
using System.Diagnostics;

namespace Async.Demos;

public sealed class ParallelVsWhenAllDemo : IDemo
{
    public string Title => "Parallel.ForEach vs Task.WhenAll vs Parallel.ForEachAsync";

    public string Question =>
        "Чем Parallel.ForEach отличается от Task.WhenAll? Что выбрать для CPU-bound, а что для I/O-bound? " +
        "Как ограничить параллелизм? Что будет с исключениями?";

    private const int CpuWorkMs = 50;

    public async Task RunAsync()
    {
        var cpuItems = Enumerable.Range(1, Environment.ProcessorCount * 2).ToArray();
        Log.Section($"1. CPU-bound: {cpuItems.Length} элементов по {CpuWorkMs} мс вычислений, ядер: {Environment.ProcessorCount}");

        await MeasureAsync("последовательно (foreach)", meter =>
        {
            foreach (var item in cpuItems)
                using (meter.Enter())
                    BurnCpu(CpuWorkMs);
            return Task.CompletedTask;
        });

        await MeasureAsync("Parallel.ForEach", meter =>
        {
            Parallel.ForEach(cpuItems, _ =>
            {
                using (meter.Enter())
                    BurnCpu(CpuWorkMs);
            });
            return Task.CompletedTask;
        });

        await MeasureAsync("Task.WhenAll(Select(Task.Run))", meter =>
            Task.WhenAll(cpuItems.Select(_ => Task.Run(() =>
            {
                using (meter.Enter())
                    BurnCpu(CpuWorkMs);
            }))));

        await MeasureAsync("Task.WhenAll(Select(async x => ...)) без await внутри", meter =>
            Task.WhenAll(cpuItems.Select(async _ =>
            {
                using (meter.Enter())
                    BurnCpu(CpuWorkMs);
                await Task.CompletedTask;
            })));

        Log.Note("Parallel.ForEach делит работу между ядрами, переиспользует потоки и учитывает их число; вызывающий поток тоже работает и блокируется до конца. " +
                 "Task.WhenAll сам ничего не распараллеливает — он только ждёт уже запущенные задачи. " +
                 "Без Task.Run async-лямбда выполняется синхронно до первого незавершённого await, т.е. ПОСЛЕДОВАТЕЛЬНО. " +
                 "Для CPU-bound естественный выбор — Parallel.ForEach / PLINQ.");

        const int ioItems = 64;
        const int ioWaitMs = 200;
        Log.Section($"2. I/O-bound: {ioItems} запросов по {ioWaitMs} мс ожидания");

        await MeasureAsync("Parallel.ForEach + синхронный I/O", meter =>
        {
            Parallel.ForEach(Enumerable.Range(1, ioItems), _ =>
            {
                using (meter.Enter())
                    Thread.Sleep(ioWaitMs); // как HttpClient.Send / ADO.NET без async
            });
            return Task.CompletedTask;
        });

        await MeasureAsync("Task.WhenAll + async I/O", meter =>
            Task.WhenAll(Enumerable.Range(1, ioItems).Select(async _ =>
            {
                using (meter.Enter())
                    await Task.Delay(ioWaitMs);
            })));

        await MeasureAsync("Parallel.ForEachAsync (MaxDOP = 16) + async I/O", meter =>
            Parallel.ForEachAsync(Enumerable.Range(1, ioItems),
                new ParallelOptions { MaxDegreeOfParallelism = 16 },
                async (_, ct) =>
                {
                    using (meter.Enter())
                        await Task.Delay(ioWaitMs, ct);
                }));

        Log.Note("Parallel.ForEach + I/O: работает примерно по числу ядер потоков, и каждый заблокирован на всё время ожидания → " +
                 "64 / 8 × 200 мс ≈ 1,6 с при простаивающем CPU. Поднять MaxDegreeOfParallelism — значит заблокировать ещё больше потоков пула. " +
                 "Task.WhenAll: ожидание потоков не занимает, всё завершается за ~одно время запроса. " +
                 "Parallel.ForEachAsync: тоже без блокировок, но с контролируемым числом одновременных операций.");

        const int manyItems = 200;
        Log.Section($"3. Ограничение параллелизма: {manyItems} элементов");

        await MeasureAsync("Task.WhenAll (без ограничения)", meter =>
            Task.WhenAll(Enumerable.Range(1, manyItems).Select(async _ =>
            {
                using (meter.Enter())
                    await Task.Delay(20);
            })));

        await MeasureAsync("Parallel.ForEachAsync (MaxDOP = 10)", meter =>
            Parallel.ForEachAsync(Enumerable.Range(1, manyItems),
                new ParallelOptions { MaxDegreeOfParallelism = 10 },
                async (_, ct) =>
                {
                    using (meter.Enter())
                        await Task.Delay(20, ct);
                }));

        Log.Note("Task.WhenAll на 10 000 URL = 10 000 одновременных запросов: rate limit на стороне API, исчерпание пула соединений к БД, " +
                 "таймауты, всплеск памяти. Нужен предел: Parallel.ForEachAsync с MaxDegreeOfParallelism (по умолчанию = числу ядер), " +
                 "SemaphoreSlim + WhenAll (демо 7) или Channel с N читателями (демо 12).");

        Log.Section("4. Исключения: элементы 3 и 7 падают, всего 100 элементов");
        await ParallelForEachExceptionsAsync();
        await WhenAllExceptionsAsync();
        await ParallelForEachAsyncExceptionsAsync();
        Log.Note("Parallel.ForEach / ForEachAsync — fail-fast: после первой ошибки новые элементы не берутся " +
                 "(ForEachAsync ещё и отменяет ct у работающих). Task.WhenAll запускает всё сразу, поэтому все задачи доработают, " +
                 "даже если первая упала мгновенно, — нужен общий CancellationToken, если остальное больше не нужно.");

        Log.Section("5. Шпаргалка");
        Log.Code("""
                                    | Parallel.ForEach      | Task.WhenAll             | Parallel.ForEachAsync
            ------------------------+-----------------------+--------------------------+------------------------
            Тип работы              | CPU-bound             | I/O-bound                | I/O-bound (и смешанная)
            Вызывающий поток        | блокируется           | не блокируется (await)   | не блокируется (await)
            Ограничение параллелизма| MaxDegreeOfParallelism| нет (всё сразу)          | MaxDegreeOfParallelism
            Когда стартует работа   | внутри вызова         | при создании задач       | внутри вызова, порциями
            Async-лямбда            | НЕЛЬЗЯ (async void)   | да                       | да
            Ошибки                  | AggregateException,   | await → одно исключение, | await → исключение,
                                    | остановка новых       | все задачи доработают    | остановка + отмена ct
            Отмена                  | ParallelOptions       | только своим токеном     | ParallelOptions + ct
            """);
    }

    private static async Task MeasureAsync(string label, Func<ConcurrencyMeter, Task> body)
    {
        var meter = new ConcurrencyMeter();
        var threadsBefore = ThreadPool.ThreadCount;
        var sw = Stopwatch.StartNew();
        await body(meter);
        Log.Info($"  {label,-52} {sw.ElapsedMilliseconds,6} мс | одновременно: {meter.MaxConcurrency,3} | " +
                 $"разных потоков: {meter.DistinctThreads,3} | пул: {threadsBefore} → {ThreadPool.ThreadCount}");
    }

    private static Task ParallelForEachExceptionsAsync()
    {
        var processed = 0;
        try
        {
            Parallel.ForEach(Enumerable.Range(1, 100), item =>
            {
                Thread.Sleep(10);
                FailOn(item);
                Interlocked.Increment(ref processed);
            });
        }
        catch (AggregateException ex)
        {
            Log.Info($"  Parallel.ForEach:      {ex.GetType().Name} [{Messages(ex)}], обработано: {processed} из 100");
        }
        return Task.CompletedTask;
    }

    private static async Task WhenAllExceptionsAsync()
    {
        var processed = 0;
        var all = Task.WhenAll(Enumerable.Range(1, 100).Select(async item =>
        {
            await Task.Delay(10);
            FailOn(item);
            Interlocked.Increment(ref processed);
        }));
        try
        {
            await all;
        }
        catch (InvalidOperationException ex)
        {
            Log.Info($"  Task.WhenAll:          {ex.GetType().Name} ({ex.Message}), в all.Exception: [{Messages(all.Exception!)}], обработано: {processed} из 100");
        }
    }

    private static async Task ParallelForEachAsyncExceptionsAsync()
    {
        var processed = 0;
        var cancelled = 0;
        var loop = Parallel.ForEachAsync(Enumerable.Range(1, 100),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (item, ct) =>
            {
                try
                {
                    await Task.Delay(10, ct);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelled);
                    throw;
                }
                FailOn(item);
                Interlocked.Increment(ref processed);
            });
        try
        {
            await loop;
        }
        catch (InvalidOperationException ex)
        {
            Log.Info($"  Parallel.ForEachAsync: {ex.GetType().Name} ({ex.Message}), в loop.Exception: [{Messages(loop.Exception!)}], " +
                     $"обработано: {processed} из 100, отменено на лету: {cancelled}");
        }
    }

    private static void FailOn(int item)
    {
        if (item is 3 or 7)
            throw new InvalidOperationException($"ошибка {item}");
    }

    private static string Messages(AggregateException ex) =>
        string.Join(", ", ex.InnerExceptions.Select(e => e.Message));

    private static void BurnCpu(int milliseconds)
    {
        var sw = Stopwatch.StartNew();
        var x = 0.0;
        while (sw.ElapsedMilliseconds < milliseconds)
            x += Math.Sqrt(x + 1);
        GC.KeepAlive(x);
    }

    /// <summary>Считает пиковое число одновременно выполняющихся элементов и число задействованных потоков.</summary>
    private sealed class ConcurrencyMeter
    {
        private readonly ConcurrentDictionary<int, bool> _threads = new();
        private int _current;
        private int _max;

        public int MaxConcurrency => _max;

        public int DistinctThreads => _threads.Count;

        public Scope Enter()
        {
            _threads.TryAdd(Environment.CurrentManagedThreadId, true);
            var now = Interlocked.Increment(ref _current);
            int snapshot;
            while (now > (snapshot = _max) && Interlocked.CompareExchange(ref _max, now, snapshot) != snapshot)
            {
            }
            return new Scope(this);
        }

        public readonly struct Scope(ConcurrencyMeter meter) : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref meter._current);
        }
    }
}
