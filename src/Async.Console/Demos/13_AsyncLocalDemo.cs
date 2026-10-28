namespace Async.Demos;

public sealed class AsyncLocalDemo : IDemo
{
    public string Title => "ExecutionContext и AsyncLocal<T> vs ThreadStatic";

    public string Question =>
        "Как передать correlation id через цепочку async-вызовов? Почему [ThreadStatic] не работает? Видит ли родитель изменения AsyncLocal из дочернего метода?";

    private static readonly AsyncLocal<string?> CorrelationId = new();

    [ThreadStatic]
    private static string? _threadStaticId;

    public async Task RunAsync()
    {
        Log.Section("1. AsyncLocal 'течёт' вместе с ExecutionContext, ThreadStatic привязан к потоку");
        CorrelationId.Value = "req-42";
        _threadStaticId = "req-42";
        Log.Info($"исходный поток: AsyncLocal = {CorrelationId.Value}, ThreadStatic = {_threadStaticId}");

        var thread = new Thread(() =>
            Log.Info($"другой поток: AsyncLocal = {CorrelationId.Value ?? "null"}, ThreadStatic = {_threadStaticId ?? "null"}"));
        thread.Start();
        thread.Join();

        await Task.Delay(50);
        Log.Info($"после await: AsyncLocal = {CorrelationId.Value ?? "null"}, ThreadStatic = {_threadStaticId ?? "null"} (зависит от того, на какой поток вернулись)");

        Log.Section("2. Изменения в дочернем async-методе не видны родителю");
        await ChildAsync();
        Log.Info($"родитель после ChildAsync: AsyncLocal = {CorrelationId.Value}");
        Log.Note("ExecutionContext неизменяемый (copy-on-write): async-метод восстанавливает контекст вызывающего при выходе. " +
                 "Значения текут только 'вниз' по цепочке вызовов.");

        Log.Section("3. ExecutionContext.SuppressFlow — не передавать контекст");
        Task suppressed;
        using (ExecutionContext.SuppressFlow())
            suppressed = Task.Run(() => Log.Info($"Task.Run под SuppressFlow: AsyncLocal = {CorrelationId.Value ?? "null"}"));
        await suppressed;

        Log.Note("На AsyncLocal построены IHttpContextAccessor, Activity.Current (трейсинг), logging scopes. " +
                 "Осторожно: долгоживущие фоновые задачи/таймеры, созданные внутри запроса, 'захватывают' его контекст.");
        CorrelationId.Value = null;
    }

    private static async Task ChildAsync()
    {
        CorrelationId.Value = "изменено-в-дочернем";
        await Task.Yield();
        Log.Info($"дочерний метод: AsyncLocal = {CorrelationId.Value}");
    }
}
