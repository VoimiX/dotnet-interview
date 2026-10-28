namespace Async.Demos;

public sealed class AsyncLockDemo : IDemo
{
    public string Title => "Синхронизация: lock + await, SemaphoreSlim, AsyncLazy";

    public string Question =>
        "Почему нельзя await внутри lock? Как защитить общий ресурс в async-коде? Как сделать ленивую асинхронную инициализацию?";

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private int _unsafeCounter;
    private int _safeCounter;
    private int _initCalls;

    public async Task RunAsync()
    {
        _unsafeCounter = _safeCounter = _initCalls = 0;

        Log.Section("1. Гонка: read-await-write без синхронизации");
        await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
        {
            var value = _unsafeCounter;
            await Task.Delay(1);
            _unsafeCounter = value + 1;
        }));
        Log.Bad($"ожидали 100, получили {_unsafeCounter} — потерянные обновления");
        Log.Note("lock (x) { await ... } — ошибка компиляции CS1996: Monitor привязан к потоку, " +
                 "а после await код может продолжиться на другом потоке и не сможет освободить lock. " +
                 "System.Threading.Lock (.NET 9) — тоже только для синхронного кода.");

        Log.Section("2. SemaphoreSlim(1, 1) как async-mutex");
        await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
        {
            await _mutex.WaitAsync();
            try
            {
                var value = _safeCounter;
                await Task.Delay(1);
                _safeCounter = value + 1;
            }
            finally
            {
                _mutex.Release();
            }
        }));
        Log.Good($"ожидали 100, получили {_safeCounter}");
        Log.Note("SemaphoreSlim не реентерабелен: повторный WaitAsync из того же 'логического потока' — deadlock. " +
                 "Без привязки к потоку: Release можно вызвать после await на другом потоке.");

        Log.Section("3. AsyncLazy: Lazy<Task<T>> — одна инициализация на всех");
        var lazy = new Lazy<Task<string>>(InitializeAsync);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => lazy.Value));
        Log.Good($"10 одновременных обращений, InitializeAsync вызван {_initCalls} раз, результат: {results[0]}");
        Log.Note("Lazy (ExecutionAndPublication) гарантирует один вызов фабрики; все ждут одну Task. " +
                 "Минус: если Task упала — ошибка закэширована навсегда (нужен сброс Lazy при ошибке).");
    }

    private async Task<string> InitializeAsync()
    {
        Interlocked.Increment(ref _initCalls);
        await Task.Delay(200);
        return "конфигурация загружена";
    }
}
