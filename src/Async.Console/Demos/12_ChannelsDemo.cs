using System.Threading.Channels;

namespace Async.Demos;

public sealed class ChannelsDemo : IDemo
{
    public string Title => "System.Threading.Channels: producer/consumer с backpressure";

    public string Question =>
        "Как реализовать асинхронную очередь producer/consumer? Чем Channel лучше BlockingCollection? Что такое backpressure?";

    public async Task RunAsync()
    {
        Log.Section("Bounded channel (capacity = 3), 1 producer, 2 consumers");
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(capacity: 3)
        {
            FullMode = BoundedChannelFullMode.Wait, // producer ждёт, когда очередь заполнена
            SingleWriter = true,
        });

        var producer = Task.Run(async () =>
        {
            for (var i = 1; i <= 8; i++)
            {
                await channel.Writer.WriteAsync(i);
                Log.Info($"producer: записал {i}");
            }
            channel.Writer.Complete();
            Log.Info("producer: Complete()");
        });

        var consumers = Enumerable.Range(1, 2).Select(id => Task.Run(async () =>
        {
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                await Task.Delay(200); // медленная обработка
                Log.Info($"    consumer {id}: обработал {item}");
            }
            Log.Info($"    consumer {id}: канал закрыт, выхожу");
        })).ToArray();

        await Task.WhenAll([producer, .. consumers]);

        Log.Note("Producer быстро записал 5 элементов (2 сразу забрали consumers + 3 в буфере), а дальше ждал свободного места — это backpressure: " +
                 "память не растёт бесконечно. BlockingCollection блокирует потоки, Channel ждёт асинхронно. " +
                 "Writer.Complete(exception) передаёт ошибку читателям. Unbounded канал — риск OutOfMemory.");
    }
}
