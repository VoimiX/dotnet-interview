using System.Collections.Immutable;

namespace Threading.Synchronization.Demos;

public sealed class InterlockedDemo : IDemo
{
    public string Title => "Interlocked и lock-free";

    public string Question =>
        "Что гарантирует Interlocked? Как устроен CAS-цикл (CompareExchange)? Как обновить сложное состояние без lock? " +
        "Почему ленивая инициализация через CompareExchange может создать объект дважды?";

    private const int Threads = 4;

    public Task RunAsync()
    {
        Operations();
        CompareExchangeLoop();
        ImmutableSnapshot();
        LazyInitialization();
        return Task.CompletedTask;
    }

    private static void Operations()
    {
        Log.Section("1. Базовые операции: все возвращают значение, полученное атомарно");
        var value = 10;
        Log.Info($"Increment(ref 10)               → вернул {Interlocked.Increment(ref value)}, value = {value}");
        Log.Info($"Add(ref 11, 5)                  → вернул {Interlocked.Add(ref value, 5)}, value = {value}");
        Log.Info($"Exchange(ref 16, 100)           → вернул старое {Interlocked.Exchange(ref value, 100)}, value = {value}");
        Log.Info($"CompareExchange(ref 100, 1, 50) → вернул {Interlocked.CompareExchange(ref value, 1, 50)}, value = {value} (не 50 — замены нет)");
        Log.Info($"CompareExchange(ref 100, 1, 100)→ вернул {Interlocked.CompareExchange(ref value, 1, 100)}, value = {value} (совпало — заменил)");
        Log.Note("Каждая операция — одна атомарная инструкция (lock xadd, lock cmpxchg) и полный барьер памяти. " +
                 "Interlocked.Read(ref long) нужен для атомарного чтения 64-битного значения в 32-битном процессе.");
    }

    private static void CompareExchangeLoop()
    {
        const int perThread = 200_000;
        const int cap = 500_000;
        Log.Section($"2. CAS-цикл: операция, которой нет в Interlocked (инкремент с потолком {cap:N0})");
        var total = 0;
        var retries = 0;
        Parallelism.Run(Threads, _ =>
        {
            for (var i = 0; i < perThread; i++)
            {
                while (true)
                {
                    var current = Volatile.Read(ref total);
                    var updated = Math.Min(current + 1, cap);
                    if (Interlocked.CompareExchange(ref total, updated, current) == current)
                        break;
                    Interlocked.Increment(ref retries); // кто-то успел изменить total между чтением и записью
                }
            }
        });
        Log.Good($"total = {total:N0} (ожидали {Math.Min(Threads * perThread, cap):N0}), повторов цикла из-за конкуренции: {retries:N0}");
        Log.Code("""
              while (true)
              {
                  var current = Volatile.Read(ref location);
                  var updated = Compute(current);
                  if (Interlocked.CompareExchange(ref location, updated, current) == current)
                      break;   // никто не вмешался — записали
                  // иначе значение изменилось: пересчитываем от нового
              }
            """);
        Log.Note("Это оптимистичная синхронизация: потоки не ждут друг друга, а повторяют попытку. " +
                 "При сильной конкуренции повторов много — тогда lock может оказаться быстрее.");
    }

    private static void ImmutableSnapshot()
    {
        Log.Section("3. Copy-on-write: неизменяемый снимок + атомарная подмена ссылки");
        var list = ImmutableList<int>.Empty;
        Parallelism.Run(Threads, index =>
        {
            for (var i = 0; i < 1_000; i++)
                ImmutableInterlocked.Update(ref list, (current, item) => current.Add(item), index * 1_000 + i);
        });
        Log.Good($"элементов: {list.Count:N0} из {Threads * 1_000:N0}");
        Log.Note("Читатели берут текущую ссылку и работают со снимком без блокировок; писатель создаёт новую версию " +
                 "и подменяет ссылку CAS-циклом (ImmutableInterlocked.Update). Хорошо для 'много чтений — редкие записи': " +
                 "конфигурация, таблицы маршрутизации, кэши справочников.");
    }

    private static void LazyInitialization()
    {
        Log.Section("4. Ленивая инициализация: CompareExchange vs Lazy<T>");
        Expensive.Constructed = 0;
        Expensive? instance = null;
        Parallelism.Run(8, _ => Interlocked.CompareExchange(ref instance, new Expensive(), null));
        Log.Bad($"CompareExchange: конструктор вызван {Expensive.Constructed} раз, но опубликован один экземпляр");

        Expensive.Constructed = 0;
        var lazy = new Lazy<Expensive>(() => new Expensive(), LazyThreadSafetyMode.ExecutionAndPublication);
        Parallelism.Run(8, index => _ = lazy.Value);
        Log.Good($"Lazy<T> (ExecutionAndPublication): конструктор вызван {Expensive.Constructed} раз");
        Log.Note("CompareExchange гарантирует, что ВСЕ увидят один экземпляр, но не что он создан один раз — лишние уходят в мусор " +
                 "(LazyThreadSafetyMode.PublicationOnly, LazyInitializer.EnsureInitialized ведут себя так же). " +
                 "Если конструктор дорогой или с побочными эффектами — Lazy<T> с ExecutionAndPublication (по умолчанию).");
    }

    private sealed class Expensive
    {
        public static int Constructed;

        public Expensive()
        {
            Interlocked.Increment(ref Constructed);
            Thread.Sleep(50);
        }
    }
}
