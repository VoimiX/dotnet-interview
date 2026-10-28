namespace Async.Demos;

public sealed class AsyncVoidDemo : IDemo
{
    public string Title => "async void и fire-and-forget";

    public string Question =>
        "Почему async void опасен? Где он допустим? Как правильно сделать fire-and-forget?";

    public async Task RunAsync()
    {
        Log.Section("1. async void нельзя дождаться");
        _asyncVoidDone = false;
        RunAsyncVoid();
        Log.Bad($"RunAsyncVoid() вернул управление, работа ещё не сделана: done = {_asyncVoidDone}");
        await Task.Delay(200);
        Log.Info($"через 200 мс: done = {_asyncVoidDone}");
        Log.Note("Нет Task — нечего await'ить, нельзя узнать о завершении или ошибке. " +
                 "Исключение из async void не попадает в try/catch вызывающего: оно выбрасывается прямо в ThreadPool → " +
                 "AppDomain.UnhandledException → падает весь процесс (в ASP.NET Core — весь хост). Здесь не воспроизводим, чтобы не уронить демо. " +
                 "async void допустим только там, где сигнатуру навязывает делегат обработчика события.");

        Log.Section("2. async Task: исключение сохраняется в Task и пробрасывается при await");
        try
        {
            await FailAsync();
        }
        catch (InvalidOperationException ex)
        {
            Log.Good($"поймали: {ex.Message}");
        }

        Log.Section("3. Ловушка: async-лямбда, переданная в Action, — это async void");
        var items = new List<int> { 1, 2, 3 };
        items.ForEach(async i =>
        {
            await Task.Delay(100);
            Log.Info($"  обработан элемент {i}");
        });
        Log.Bad("List.ForEach 'завершился' ДО обработки элементов (лямбда скомпилирована в async void)");
        await Task.Delay(300);

        Log.Info("Правильно: Task.WhenAll(items.Select(async ...)) или обычный foreach с await");
        await Task.WhenAll(items.Select(async i =>
        {
            await Task.Delay(100);
            Log.Info($"  обработан элемент {i}");
        }));
        Log.Good("WhenAll завершился ПОСЛЕ обработки всех элементов");

        Log.Section("4. Fire-and-forget правильно: явная обработка ошибок");
        _ = FireAndForgetSafeAsync(FailAsync(), ex => Log.Bad($"фоновая задача упала, но мы это залогировали: {ex.Message}"));
        Log.Info("основной поток продолжает работу");
        await Task.Delay(200);
        Log.Note("В ASP.NET Core вместо fire-and-forget используйте очередь + BackgroundService (см. Web-проект): " +
                 "запрос закончится, а scoped-зависимости (DbContext) будут уничтожены.");
    }

    private static volatile bool _asyncVoidDone;

    private static async void RunAsyncVoid()
    {
        await Task.Delay(100);
        _asyncVoidDone = true;
    }

    private static async Task FailAsync()
    {
        await Task.Delay(50);
        throw new InvalidOperationException("Бум из async Task");
    }

    private static async Task FireAndForgetSafeAsync(Task task, Action<Exception> onError)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
