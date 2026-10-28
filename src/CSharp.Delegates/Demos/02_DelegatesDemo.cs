namespace CSharp.Delegates.Demos;

public sealed class DelegatesDemo : IDemo
{
    public string Title => "Делегаты: Func/Action, multicast, неизменяемость, отписка лямбды, вариантность";

    public string Question =>
        "Что такое делегат внутри? Что вернёт multicast Func? Что будет, если один обработчик бросит исключение? " +
        "Почему -= с лямбдой не отписывает?";

    public Task RunAsync()
    {
        Log.Section("1. Делегат — это объект: Target + Method");
        Func<int, bool> isEven = x => x % 2 == 0;
        Log.Info($"тип: {isEven.GetType().Name}, базовый класс: {isEven.GetType().BaseType!.Name}, Method: {isEven.Method.Name}");

        // Predicate<int> predicate = isEven;  // CS0029: одинаковая сигнатура, но разные типы делегатов
        var predicate = new Predicate<int>(isEven);
        Log.Info($"Func<int,bool> → Predicate<int> только через new Predicate<int>(func): [{string.Join(", ", new List<int> { 1, 2, 3, 4 }.FindAll(predicate))}]");
        Log.Note("Делегат — класс, унаследованный от MulticastDelegate. Типы делегатов номинальные: Func<int,bool> и Predicate<int> несовместимы. " +
                 "Поэтому в новых API используют Func/Action, а не собственные типы делегатов.");

        Log.Section("2. Multicast: несколько методов в одном делегате");
        Func<int> numbers = () => 1;
        numbers += () => 2;
        numbers += () => 3;
        Log.Info($"numbers() = {numbers()} — возвращается результат ПОСЛЕДНЕГО");
        var all = numbers.GetInvocationList().Cast<Func<int>>().Select(f => f());
        Log.Info($"все результаты через GetInvocationList(): [{string.Join(", ", all)}]");

        Action chain = () => Log.Info("  A");
        chain += () => throw new InvalidOperationException("B упал");
        chain += () => Log.Info("  C");
        try
        {
            chain();
        }
        catch (InvalidOperationException ex)
        {
            Log.Bad($"исключение в B ({ex.Message}) — C не вызван");
        }

        Log.Info("устойчивый вызов: каждый обработчик отдельно");
        foreach (var handler in chain.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Log.Info($"  залогировали ошибку обработчика: {ex.Message}");
            }
        }

        Log.Section("3. Делегаты неизменяемы: += создаёт новый объект");
        Action handlers = () => Log.Info("  A");
        var snapshot = handlers;
        handlers += () => Log.Info("  B");
        Log.Info("snapshot():");
        snapshot();
        Log.Note("Поэтому 'var handler = Changed; handler?.Invoke()' потокобезопасен: снимок не изменится, даже если кто-то отпишется в этот момент.");

        Log.Section("4. -= с лямбдой не отписывает");
        Action? subscribers = null;
        subscribers += () => Log.Info("  обработчик");
        subscribers -= () => Log.Info("  обработчик");
        Log.Bad($"после -= 'такой же' лямбды обработчиков: {Count(subscribers)}");

        subscribers = null;
        subscribers += Handler;
        subscribers -= Handler;
        Log.Good($"с методом (method group): {Count(subscribers)}");
        Log.Note("Каждая лямбда — отдельный метод и отдельный делегат. Делегаты равны, если совпадают Target и Method. " +
                 "Для отписки сохраняйте делегат в переменную/поле или используйте метод.");

        Log.Section("5. Вариантность: Func<out T>, Action<in T>");
        Func<string> getString = () => "текст";
        Func<object> getObject = getString;          // ковариантность
        Action<object> printObject = o => Log.Info($"  {o}");
        Action<string> printString = printObject;    // контравариантность
        printString((string)getObject());
        // Func<object> boxed = (Func<int>)(() => 1); // CS0029: вариантность работает только для ссылочных типов
        Log.Note("Func<string> можно присвоить Func<object>, Action<object> — Action<string>. Для value-типов нельзя: нужен boxing, а это другое представление.");

        return Task.CompletedTask;
    }

    private static void Handler()
    {
    }

    private static int Count(Delegate? @delegate) => @delegate?.GetInvocationList().Length ?? 0;
}
