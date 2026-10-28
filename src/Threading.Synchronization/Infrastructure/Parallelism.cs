using System.Diagnostics;

namespace Threading.Synchronization;

public static class Parallelism
{
    /// <summary>
    /// Запускает <paramref name="body"/> одновременно на <paramref name="threads"/> выделенных потоках
    /// (Barrier — чтобы стартовали вместе и конкуренция была настоящей). Возвращает время выполнения.
    /// </summary>
    public static TimeSpan Run(int threads, Action<int> body)
    {
        using var start = new Barrier(threads + 1);
        var workers = Enumerable.Range(0, threads)
            .Select(index => new Thread(() =>
            {
                start.SignalAndWait();
                body(index);
            }) { IsBackground = true })
            .ToArray();

        foreach (var worker in workers)
            worker.Start();

        start.SignalAndWait();
        var sw = Stopwatch.StartNew();
        foreach (var worker in workers)
            worker.Join();
        return sw.Elapsed;
    }

    public static Thread StartBackground(Action body, string name)
    {
        var thread = new Thread(() => body()) { IsBackground = true, Name = name };
        thread.Start();
        return thread;
    }
}
