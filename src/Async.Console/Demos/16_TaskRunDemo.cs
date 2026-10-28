using System.Diagnostics;

namespace Async.Demos;

public sealed class TaskRunDemo : IDemo
{
    public string Title => "Task.Run vs Task.Factory.StartNew";

    public string Question =>
        "Чем Task.Run отличается от StartNew? Что такое LongRunning? Почему Task.Run(() => File.ReadAllText(...)) — плохая 'асинхронность'? " +
        "Нужен ли Task.Run в контроллере ASP.NET Core?";

    public async Task RunAsync()
    {
        Log.Section("1. Async-лямбда: StartNew возвращает Task<Task>, Task.Run разворачивает сам");
        var sw = Stopwatch.StartNew();
        Task<Task> nested = Task.Factory.StartNew(() => Task.Delay(300));
        await nested;
        Log.Bad($"await StartNew(async): {sw.ElapsedMilliseconds} мс — дождались только запуска внешней задачи");

        sw.Restart();
        await nested.Unwrap();
        Log.Info($"await StartNew(...).Unwrap(): дождались внутреннюю ещё {sw.ElapsedMilliseconds} мс");

        sw.Restart();
        Task run = Task.Run(() => Task.Delay(300));
        await run;
        Log.Good($"await Task.Run(async): {sw.ElapsedMilliseconds} мс — Task.Run имеет перегрузку Func<Task> и разворачивает сам");
        Log.Note("Task.Run(f) ≈ Task.Factory.StartNew(f, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap(). " +
                 "Task.Run — 'безопасный' выбор по умолчанию; StartNew — для тонкой настройки.");

        Log.Section("2. Скрытая ловушка: StartNew использует TaskScheduler.Current, а не Default");
        await SchedulerPitfallAsync();
        Log.Note("Внутри задачи с нестандартным планировщиком (ConcurrentExclusiveSchedulerPair, Orleans, ограничивающий scheduler) " +
                 "StartNew без явного TaskScheduler.Default наследует его. Здесь эксклюзивный планировщик занят внешней задачей → " +
                 "вложенная ждёт. С Wait() внутри — классический deadlock. То же касается ContinueWith без указания scheduler.");

        Log.Section("3. TaskCreationOptions.LongRunning: отдельный поток вместо пула");
        await Task.Factory.StartNew(
            () => Log.Info($"  LongRunning, синхронный код: IsThreadPoolThread = {Thread.CurrentThread.IsThreadPoolThread}"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        await Task.Factory.StartNew(async () =>
        {
            Log.Info($"  LongRunning + async, до await: IsThreadPoolThread = {Thread.CurrentThread.IsThreadPoolThread}");
            await Task.Delay(50);
            Log.Bad($"  LongRunning + async, после await: IsThreadPoolThread = {Thread.CurrentThread.IsThreadPoolThread}");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        Log.Note("LongRunning создаёт выделенный поток — для долгой блокирующей/CPU работы (цикл чтения очереди, legacy-драйвер), " +
                 "чтобы не занимать поток пула навсегда. С async-лямбдой бессмысленно: поток живёт только до первого await, " +
                 "дальше код идёт в пуле, а создание потока стоит дорого. Для async-циклов — обычный Task.Run.");

        Log.Section("4. Фальшивая асинхронность: Task.Run поверх синхронного I/O");
        var count = Environment.ProcessorCount * 4;
        await MeasureIoAsync("Task.Run(() => синхронный I/O)", count, () => Task.Run(() => Thread.Sleep(300)));
        await MeasureIoAsync("настоящий async I/O", count, () => Task.Delay(300));
        Log.Note("Task.Run(() => File.ReadAllText(p)) не делает I/O асинхронным: поток пула всё равно заблокирован на время чтения — " +
                 "просто это ДРУГОЙ поток. Настоящий async (File.ReadAllTextAsync, сокеты, HttpClient) во время ожидания потоков не держит. " +
                 "Правило: библиотека не должна выдавать 'async over sync' за асинхронный API — пусть вызывающий сам решит, нужен ли Task.Run.");

        Log.Section("5. Захват переменной цикла");
        var forTasks = new List<Task<int>>();
        for (var i = 0; i < 5; i++)
            forTasks.Add(Task.Run(() => i));
        Log.Bad($"for + Task.Run(() => i): [{string.Join(", ", await Task.WhenAll(forTasks))}]");

        var fixedTasks = new List<Task<int>>();
        for (var i = 0; i < 5; i++)
        {
            var copy = i;
            fixedTasks.Add(Task.Run(() => copy));
        }
        Log.Good($"for + локальная копия: [{string.Join(", ", await Task.WhenAll(fixedTasks))}]");

        var foreachTasks = new List<Task<int>>();
        foreach (var i in Enumerable.Range(0, 5))
            foreachTasks.Add(Task.Run(() => i));
        Log.Good($"foreach: [{string.Join(", ", await Task.WhenAll(foreachTasks))}]");
        Log.Note("В for переменная одна на весь цикл — лямбды захватывают её саму, а не значение; к моменту запуска задачи цикл уже прошёл. " +
                 "В foreach с C# 5 на каждой итерации новая переменная. Результат for недетерминирован — зависит от того, когда стартовали задачи.");

        Log.Section("6. Исключение внутри Task.Run");
        var faulted = Task.Run(() => Fail("ошибка в Task.Run"));
        await Task.Delay(50);
        Log.Bad($"без await никто не узнал об ошибке: Status = {faulted.Status}");
        try
        {
            await faulted;
        }
        catch (InvalidOperationException ex)
        {
            Log.Good($"await пробросил исходное исключение: {ex.Message}");
        }

        try
        {
            await Task.Run(async () =>
            {
                await Task.Delay(10);
                Fail("ошибка в async-лямбде");
            });
        }
        catch (InvalidOperationException ex)
        {
            Log.Good($"из async-лямбды тоже (через Unwrap): {ex.Message}");
        }
        Log.Note("Исключение сохраняется в Task. Не await'нули — оно потеряно (максимум UnobservedTaskException после GC, демо 3).");

        Log.Section("7. Task.Run в обработчике запроса ASP.NET Core");
        await MeasureOverheadAsync();
        Log.Note("Обработчик запроса уже выполняется на потоке пула. await Task.Run(Compute) освобождает один поток пула и тут же занимает другой: " +
                 "выигрыша нет, есть лишнее переключение и лишний элемент в очереди пула (под нагрузкой — хуже). " +
                 "Короткие вычисления — вызывать напрямую. Тяжёлые и долгие — выносить из запроса: очередь + BackgroundService/отдельный воркер. " +
                 "Task.Run оправдан, когда нужно распараллелить CPU-работу внутри запроса на несколько ядер (и то осторожно — это отнимает потоки у других запросов).");
    }

    private static async Task SchedulerPitfallAsync()
    {
        var exclusive = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
        Task? viaStartNew = null;

        await Task.Factory.StartNew(() =>
        {
            Log.Info($"  внешняя задача на эксклюзивном планировщике: TaskScheduler.Current = {Describe(TaskScheduler.Current)}");

            viaStartNew = Task.Factory.StartNew(() =>
                Log.Info($"  StartNew внутри: TaskScheduler.Current = {Describe(TaskScheduler.Current)}"));
            var viaRun = Task.Run(() =>
                Log.Info($"  Task.Run внутри: TaskScheduler.Current = {Describe(TaskScheduler.Current)}"));

            Thread.Sleep(300); // внешняя задача ещё работает
            Log.Bad($"  через 300 мс: Task.Run выполнен = {viaRun.IsCompleted}, StartNew выполнен = {viaStartNew.IsCompleted}");
        }, CancellationToken.None, TaskCreationOptions.None, exclusive);

        await viaStartNew!;
        Log.Info("  StartNew выполнился только после завершения внешней задачи");
    }

    private static string Describe(TaskScheduler scheduler) =>
        scheduler == TaskScheduler.Default ? "Default (ThreadPool)" : scheduler.GetType().Name;

    private static async Task MeasureIoAsync(string label, int count, Func<Task> operation)
    {
        var threadsBefore = ThreadPool.ThreadCount;
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, count).Select(_ => operation()));
        Log.Info($"  {label}: {count} операций по 300 мс → {sw.ElapsedMilliseconds} мс, потоков в пуле: {threadsBefore} → {ThreadPool.ThreadCount}");
    }

    private static async Task MeasureOverheadAsync()
    {
        const int requests = 100_000;

        var sw = Stopwatch.StartNew();
        long direct = 0;
        for (var i = 0; i < requests; i++)
            direct += Compute(i);
        var directMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        long viaRun = 0;
        for (var i = 0; i < requests; i++)
        {
            var n = i;
            viaRun += await Task.Run(() => Compute(n));
        }
        var runMs = sw.Elapsed.TotalMilliseconds;

        Log.Info($"  {requests:N0} коротких вычислений: напрямую = {directMs:F0} мс, через await Task.Run = {runMs:F0} мс (результаты равны: {direct == viaRun})");
    }

    private static long Compute(int seed)
    {
        long acc = seed;
        for (var i = 0; i < 100; i++)
            acc = acc * 31 + i;
        return acc & 0xFFFF;
    }

    private static void Fail(string message) => throw new InvalidOperationException(message);
}
