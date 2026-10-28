using System.Threading.Tasks.Sources;

namespace Async.Demos;

public sealed class ValueTaskDemo : IDemo
{
    public string Title => "ValueTask<T>: когда нужен и какие у него ограничения";

    public string Question =>
        "Чем ValueTask отличается от Task? Когда его использовать? Почему ValueTask нельзя await'ить дважды?";

    public async Task RunAsync()
    {
        Log.Section("1. Горячий путь (данные в кэше): аллокации Task<T> vs ValueTask<T>");
        var repository = new CachedRepository();
        await repository.GetWithTaskAsync(1);       // прогрев: заполняем кэш
        await repository.GetWithValueTaskAsync(1);

        const int iterations = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
            await repository.GetWithTaskAsync(1);
        var taskBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
            await repository.GetWithValueTaskAsync(1);
        var valueTaskBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Log.Info($"{iterations} вызовов из кэша: Task<string> = {taskBytes:N0} байт, ValueTask<string> = {valueTaskBytes:N0} байт");
        Log.Note("Синхронный результат в Task<T> всё равно требует объекта Task в куче (кэшируются лишь bool, null, мелкие int). " +
                 "ValueTask<T> — struct: при синхронном результате аллокаций нет. " +
                 "Методы здесь без async намеренно: в Debug state machine — class и аллоцируется сама по себе.");

        Log.Section("2. Повторный await ValueTask из пула → InvalidOperationException");
        var source = new ReusableValueTaskSource();
        var first = source.StartAsync(1);
        Log.Info($"первый await: {await first}");

        var second = source.StartAsync(2); // объект-источник переиспользован (как сокеты в .NET)
        try
        {
            await first; // ОШИБКА: этот ValueTask уже "потреблён"
        }
        catch (InvalidOperationException ex)
        {
            Log.Bad($"повторный await: {ex.GetType().Name}");
        }
        Log.Info($"второй ValueTask: {await second}");

        Log.Note("Правила ValueTask: await ровно один раз; не вызывать .Result до завершения; " +
                 "не передавать в WhenAll/WhenAny напрямую и не кэшировать — для этого .AsTask(). " +
                 "Используйте ValueTask для горячих путей с частым синхронным результатом (кэш, буфер), по умолчанию — Task.");
    }

    private sealed class CachedRepository
    {
        private readonly Dictionary<int, string> _cache = new();

        public Task<string> GetWithTaskAsync(int id) =>
            _cache.TryGetValue(id, out var cached)
                ? Task.FromResult(cached) // новый Task<string> на каждый вызов
                : LoadAndCacheAsync(id);

        public ValueTask<string> GetWithValueTaskAsync(int id) =>
            _cache.TryGetValue(id, out var cached)
                ? new ValueTask<string>(cached) // без аллокаций
                : new ValueTask<string>(LoadAndCacheAsync(id));

        private async Task<string> LoadAndCacheAsync(int id)
        {
            await Task.Delay(10);
            return _cache[id] = $"entity-{id}";
        }
    }

    /// <summary>
    /// Переиспользуемый источник для ValueTask — так устроены Socket.ReceiveAsync, PipeReader и т.п.
    /// После Reset() версия меняется, и старые ValueTask становятся недействительными.
    /// </summary>
    private sealed class ReusableValueTaskSource : IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _core = new() { RunContinuationsAsynchronously = true };

        public ValueTask<int> StartAsync(int value)
        {
            _core.Reset();
            _ = CompleteLaterAsync(value);
            return new ValueTask<int>(this, _core.Version);
        }

        private async Task CompleteLaterAsync(int value)
        {
            await Task.Delay(50);
            _core.SetResult(value);
        }

        public int GetResult(short token) => _core.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }
}
