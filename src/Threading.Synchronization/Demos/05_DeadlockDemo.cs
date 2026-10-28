namespace Threading.Synchronization.Demos;

public sealed class DeadlockDemo : IDemo
{
    public string Title => "Deadlock: как получить и как избежать";

    public string Question =>
        "Напишите код, который гарантированно даёт deadlock. Какие условия для него нужны? Как его избежать и как диагностировать в проде?";

    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(1);

    public Task RunAsync()
    {
        LockOrdering();
        OrderedLocks();
        TryEnterWithTimeout();
        CallbackUnderLock();
        Diagnostics();
        return Task.CompletedTask;
    }

    private static void LockOrdering()
    {
        Log.Section("1. Классика: два перевода навстречу друг другу");
        var a = new Account(1, 1_000);
        var b = new Account(2, 1_000);

        var first = Parallelism.StartBackground(() => TransferNaive(a, b, 100), "A→B");
        var second = Parallelism.StartBackground(() => TransferNaive(b, a, 100), "B→A");

        var finished = first.Join(DetectionTimeout) & second.Join(DetectionTimeout);
        if (finished)
            Log.Good("оба перевода завершились (в этот раз повезло)");
        else
            Log.Bad($"DEADLOCK: за {DetectionTimeout.TotalSeconds} с ни один перевод не завершился. " +
                    "Поток A→B держит A и ждёт B, поток B→A держит B и ждёт A.");
        Log.Note("Потоки остались заблокированы навсегда — в сервисе это 'зависшие' запросы и растущий пул потоков.");
    }

    private static void TransferNaive(Account from, Account to, int amount)
    {
        lock (from.Gate)
        {
            Thread.Sleep(50); // окно, в которое второй поток успевает захватить свою блокировку
            lock (to.Gate)
            {
                from.Balance -= amount;
                to.Balance += amount;
            }
        }
    }

    private static void OrderedLocks()
    {
        Log.Section("2. Решение: единый порядок захвата блокировок");
        var a = new Account(1, 1_000);
        var b = new Account(2, 1_000);

        var first = Parallelism.StartBackground(() => TransferOrdered(a, b, 100), "A→B");
        var second = Parallelism.StartBackground(() => TransferOrdered(b, a, 300), "B→A");
        var finished = first.Join(DetectionTimeout) & second.Join(DetectionTimeout);

        Log.Good($"завершились: {finished}, балансы: A = {a.Balance}, B = {b.Balance}");
        Log.Note("Блокировки всегда берутся в одном порядке (здесь — по Id), независимо от направления перевода. " +
                 "Круговое ожидание становится невозможным.");
    }

    private static void TransferOrdered(Account from, Account to, int amount)
    {
        var (firstLock, secondLock) = from.Id < to.Id ? (from, to) : (to, from);
        lock (firstLock.Gate)
        {
            Thread.Sleep(50);
            lock (secondLock.Gate)
            {
                from.Balance -= amount;
                to.Balance += amount;
            }
        }
    }

    private static void TryEnterWithTimeout()
    {
        Log.Section("3. Решение: Monitor.TryEnter с таймаутом и повтором");
        var a = new Account(1, 1_000);
        var b = new Account(2, 1_000);
        var retries = 0;

        var first = Parallelism.StartBackground(() => TransferWithRetry(a, b, 100, ref retries), "A→B");
        var second = Parallelism.StartBackground(() => TransferWithRetry(b, a, 300, ref retries), "B→A");
        var finished = first.Join(TimeSpan.FromSeconds(5)) & second.Join(TimeSpan.FromSeconds(5));

        Log.Good($"завершились: {finished}, повторов: {retries}, балансы: A = {a.Balance}, B = {b.Balance}");
        Log.Note("Не дождались второй блокировки — отпускаем первую и пробуем снова со случайной паузой (иначе возможен livelock: " +
                 "потоки синхронно уступают друг другу бесконечно). Это ломает условие 'hold and wait'.");
    }

    private static void TransferWithRetry(Account from, Account to, int amount, ref int retries)
    {
        while (true)
        {
            lock (from.Gate)
            {
                Thread.Sleep(50);
                if (Monitor.TryEnter(to.Gate, TimeSpan.FromMilliseconds(20)))
                {
                    try
                    {
                        from.Balance -= amount;
                        to.Balance += amount;
                        return;
                    }
                    finally
                    {
                        Monitor.Exit(to.Gate);
                    }
                }
            }

            Interlocked.Increment(ref retries);
            Thread.Sleep(Random.Shared.Next(10, 60));
        }
    }

    private static void CallbackUnderLock()
    {
        Log.Section("4. Скрытый deadlock: вызов чужого кода под lock");
        var cache = new NotifyingCache();
        cache.Changed += () =>
        {
            // Обработчик выглядит безобидно: прочитать актуальное значение в другом потоке и дождаться.
            var read = Task.Run(() => cache.Get());
            if (read.Wait(DetectionTimeout))
                Log.Good("обработчик прочитал значение");
            else
                Log.Bad($"обработчик ждёт поток, которому нужен lock, а lock держит поток, ждущий обработчик — " +
                        $"deadlock (обнаружен по таймауту {DetectionTimeout.TotalSeconds} с)");
        };
        cache.Set(42);
        Log.Note("Событие вызвано изнутри lock. Код подписчика вам не принадлежит: он может ждать другой поток, брать свои блокировки. " +
                 "Правило: не вызывать колбэки, события, виртуальные методы и не ждать задачи, удерживая блокировку. " +
                 "Сначала изменить состояние под lock, затем выйти из lock и уведомить.");
    }

    private static void Diagnostics()
    {
        Log.Section("5. Условия deadlock и диагностика");
        Log.Note("Условия Коффмана (нужны все четыре): взаимное исключение, удержание и ожидание, нет принудительного отбора, круговое ожидание. " +
                 "Разорвать любое — и deadlock невозможен: порядок блокировок (круговое ожидание), TryEnter с таймаутом (удержание и ожидание), " +
                 "одна блокировка вместо двух, lock-free структуры.");
        Log.Note("В проде: dotnet-dump collect -p <pid> → dotnet-dump analyze → clrstack -all (стеки всех потоков) и syncblk " +
                 "(кто владеет какими Monitor); dotnet-stack report -p <pid>; в Visual Studio — окно Parallel Stacks. " +
                 "Симптомы: запросы не завершаются, CPU около нуля, число потоков растёт.");
    }

    private sealed class Account(int id, int balance)
    {
        public int Id { get; } = id;

        public int Balance { get; set; } = balance;

        public object Gate { get; } = new();
    }

    private sealed class NotifyingCache
    {
        private readonly object _gate = new();
        private int _value;

        public event Action? Changed;

        public int Get()
        {
            lock (_gate)
                return _value;
        }

        public void Set(int value)
        {
            lock (_gate)
            {
                _value = value;
                Changed?.Invoke(); // ОШИБКА: событие под lock
            }
        }
    }
}
