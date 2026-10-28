namespace Async.Demos;

public sealed class ModernApisDemo : IDemo
{
    public string Title => "Современные API: ConfigureAwaitOptions, PeriodicTimer, IAsyncDisposable, TimeProvider";

    public string Question =>
        "Какие новые async API появились в .NET 6–10 и какие задачи они решают?";

    public async Task RunAsync()
    {
        Log.Section("1. ConfigureAwaitOptions.SuppressThrowing (.NET 8): дождаться без исключения");
        var background = FailAsync();
        await background.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Log.Info($"дождались завершения без try/catch, IsFaulted = {background.IsFaulted}");
        Log.Note("Удобно при остановке: 'дождаться фоновую задачу, а её ошибку обработать отдельно'. Только для Task, не Task<T>.");

        Log.Section("2. ConfigureAwaitOptions.ForceYielding: всегда асинхронное продолжение");
        Log.Info("до");
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        Log.Info("после — продолжение асинхронное, хотя задача уже была завершена (аналог Task.Yield())");

        Log.Section("3. PeriodicTimer (.NET 6): асинхронный таймер без наложения тиков");
        using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)))
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450)))
        {
            var tick = 0;
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                    Log.Info($"  tick {++tick}");
            }
            catch (OperationCanceledException)
            {
                Log.Info("  таймер остановлен токеном");
            }
        }
        Log.Note("System.Threading.Timer вызывает колбэк в ThreadPool, даже если предыдущий ещё выполняется (наложение + async void). " +
                 "PeriodicTimer — цикл с await: следующий тик не начнётся, пока не закончен текущий. Стандарт для BackgroundService.");

        Log.Section("4. IAsyncDisposable + await using");
        await using (var connection = new FakeConnection())
            await connection.QueryAsync();

        Log.Section("5. TimeProvider (.NET 8): тестируемое время");
        await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System);
        Log.Info("Task.Delay(delay, TimeProvider) — в тестах подставляется FakeTimeProvider и время 'перематывается' мгновенно");
        Log.Note("Также: CancellationTokenSource(delay, timeProvider), PeriodicTimer(period, timeProvider), task.WaitAsync(timeout, timeProvider).");
    }

    private static async Task FailAsync()
    {
        await Task.Delay(20);
        throw new InvalidOperationException("ошибка фоновой задачи");
    }

    private sealed class FakeConnection : IAsyncDisposable
    {
        public async Task QueryAsync()
        {
            await Task.Delay(30);
            Log.Info("  QueryAsync выполнен");
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Delay(30);
            Log.Info("  DisposeAsync: асинхронно сбросили буферы и закрыли соединение");
        }
    }
}
