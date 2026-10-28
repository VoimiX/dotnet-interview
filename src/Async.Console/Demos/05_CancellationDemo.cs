namespace Async.Demos;

public sealed class CancellationDemo : IDemo
{
    public string Title => "Отмена: CancellationToken, таймауты, linked tokens";

    public string Question =>
        "Как работает кооперативная отмена? Как совместить таймаут и отмену пользователем? Отменяет ли WaitAsync саму операцию?";

    public async Task RunAsync()
    {
        Log.Section("1. Кооперативная отмена + CancelAfter");
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350)))
        {
            try
            {
                await LongWorkAsync(10, cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                Log.Good($"{ex.GetType().Name}; ex.CancellationToken == cts.Token: {ex.CancellationToken == cts.Token}");
            }
        }
        Log.Note("Токен ничего не 'убивает' — код сам проверяет его (ThrowIfCancellationRequested) или передаёт дальше в I/O API. " +
                 "TaskCanceledException наследует OperationCanceledException → ловите OperationCanceledException.");

        Log.Section("2. Linked token: таймаут операции + отмена пользователем — кто сработал?");
        await RunWithTimeoutAsync(userCancelAfterMs: 1000, timeoutMs: 250);
        await RunWithTimeoutAsync(userCancelAfterMs: 150, timeoutMs: 1000);

        Log.Section("3. WaitAsync(timeout) перестаёт ЖДАТЬ, но НЕ отменяет операцию");
        var work = NonCancellableWorkAsync();
        try
        {
            await work.WaitAsync(TimeSpan.FromMilliseconds(100));
        }
        catch (TimeoutException)
        {
            Log.Info("TimeoutException: мы перестали ждать...");
        }
        await work;
        Log.Note("...а операция доработала до конца. То же с Task.WhenAny(work, Task.Delay(t)). " +
                 "Настоящая отмена возможна только если операция принимает CancellationToken.");

        Log.Section("4. token.Register: отмена для API без поддержки токенов");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
        using (cts.Token.Register(() => tcs.TrySetCanceled(cts.Token)))
        {
            try
            {
                await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                Log.Good("TaskCompletionSource отменён через Register");
            }
        }
        Log.Note("Регистрацию нужно Dispose'ить (иначе утечка колбэков на долгоживущем токене). " +
                 "CancellationTokenSource с таймером тоже нужно Dispose'ить.");
    }

    private static async Task RunWithTimeoutAsync(int userCancelAfterMs, int timeoutMs)
    {
        using var userCts = new CancellationTokenSource(userCancelAfterMs);  // например, HttpContext.RequestAborted
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(userCts.Token, timeoutCts.Token);

        try
        {
            await LongWorkAsync(20, linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            Log.Info($"  → сработал ТАЙМАУТ ({timeoutMs} мс)");
        }
        catch (OperationCanceledException) when (userCts.IsCancellationRequested)
        {
            Log.Info($"  → отменил ПОЛЬЗОВАТЕЛЬ ({userCancelAfterMs} мс)");
        }
    }

    private static async Task LongWorkAsync(int steps, CancellationToken ct)
    {
        for (var i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(100, ct);
            Log.Info($"  шаг {i}/{steps}");
        }
    }

    private static async Task NonCancellableWorkAsync()
    {
        await Task.Delay(300);
        Log.Info("  NonCancellableWorkAsync завершилась");
    }
}
