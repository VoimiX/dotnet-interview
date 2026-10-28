using System.Runtime.CompilerServices;

namespace Async.Demos;

public sealed class ExceptionsDemo : IDemo
{
    public string Title => "Исключения: WhenAll, AggregateException, unobserved exceptions";

    public string Question =>
        "Какое исключение получим из await Task.WhenAll при нескольких ошибках? Чем .Result отличается от GetAwaiter().GetResult()? " +
        "Когда бросается исключение — при вызове или при await?";

    public async Task RunAsync()
    {
        Log.Section("1. await Task.WhenAll пробрасывает только ОДНО исключение");
        var all = Task.WhenAll(FailAfterAsync(100, "A"), FailAfterAsync(200, "B"), FailAfterAsync(50, "C"));
        try
        {
            await all;
        }
        catch (Exception ex)
        {
            Log.Info($"catch получил: {ex.GetType().Name}: {ex.Message}");
            Log.Info($"all.Exception.InnerExceptions: {string.Join(", ", all.Exception!.InnerExceptions.Select(e => e.Message))}");
        }
        Log.Note("await разворачивает AggregateException и бросает только InnerExceptions[0] (здесь — C, упавшую первой; " +
                 "порядок — деталь реализации, полагаться на него нельзя). Остальные ошибки 'теряются' для catch, " +
                 "но доступны в task.Exception.InnerExceptions — сохраните Task из WhenAll в переменную, чтобы залогировать все.");

        Log.Section("2. .Wait()/.Result → AggregateException, GetAwaiter().GetResult() → исходное исключение");
        try
        {
            FailAfterAsync(10, "X").Wait();
        }
        catch (AggregateException ex)
        {
            Log.Info($".Wait(): {ex.GetType().Name} → inner: {ex.InnerException!.Message}");
        }

        try
        {
            FailAfterAsync(10, "Y").GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            Log.Info($"GetAwaiter().GetResult(): {ex.GetType().Name}: {ex.Message}");
        }
        Log.Note("Оба варианта блокируют поток (sync-over-async). GetAwaiter().GetResult() лишь удобнее в обработке ошибок.");

        Log.Section("3. Когда бросается исключение: при вызове или при await?");
        var task = ParseAsync(null);
        Log.Info($"async-метод не бросил при вызове: task.IsFaulted = {task.IsFaulted}");
        try
        {
            await task;
        }
        catch (ArgumentNullException)
        {
            Log.Info("исключение получено только на await");
        }

        try
        {
            _ = ParseEagerAsync(null);
        }
        catch (ArgumentNullException)
        {
            Log.Info("метод без async (валидация + локальная async-функция) бросил СРАЗУ при вызове");
        }
        Log.Note("В async-методе даже ArgumentNullException упаковывается в Task. Если нужна мгновенная валидация " +
                 "аргументов — проверка в обычном методе, а работа в локальной async-функции.");

        Log.Section("4. Забытые задачи: TaskScheduler.UnobservedTaskException");
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            Log.Bad($"UnobservedTaskException: {e.Exception.InnerException?.Message}");
            e.SetObserved();
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            StartAndForget();
            await Task.Delay(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(50);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
        Log.Note("Событие срабатывает из финализатора Task, т.е. непредсказуемо и только после GC. " +
                 "С .NET 4.5 процесс не падает (если не включён ThrowUnobservedTaskExceptions) — ошибка просто теряется.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartAndForget() => _ = FailAfterAsync(10, "о которой все забыли");

    private static async Task FailAfterAsync(int delayMs, string name)
    {
        await Task.Delay(delayMs);
        throw new InvalidOperationException($"Ошибка {name}");
    }

    private static async Task<int> ParseAsync(string? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        await Task.Delay(10);
        return input.Length;
    }

    private static Task<int> ParseEagerAsync(string? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return CoreAsync(input);

        static async Task<int> CoreAsync(string s)
        {
            await Task.Delay(10);
            return s.Length;
        }
    }
}
