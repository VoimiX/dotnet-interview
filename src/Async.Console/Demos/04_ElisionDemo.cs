namespace Async.Demos;

public sealed class ElisionDemo : IDemo
{
    public string Title => "Eliding async/await: можно ли вернуть Task без await?";

    public string Question =>
        "Чем 'return SomeAsync();' отличается от 'return await SomeAsync();'? Когда опускать async/await безопасно?";

    public async Task RunAsync()
    {
        Log.Section("1. using + return Task без await → ресурс освобождается раньше времени");
        try
        {
            Log.Info($"с await: {await ReadWithAwaitAsync()}");
            Log.Info($"без await: {await ReadWithoutAwaitAsync()}");
        }
        catch (ObjectDisposedException ex)
        {
            Log.Bad($"без await: {ex.GetType().Name} — Dispose() вызван до завершения чтения");
        }

        Log.Section("2. try/catch не ловит ошибку возвращённой (не ожидаемой) Task");
        await CatchWithoutAwaitAsync().ContinueWith(t => Log.Bad($"исключение пролетело мимо catch: {t.Exception!.InnerException!.Message}"));
        await CatchWithAwaitAsync();

        Log.Note("Без async/await метод возвращает Task и сразу выходит: using/try/finally/catch отрабатывают ДО завершения операции. " +
                 "Плюс в стектрейсе пропадает 'промежуточный' метод, а AsyncLocal-изменения 'протекают' к вызывающему. " +
                 "Безопасно опускать только для тривиальных перегрузок-прокси: Task FooAsync() => FooAsync(CancellationToken.None);");
    }

    private static async Task<string> ReadWithAwaitAsync()
    {
        using var resource = new FakeResource();
        return await resource.ReadAsync();
    }

    private static Task<string> ReadWithoutAwaitAsync()
    {
        using var resource = new FakeResource();
        return resource.ReadAsync();
    }

    private static Task CatchWithoutAwaitAsync()
    {
        try
        {
            return FailAsync();
        }
        catch (Exception)
        {
            Log.Good("поймали в catch"); // не выполнится
            return Task.CompletedTask;
        }
    }

    private static async Task CatchWithAwaitAsync()
    {
        try
        {
            await FailAsync();
        }
        catch (Exception ex)
        {
            Log.Good($"с await исключение поймано в catch: {ex.Message}");
        }
    }

    private static async Task FailAsync()
    {
        await Task.Delay(20);
        throw new InvalidOperationException("ошибка внутри операции");
    }

    private sealed class FakeResource : IDisposable
    {
        private bool _disposed;

        public async Task<string> ReadAsync()
        {
            await Task.Delay(50);
            ObjectDisposedException.ThrowIf(_disposed, this);
            return "данные";
        }

        public void Dispose() => _disposed = true;
    }
}
