using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Async.Demos;

public sealed class BasicsDemo : IDemo
{
    public string Title => "Основы: как работает async/await, потоки и state machine";

    public string Question =>
        "Создаёт ли async новый поток? Что генерирует компилятор? Чем I/O-bound отличается от CPU-bound?";

    public async Task RunAsync()
    {
        Log.Section("1. async-метод выполняется синхронно до первого незавершённого await");
        var task = DoWorkAsync();
        Log.Info($"Вызывающий код получил Task. IsCompleted = {task.IsCompleted}");
        await task;
        Log.Note("async НЕ создаёт поток. Код до первого 'настоящего' await выполняется на потоке вызывающего.");

        Log.Section("2. await завершённой задачи не переключает поток (fast path)");
        Log.Info("до await Task.CompletedTask / Task.FromResult");
        await Task.CompletedTask;
        var value = await Task.FromResult(42);
        Log.Info($"после — тот же поток, value = {value}");
        Log.Note("Awaiter сначала проверяет IsCompleted: если задача уже завершена, продолжение выполняется синхронно.");

        Log.Section("3. Task.Yield() принудительно делает продолжение асинхронным");
        Log.Info("до await Task.Yield()");
        await Task.Yield();
        Log.Info("после — продолжение было поставлено в очередь ThreadPool");

        Log.Section("4. Компилятор превращает async-метод в state machine");
        var stateMachine = typeof(BasicsDemo)
            .GetMethod(nameof(DoWorkAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType;
        Log.Info($"Тип: {stateMachine.Name}, IsValueType = {stateMachine.IsValueType}, " +
                 $"интерфейсы: {string.Join(", ", stateMachine.GetInterfaces().Select(i => i.Name))}");
        Log.Note("В Release это struct (боксится в кучу только при первом незавершённом await), в Debug — class. " +
                 "Поля: <>1__state, <>t__builder (AsyncTaskMethodBuilder), awaiter'ы и 'поднятые' локальные переменные. " +
                 "MoveNext() — switch по состоянию; await = 'подписать MoveNext как продолжение и выйти'.");

        Log.Section("5. I/O-bound: 1000 одновременных ожиданий не занимают 1000 потоков");
        var threadsBefore = ThreadPool.ThreadCount;
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(_ => Task.Delay(500)));
        Log.Info($"1000 ожиданий по 500 мс заняли {sw.ElapsedMilliseconds} мс; потоков в пуле: {threadsBefore} → {ThreadPool.ThreadCount}");
        Log.Note("\"There is no thread\": пока идёт I/O (таймер, сокет, файл через IOCP/epoll), поток не занят. " +
                 "Task.Run нужен для CPU-bound работы, а не для 'асинхронизации' I/O.");

        Log.Section("6. CPU-bound: Task.Run выгружает вычисления в ThreadPool");
        var sum = await Task.Run(() =>
        {
            Log.Info("вычисляю в ThreadPool...");
            long s = 0;
            for (var i = 0; i < 50_000_000; i++)
                s += i;
            return s;
        });
        Log.Info($"sum = {sum}");
        Log.Note("В ASP.NET Core оборачивать CPU-работу в Task.Run обычно бессмысленно: запрос и так обслуживается потоком пула " +
                 "(это лишь лишнее переключение потоков). Task.Run уместен для распараллеливания CPU-работы.");
    }

    private static async Task DoWorkAsync()
    {
        Log.Info("  DoWorkAsync: до await (поток вызывающего)");
        await Task.Delay(100);
        Log.Info("  DoWorkAsync: после await (поток из ThreadPool, может отличаться)");
    }
}
