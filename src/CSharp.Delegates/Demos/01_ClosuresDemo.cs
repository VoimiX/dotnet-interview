using System.Reflection;
using System.Runtime.CompilerServices;

namespace CSharp.Delegates.Demos;

public sealed class ClosuresDemo : IDemo
{
    public string Title => "Лямбды и замыкания";

    public string Question =>
        "Что захватывает лямбда — значение или переменную? Во что компилятор превращает замыкание? " +
        "Откуда берутся лишние аллокации и утечки? Зачем static-лямбды?";

    private const int Iterations = 100_000;

    public Task RunAsync()
    {
        CapturesVariable();
        CompiledShape();
        SharedClosureKeepsObjectAlive();
        LoopCapture();
        Allocations();
        HiddenAllocation();
        LocalFunctions();
        return Task.CompletedTask;
    }

    private static void CapturesVariable()
    {
        Log.Section("1. Захватывается переменная, а не значение");
        var counter = 1;
        Func<int> read = () => counter;
        Action increment = () => counter++;

        counter = 10;
        Log.Info($"counter = 10 уже ПОСЛЕ создания лямбды → read() = {read()}");
        increment();
        Log.Info($"после increment(): counter = {counter}, read() = {read()}");
        Log.Note("Метод и обе лямбды работают с одной и той же переменной. Значение читается в момент вызова, а не создания лямбды.");
    }

    private static void CompiledShape()
    {
        Log.Section("2. Во что компилятор превращает лямбду");
        var factor = 3;
        Func<int, int> capturing = x => x * factor;
        Func<int, int> nonCapturing = x => x * 2;

        Describe("с захватом", capturing);
        Describe("без захвата", nonCapturing);
        Log.Note("С захватом: захваченные переменные становятся полями сгенерированного класса <>c__DisplayClass, " +
                 "лямбда — его методом, а сама переменная в методе — это поле этого объекта. " +
                 "Без захвата: метод синглтона <>c, а делегат создаётся один раз и кэшируется в статическом поле.");
    }

    private static void SharedClosureKeepsObjectAlive()
    {
        Log.Section("3. Один класс-контейнер на scope: маленькая лямбда держит большой объект");
        var (keeper, bigArray) = CreateKeeper();
        ForceGc();
        Describe("keeper", keeper);
        Log.Bad($"keeper читает только small, но массив на 50 МБ всё ещё жив: {bigArray.IsAlive}");
        Log.Note("Все переменные одного scope, захваченные ЛЮБЫМИ лямбдами, попадают в один DisplayClass. " +
                 "Долгоживущая лямбда (подписка, кэш, таймер) удерживает всё, что захватили её 'соседи'. " +
                 "Лечение: вынести создание лямбд в разные методы/scope, не захватывать лишнего.");
        GC.KeepAlive(keeper);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Func<int> Keeper, WeakReference BigArray) CreateKeeper()
    {
        var big = new byte[50 * 1024 * 1024];
        var small = 42;
        Func<int> useBig = () => big.Length;   // никогда не вызывается и сразу теряется
        Func<int> keeper = () => small;
        _ = useBig;
        return (keeper, new WeakReference(big));
    }

    private static void LoopCapture()
    {
        Log.Section("4. Захват переменной цикла");
        var fromFor = new List<Func<int>>();
        for (var i = 0; i < 3; i++)
            fromFor.Add(() => i);
        Log.Bad($"for:     {string.Join(" ", fromFor.Select(f => f()))}");

        var fromForeach = new List<Func<int>>();
        foreach (var i in Enumerable.Range(0, 3))
            fromForeach.Add(() => i);
        Log.Good($"foreach: {string.Join(" ", fromForeach.Select(f => f()))}");
        Log.Note("В for одна переменная i на весь цикл (её поле в DisplayClass), к моменту вызова там 3. " +
                 "В foreach с C# 5 переменная новая на каждой итерации. Лечение для for: var copy = i;");
    }

    private static void Allocations()
    {
        Log.Section($"5. Аллокации: {Iterations:N0} вызовов метода, принимающего лямбду");
        var nonCapturing = MeasureBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
                Apply(i, x => x * 2);
        });

        var capturing = MeasureBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
            {
                var factor = i;
                Apply(i, x => x * factor);
            }
        });

        var staticWithState = MeasureBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
                Apply(i, static (x, factor) => x * factor, i);
        });

        Log.Info($"  без захвата:                   {nonCapturing,12:N0} байт");
        Log.Bad($" с захватом:                    {capturing,12:N0} байт (~{capturing / Iterations} байт на вызов: DisplayClass + делегат)");
        Log.Good($" static-лямбда + state-параметр: {staticWithState,12:N0} байт");
        Log.Note("static-лямбда (C# 9) запрещает захват на этапе компиляции — гарантия отсутствия аллокаций. " +
                 "Поэтому у API есть перегрузки со state: ConcurrentDictionary.GetOrAdd(key, (k, arg) => ..., arg), " +
                 "string.Create(length, state, ...), CancellationToken.Register(callback, state).");
    }

    private static void HiddenAllocation()
    {
        Log.Section("6. Скрытая аллокация: DisplayClass создаётся в начале scope, даже если лямбда не нужна");
        var list = new List<int> { 1, 2, 3 };
        var eager = MeasureBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
                ContainsIfEnabled(list, i, enabled: false);
        });
        var lazy = MeasureBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
                ContainsIfEnabledFixed(list, i, enabled: false);
        });

        Log.Bad($" лямбда в том же методе, ветка не выполняется: {eager,10:N0} байт");
        Log.Good($" лямбда вынесена в отдельный метод:           {lazy,10:N0} байт");
        Log.Note("Захваченный параметр value живёт в DisplayClass, а он создаётся при входе в метод — до проверки enabled. " +
                 "На горячем пути (логирование, валидация) это лишний мусор на каждый вызов.");
    }

    private static bool ContainsIfEnabled(List<int> list, int value, bool enabled)
    {
        if (!enabled)
            return false;
        return list.Exists(x => x == value);
    }

    private static bool ContainsIfEnabledFixed(List<int> list, int value, bool enabled) =>
        enabled && ExistsCore(list, value);

    private static bool ExistsCore(List<int> list, int value) => list.Exists(x => x == value);

    private static void LocalFunctions()
    {
        Log.Section("7. Локальная функция вместо лямбды");
        var data = Enumerable.Range(1, 10).ToArray();
        var lambda = MeasureBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
                SumWithLambda(data, i);
        });
        var local = MeasureBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
                SumWithLocalFunction(data, i);
        });

        Log.Bad($" лямбда с захватом:            {lambda,10:N0} байт");
        Log.Good($" локальная функция с захватом: {local,10:N0} байт");
        Log.Note("Если локальная функция не превращается в делегат, компилятор кладёт захваченные переменные в struct " +
                 "и передаёт её по ref — без аллокаций. Как только её передали как Func/Action — аллокация вернётся.");
    }

    private static int SumWithLambda(int[] data, int factor)
    {
        Func<int, int> multiply = x => x * factor;
        var sum = 0;
        foreach (var item in data)
            sum += multiply(item);
        return sum;
    }

    private static int SumWithLocalFunction(int[] data, int factor)
    {
        var sum = 0;
        foreach (var item in data)
            sum += Multiply(item);
        return sum;

        int Multiply(int x) => x * factor;
    }

    private static int Apply(int value, Func<int, int> func) => func(value);

    private static int Apply<TState>(int value, Func<int, TState, int> func, TState state) => func(value, state);

    private static long MeasureBytes(Action body)
    {
        body(); // прогрев: кэш делегатов, JIT
        var before = GC.GetAllocatedBytesForCurrentThread();
        body();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Describe(string label, Delegate @delegate)
    {
        var target = @delegate.Target;
        Log.Info($"{label}: Method = {@delegate.Method.Name}, Target = {target?.GetType().Name ?? "null"}");
        if (target is null)
            return;

        foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Log.Info($"    поле {field.Name} = {Format(field.GetValue(target))}");
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        Array array => $"{array.GetType().GetElementType()!.Name}[{array.Length:N0}]",
        _ => value.ToString() ?? string.Empty,
    };

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
