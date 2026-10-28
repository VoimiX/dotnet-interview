using System.Diagnostics;

namespace Async.Demos;

public sealed class CombinatorsDemo : IDemo
{
    public string Title => "Task.WhenAll / WhenAny / WhenEach: параллельные запросы";

    public string Question =>
        "Как выполнить несколько запросов параллельно? Как взять первый ответ и отменить остальные? Как обрабатывать результаты по мере готовности?";

    public async Task RunAsync()
    {
        Log.Section("1. Последовательно vs параллельно");
        var sw = Stopwatch.StartNew();
        await FetchAsync("A", 300);
        await FetchAsync("B", 300);
        await FetchAsync("C", 300);
        Log.Info($"последовательно: {sw.ElapsedMilliseconds} мс");

        sw.Restart();
        var a = FetchAsync("A", 300);
        var b = FetchAsync("B", 300);
        var c = FetchAsync("C", 300);
        await Task.WhenAll(a, b, c);
        Log.Info($"параллельно: {sw.ElapsedMilliseconds} мс, результаты: {await a}, {await b}, {await c}");
        Log.Note("Задача стартует в момент вызова метода (hot task), await лишь ждёт. " +
                 "После WhenAll 'await a' не ждёт и не блокирует — задача уже завершена.");

        Log.Section("2. WhenAny: первый ответ выигрывает, остальных отменяем (hedged requests)");
        using (var cts = new CancellationTokenSource())
        {
            Task<string>[] mirrors =
            [
                FetchAsync("mirror-eu", 400, cts.Token),
                FetchAsync("mirror-us", 150, cts.Token),
                FetchAsync("mirror-asia", 300, cts.Token),
            ];

            var winner = await Task.WhenAny(mirrors);
            Log.Good($"победитель: {await winner}");
            await cts.CancelAsync();

            try
            {
                await Task.WhenAll(mirrors);
            }
            catch (OperationCanceledException)
            {
                // ожидаемо для проигравших
            }
            Log.Info($"статусы: {string.Join(", ", mirrors.Select(t => t.Status))}");
        }
        Log.Note("WhenAny никогда не бросает исключение — оно появится только на 'await winner'. " +
                 "Не забывайте отменять и наблюдать проигравшие задачи.");

        Log.Section("3. Task.WhenEach (.NET 9+): обрабатываем результаты по мере завершения");
        Task<string>[] tasks = [FetchAsync("slow", 300), FetchAsync("fast", 100), FetchAsync("medium", 200)];
        await foreach (var completed in Task.WhenEach(tasks))
            Log.Info($"  обработан: {await completed}");
        Log.Note("До .NET 9 писали цикл while + WhenAny + Remove — это O(n²).");
    }

    private static async Task<string> FetchAsync(string name, int delayMs, CancellationToken ct = default)
    {
        await Task.Delay(delayMs, ct);
        Log.Info($"  {name} готов ({delayMs} мс)");
        return name;
    }
}
