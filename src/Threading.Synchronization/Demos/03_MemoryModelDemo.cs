using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Threading.Synchronization.Demos;

public sealed class MemoryModelDemo : IDemo
{
    public string Title => "volatile и модель памяти";

    public string Question =>
        "Почему поток может никогда не увидеть изменение флага? Что гарантирует volatile и чего не гарантирует? " +
        "Какие перестановки операций возможны на x86 и ARM? Зачем volatile в double-checked locking?";

    private const long SpinLimit = 3_000_000_000;

    private bool _stop;
    private volatile bool _stopVolatile;

    // Для litmus-теста: обычные статические поля, без volatile.
    private static int _x, _y, _r1, _r2;

    public Task RunAsync()
    {
        Hoisting();
        StoreLoadReordering();
        DoubleCheckedLocking();
        return Task.CompletedTask;
    }

    private void Hoisting()
    {
        Log.Section("1. JIT выносит чтение обычного поля из цикла");
        RunSpin("обычное поле", () => _stop = true, SpinUntilStopped);
        RunSpin("volatile-поле", () => _stopVolatile = true, SpinUntilStoppedVolatile);
        Log.Note("В цикле без вызовов и без volatile JIT вправе прочитать поле один раз и держать значение в регистре: " +
                 "для однопоточного кода результат тот же. Другой поток меняет память, но цикл её больше не читает. " +
                 "volatile (или Volatile.Read) запрещает такую оптимизацию. В Debug не воспроизводится — JIT без оптимизаций.");
    }

    private static void RunSpin(string label, Action stop, Func<long> spin)
    {
        long iterations = 0;
        var worker = Parallelism.StartBackground(() => iterations = spin(), label);
        Thread.Sleep(200);

        var sw = Stopwatch.StartNew();
        stop();
        worker.Join();

        if (iterations >= SpinLimit)
            Log.Bad($"{label}: флаг установлен, но цикл его не увидел — дошёл до предохранителя в {SpinLimit:N0} итераций ({sw.ElapsedMilliseconds} мс)");
        else
            Log.Good($"{label}: цикл увидел флаг через {sw.ElapsedMilliseconds} мс");
    }

    // Предохранитель по числу итераций нужен, чтобы зависший поток не крутился вечно.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private long SpinUntilStopped()
    {
        long i = 0;
        while (!_stop && i < SpinLimit)
            i++;
        return i;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private long SpinUntilStoppedVolatile()
    {
        long i = 0;
        while (!_stopVolatile && i < SpinLimit)
            i++;
        return i;
    }

    private static void StoreLoadReordering()
    {
        const int iterations = 200_000;
        Log.Section($"2. Перестановка store → load (litmus-тест, {iterations:N0} прогонов)");
        Log.Code("""
              // поток 1        // поток 2
              x = 1;            y = 1;
              r1 = y;           r2 = x;
              // может ли оказаться r1 == 0 И r2 == 0?
            """);

        var withoutFence = RunLitmus(iterations, fence: false);
        var withFence = RunLitmus(iterations, fence: true);

        Log.Info($"без барьера:                  r1 == r2 == 0 в {withoutFence:N0} прогонах");
        Log.Info($"с Interlocked.MemoryBarrier(): r1 == r2 == 0 в {withFence:N0} прогонах");
        Log.Note("Последовательно такого результата не бывает, но процессор может выполнить чтение раньше, " +
                 "чем запись станет видна другим ядрам (store buffer). Это единственная перестановка на x86; " +
                 "на ARM (Apple Silicon, AWS Graviton) возможны и другие — код, 'работающий' на x64, может сломаться на ARM. " +
                 "volatile даёт только acquire/release и эту перестановку НЕ запрещает — нужен полный барьер (Interlocked, lock).");
    }

    private static int RunLitmus(int iterations, bool fence)
    {
        var observed = 0;
        using var start = new Barrier(2);
        using var end = new Barrier(2, _ =>
        {
            if (_r1 == 0 && _r2 == 0)
                observed++;
            _x = _y = 0;
        });

        Parallelism.Run(2, index =>
        {
            for (var i = 0; i < iterations; i++)
            {
                start.SignalAndWait();
                if (index == 0)
                {
                    _x = 1;
                    if (fence)
                        Interlocked.MemoryBarrier();
                    _r1 = _y;
                }
                else
                {
                    _y = 1;
                    if (fence)
                        Interlocked.MemoryBarrier();
                    _r2 = _x;
                }
                end.SignalAndWait();
            }
        });

        return observed;
    }

    private static void DoubleCheckedLocking()
    {
        Log.Section("3. Double-checked locking");
        Log.Code("""
              private static volatile Singleton? _instance;   // volatile обязателен
              private static readonly Lock Gate = new();

              public static Singleton Instance
              {
                  get
                  {
                      if (_instance is null)                  // 1-я проверка без блокировки
                          lock (Gate)
                              _instance ??= new Singleton();  // 2-я проверка под lock
                      return _instance;
                  }
              }
            """);
        Log.Note("Без volatile запись ссылки в _instance может стать видна раньше, чем записи полей внутри конструктора, — " +
                 "другой поток получит недоинициализированный объект (на слабых моделях памяти, ARM). volatile-запись — release: " +
                 "всё, что записано до неё, видно тому, кто прочитал ссылку через volatile-чтение (acquire). " +
                 "На практике проще Lazy<T> или статический readonly-инициализатор — CLR гарантирует его потокобезопасность.");
        Log.Note("volatile не делает операции атомарными: _volatileCounter++ по-прежнему теряет обновления.");
    }
}
