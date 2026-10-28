using System.Diagnostics;

namespace Threading.Synchronization.Demos;

public sealed class ReaderWriterLockDemo : IDemo
{
    public string Title => "ReaderWriterLockSlim";

    public string Question =>
        "Когда ReaderWriterLockSlim лучше lock, а когда хуже? Что такое upgradeable read lock? " +
        "Какие у него ловушки (рекурсия, привязка к потоку)? Чем его заменить?";

    private const int Readers = 4;
    private const int ReadsPerThread = 200;

    public Task RunAsync()
    {
        ConcurrentReaders();
        UpgradeableRead();
        Pitfalls();
        return Task.CompletedTask;
    }

    private static void ConcurrentReaders()
    {
        Log.Section($"1. {Readers} читателя × {ReadsPerThread} чтений, каждое держит блокировку ~0,5 мс");

        var gate = new Lock();
        var lockMeter = new ConcurrencyMeter();
        var lockTime = Parallelism.Run(Readers, _ =>
        {
            for (var i = 0; i < ReadsPerThread; i++)
                lock (gate)
                    lockMeter.Measure(SimulateRead);
        });

        using var rwLock = new ReaderWriterLockSlim();
        var rwMeter = new ConcurrencyMeter();
        var rwTime = Parallelism.Run(Readers, _ =>
        {
            for (var i = 0; i < ReadsPerThread; i++)
            {
                rwLock.EnterReadLock();
                try
                {
                    rwMeter.Measure(SimulateRead);
                }
                finally
                {
                    rwLock.ExitReadLock();
                }
            }
        });

        Log.Bad($"lock:                 {lockTime.TotalMilliseconds,6:F0} мс, одновременно читали: {lockMeter.Max}");
        Log.Good($"ReaderWriterLockSlim: {rwTime.TotalMilliseconds,6:F0} мс, одновременно читали: {rwMeter.Max}");
        Log.Note("Read lock могут держать многие потоки сразу, write lock — эксклюзивный (ждёт ухода всех читателей и блокирует новых). " +
                 "Выигрыш есть, только если чтения частые и долгие. Для коротких секций RWLS медленнее lock — у него дороже бухгалтерия.");
    }

    private static void UpgradeableRead()
    {
        Log.Section("2. Upgradeable read: прочитать, и если нужно — записать (GetOrAdd)");
        var cache = new RwCache();
        Parallelism.Run(8, _ => cache.GetOrAdd("config"));
        Log.Good($"8 потоков запросили один ключ, фабрика вызвана {cache.FactoryCalls} раз");
        Log.Note("Upgradeable read совместим с обычными читателями, но такой поток может быть только ОДИН — " +
                 "иначе два 'повышающихся' потока ждали бы друг друга. Повысить обычный read lock до write нельзя.");
    }

    private static void Pitfalls()
    {
        Log.Section("3. Ловушки");
        using var rwLock = new ReaderWriterLockSlim(); // LockRecursionPolicy.NoRecursion по умолчанию

        rwLock.EnterReadLock();
        try
        {
            rwLock.EnterReadLock();
        }
        catch (LockRecursionException ex)
        {
            Log.Bad($"повторный EnterReadLock тем же потоком: {ex.GetType().Name}");
        }

        try
        {
            rwLock.EnterWriteLock();
        }
        catch (LockRecursionException ex)
        {
            Log.Bad($"EnterWriteLock, удерживая read lock: {ex.GetType().Name} (иначе был бы deadlock)");
        }
        rwLock.ExitReadLock();

        rwLock.EnterReadLock();
        var otherThread = Parallelism.StartBackground(() =>
        {
            try
            {
                rwLock.ExitReadLock();
            }
            catch (SynchronizationLockException ex)
            {
                Log.Bad($"ExitReadLock из другого потока: {ex.GetType().Name} — блокировка привязана к потоку, через await не работает");
            }
        }, "other");
        otherThread.Join();
        rwLock.ExitReadLock();

        Log.Note("Не забывайте Dispose (держит ресурсы ядра) и try/finally вокруг Enter/Exit. " +
                 "Альтернативы: ConcurrentDictionary; неизменяемый снимок + атомарная подмена ссылки (демо 2); " +
                 "FrozenDictionary (.NET 8) для данных, которые не меняются после старта.");
    }

    private static void SimulateRead()
    {
        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2_000; // 0,5 мс
        while (Stopwatch.GetTimestamp() < until)
            Thread.SpinWait(10);
    }

    private sealed class ConcurrencyMeter
    {
        private int _current;
        private int _max;

        public int Max => _max;

        public void Measure(Action body)
        {
            var now = Interlocked.Increment(ref _current);
            int snapshot;
            while (now > (snapshot = _max) && Interlocked.CompareExchange(ref _max, now, snapshot) != snapshot)
            {
            }

            body();
            Interlocked.Decrement(ref _current);
        }
    }

    private sealed class RwCache
    {
        private readonly Dictionary<string, string> _items = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private int _factoryCalls;

        public int FactoryCalls => _factoryCalls;

        public string GetOrAdd(string key)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_items.TryGetValue(key, out var value))
                    return value;

                _lock.EnterWriteLock();
                try
                {
                    Interlocked.Increment(ref _factoryCalls);
                    Thread.Sleep(20);
                    return _items[key] = $"значение для {key}";
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }
    }
}
