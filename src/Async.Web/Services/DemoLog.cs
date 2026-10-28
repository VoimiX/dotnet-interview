using System.Collections.Concurrent;

namespace Async.Web.Services;

/// <summary>Журнал событий, которые происходят уже после отправки ответа (фоновые задачи). Смотреть: GET /log.</summary>
public sealed class DemoLog(ILogger<DemoLog> logger)
{
    private const int Capacity = 200;
    private readonly ConcurrentQueue<string> _entries = new();

    public IEnumerable<string> Entries => _entries.Reverse();

    public void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] {message}";
        _entries.Enqueue(line);
        while (_entries.Count > Capacity)
            _entries.TryDequeue(out _);

        logger.LogInformation("{Message}", message);
    }
}
