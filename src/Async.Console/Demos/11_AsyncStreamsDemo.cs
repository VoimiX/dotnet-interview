using System.Runtime.CompilerServices;

namespace Async.Demos;

public sealed class AsyncStreamsDemo : IDemo
{
    public string Title => "IAsyncEnumerable<T>: async streams и отмена";

    public string Question =>
        "Чем IAsyncEnumerable отличается от Task<List<T>>? Зачем [EnumeratorCancellation]? Что будет при break?";

    public async Task RunAsync()
    {
        Log.Section("1. await foreach: элементы приходят по одному, по мере готовности");
        await foreach (var item in ProduceAsync(3))
            Log.Info($"  получен {item}");
        Log.Note("Task<List<T>> отдаст всё разом в конце и держит всё в памяти; IAsyncEnumerable — pull-модель с потоковой обработкой.");

        Log.Section("2. Отмена через WithCancellation + [EnumeratorCancellation]");
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(180)))
        {
            try
            {
                await foreach (var item in ProduceAsync(100).WithCancellation(cts.Token))
                    Log.Info($"  получен {item}");
            }
            catch (OperationCanceledException)
            {
                Log.Good("поток отменён");
            }
        }
        Log.Note("[EnumeratorCancellation] связывает токен из WithCancellation(...) с параметром итератора. " +
                 "Без атрибута токен из WithCancellation просто игнорируется.");

        Log.Section("3. break → DisposeAsync итератора → выполняется finally");
        await foreach (var item in ProduceAsync(100))
        {
            Log.Info($"  получен {item}");
            if (item == 2)
                break;
        }

        Log.Section("4. LINQ для IAsyncEnumerable (System.Linq.AsyncEnumerable встроен в .NET 10)");
        var list = await ProduceAsync(20, log: false)
            .Where(x => x % 2 == 0)
            .Select(x => x * 10)
            .Take(3)
            .ToListAsync();
        Log.Info($"результат: [{string.Join(", ", list)}]");
        Log.Note("До .NET 10 нужен был NuGet-пакет System.Linq.Async.");
    }

    private static async IAsyncEnumerable<int> ProduceAsync(
        int count,
        bool log = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            for (var i = 1; i <= count; i++)
            {
                await Task.Delay(50, ct);
                if (log)
                    Log.Info($"  итератор: yield {i}");
                yield return i;
            }
        }
        finally
        {
            if (log)
                Log.Info("  итератор: finally (освобождение ресурсов)");
        }
    }
}
