namespace Threading.Synchronization.Demos;

public sealed class LockDemo : IDemo
{
    public string Title => "lock и Monitor";

    public string Question =>
        "Почему counter++ не потокобезопасен? Во что компилируется lock? Реентерабелен ли он? " +
        "На каких объектах нельзя блокироваться? Как ждать условия через Monitor.Wait/Pulse?";

    private const int Threads = 4;
    private const int PerThread = 1_000_000;

    public Task RunAsync()
    {
        RaceCondition();
        WhatLockCompilesTo();
        Reentrancy();
        WrongLockObjects();
        WaitAndPulse();
        return Task.CompletedTask;
    }

    private static void RaceCondition()
    {
        Log.Section($"1. Гонка: {Threads} потока × {PerThread:N0} инкрементов");
        const int expected = Threads * PerThread;

        var unsafeCounter = 0;
        var unsafeTime = Parallelism.Run(Threads, _ =>
        {
            for (var i = 0; i < PerThread; i++)
                unsafeCounter++;
        });
        Log.Bad($"без синхронизации: {unsafeCounter,11:N0} из {expected:N0} ({unsafeTime.TotalMilliseconds:F0} мс)");

        var lockedCounter = 0;
        var gate = new Lock();
        var lockTime = Parallelism.Run(Threads, _ =>
        {
            for (var i = 0; i < PerThread; i++)
                lock (gate)
                    lockedCounter++;
        });
        Log.Good($"lock:              {lockedCounter,11:N0} из {expected:N0} ({lockTime.TotalMilliseconds:F0} мс)");

        var interlockedCounter = 0;
        var interlockedTime = Parallelism.Run(Threads, _ =>
        {
            for (var i = 0; i < PerThread; i++)
                Interlocked.Increment(ref interlockedCounter);
        });
        Log.Good($"Interlocked:       {interlockedCounter,11:N0} из {expected:N0} ({interlockedTime.TotalMilliseconds:F0} мс)");
        Log.Note("counter++ — это три операции: прочитать, прибавить, записать. Два потока читают одно значение — одно обновление теряется. " +
                 "Interlocked — одна атомарная инструкция процессора, lock — взаимное исключение для произвольного блока кода.");
    }

    private static void WhatLockCompilesTo()
    {
        Log.Section("2. Во что компилируется lock");
        Log.Code("""
              // lock (obj) { ... }  — для обычного object:
              bool lockTaken = false;
              try
              {
                  Monitor.Enter(obj, ref lockTaken);
                  ...
              }
              finally
              {
                  if (lockTaken) Monitor.Exit(obj);
              }

              // lock (gate) { ... }  — для System.Threading.Lock (.NET 9+):
              using (gate.EnterScope()) { ... }
            """);
        Log.Note("Monitor хранит состояние блокировки в заголовке объекта (thin lock → sync block при конкуренции): " +
                 "сначала короткий spin, затем ожидание в ядре. System.Threading.Lock — отдельный тип, быстрее и без sync block; " +
                 "компилятор предупреждает, если Lock приводят к object (тогда снова будет Monitor).");
    }

    private static void Reentrancy()
    {
        Log.Section("3. lock реентерабелен и привязан к потоку");
        var gate = new object();
        lock (gate)
        {
            lock (gate)
                Log.Good("вложенный lock тем же потоком — без deadlock (счётчик рекурсии)");
        }

        try
        {
            Monitor.Exit(gate);
        }
        catch (SynchronizationLockException ex)
        {
            Log.Bad($"Monitor.Exit потоком, который не владеет блокировкой: {ex.GetType().Name}");
        }
        Log.Note("Владелец — конкретный поток. Поэтому await внутри lock запрещён (CS1996): продолжение может оказаться " +
                 "на другом потоке и не сможет отпустить блокировку. Для async — SemaphoreSlim, он не реентерабелен.");
    }

    private static void WrongLockObjects()
    {
        Log.Section("4. На чём нельзя блокироваться");

        Log.Info($"строковые литералы интернированы: ReferenceEquals(ServiceA.Gate, ServiceB.Gate) = {ReferenceEquals(ServiceA.Gate, ServiceB.Gate)}");
        using (var held = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var holder = Parallelism.StartBackground(() =>
            {
                lock (ServiceA.Gate)
                {
                    held.Set();
                    release.Wait();
                }
            }, "ServiceA");

            held.Wait();
            var acquired = Monitor.TryEnter(ServiceB.Gate, TimeSpan.FromMilliseconds(200));
            Log.Bad($"ServiceB не может взять СВОЙ lock, пока ServiceA держит свой: TryEnter = {acquired}");
            if (acquired)
                Monitor.Exit(ServiceB.Gate);
            release.Set();
            holder.Join();
        }

        var counter = 0;
        var flag = 0;
        Parallelism.Run(Threads, _ =>
        {
            for (var i = 0; i < 100_000; i++)
                lock ((object)flag) // каждый раз новый boxed-объект → у каждого потока свой "замок"
                    counter++;
        });
        Log.Bad($"lock на упакованном value-типе: {counter:N0} из {Threads * 100_000:N0}");
        Log.Note("Строки (интернирование), typeof(X), this — объекты, доступные чужому коду: он может заблокироваться на них же " +
                 "и вызвать неожиданную конкуренцию или deadlock. lock(int) не компилируется, а (object)value — каждый раз новый объект. " +
                 "Правильно: private readonly Lock _gate = new(); (или private readonly object).");
    }

    private static void WaitAndPulse()
    {
        Log.Section("5. Monitor.Wait/Pulse: ожидание условия под lock (ограниченный буфер на 2 элемента)");
        var buffer = new BoundedBuffer(capacity: 2);

        var producer = Parallelism.StartBackground(() =>
        {
            for (var i = 1; i <= 5; i++)
            {
                buffer.Put(i);
                Log.Info($"producer: положил {i}");
            }
        }, "producer");

        var consumer = Parallelism.StartBackground(() =>
        {
            for (var i = 1; i <= 5; i++)
            {
                Thread.Sleep(100);
                Log.Info($"    consumer: взял {buffer.Take()}");
            }
        }, "consumer");

        producer.Join();
        consumer.Join();
        Log.Note("Monitor.Wait отпускает lock и засыпает; после Pulse/PulseAll снова захватывает lock и только тогда возвращается. " +
                 "Условие проверяется в while, а не в if: между Pulse и пробуждением другой поток мог успеть изменить состояние. " +
                 "В прикладном коде вместо этого — Channel, BlockingCollection, SemaphoreSlim.");
    }

    private static class ServiceA
    {
        public const string Gate = "resource-lock";
    }

    private static class ServiceB
    {
        public const string Gate = "resource-lock";
    }

    private sealed class BoundedBuffer(int capacity)
    {
        private readonly Queue<int> _items = new();
        private readonly object _gate = new();

        public void Put(int item)
        {
            lock (_gate)
            {
                while (_items.Count == capacity)
                    Monitor.Wait(_gate);

                _items.Enqueue(item);
                Monitor.PulseAll(_gate);
            }
        }

        public int Take()
        {
            lock (_gate)
            {
                while (_items.Count == 0)
                    Monitor.Wait(_gate);

                var item = _items.Dequeue();
                Monitor.PulseAll(_gate);
                return item;
            }
        }
    }
}
