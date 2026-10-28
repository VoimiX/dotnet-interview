using System.Collections.Concurrent;

namespace Async.Web.Services;

/// <summary>
/// Кэш с защитой от cache stampede: при одновременных промахах фабрика выполняется ОДИН раз,
/// остальные запросы ждут ту же Task.
/// В продакшене — HybridCache (.NET 9+), где это реализовано из коробки (плюс L2-кэш и TTL).
/// </summary>
public sealed class AsyncCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _items = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        // GetOrAdd может вызвать valueFactory несколько раз при гонке, но в словарь попадёт только один Lazy,
        // а Lazy гарантирует единственный запуск factory.
        var lazy = _items.GetOrAdd(key, _ => new Lazy<Task<object?>>(async () => await factory()));
        try
        {
            return (T)(await lazy.Value)!;
        }
        catch
        {
            // Не кэшируем ошибку навсегда.
            _items.TryRemove(new KeyValuePair<string, Lazy<Task<object?>>>(key, lazy));
            throw;
        }
    }

    public void Remove(string key) => _items.TryRemove(key, out _);
}
