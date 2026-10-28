using System.Runtime.CompilerServices;

namespace CSharp.Delegates.Demos;

public sealed class EventsDemo : IDemo
{
    public string Title => "События: event vs поле-делегат, утечки памяти, отписка";

    public string Question =>
        "Чем event отличается от публичного поля-делегата? Как подписка на событие приводит к утечке памяти? Как правильно отписываться?";

    public Task RunAsync()
    {
        Log.Section("1. Публичное поле-делегат: внешний код может всё");
        var fieldPublisher = new FieldPublisher();
        fieldPublisher.Changed += value => Log.Info($"  подписчик 1: {value}");
        fieldPublisher.Changed += value => Log.Info($"  подписчик 2: {value}");
        fieldPublisher.Changed = value => Log.Bad($" чужой код затёр всех подписчиков и получил: {value}");
        fieldPublisher.Changed("обновление от издателя");
        fieldPublisher.Changed("подделка: событие 'поднял' внешний код");

        Log.Info("event: снаружи доступны только += и -=");
        var eventPublisher = new EventPublisher();
        eventPublisher.Changed += (_, value) => Log.Info($"  подписчик 1: {value}");
        eventPublisher.Changed += (_, value) => Log.Info($"  подписчик 2: {value}");
        // eventPublisher.Changed = null;                   // CS0070: только += / -=
        // eventPublisher.Changed(this, "подделка");        // CS0070: вызвать может только сам класс
        eventPublisher.Raise("обновление от издателя");
        Log.Note("event = приватное поле-делегат + методы add/remove. Он защищает инкапсуляцию: подписчики не могут " +
                 "стереть друг друга или 'поднять' событие от имени издателя.");

        Log.Section("2. Утечка памяти: издатель держит подписчиков");
        var publisher = new LongLivedPublisher();
        var leaked = CreateSubscriber(publisher, unsubscribe: false);
        var released = CreateSubscriber(publisher, unsubscribe: true);
        ForceGc();
        Log.Bad($"без отписки: подписчик (10 МБ) жив после GC = {leaked.IsAlive}");
        Log.Good($"с отпиской в Dispose: жив = {released.IsAlive}");
        Log.Info($"у издателя подписчиков: {publisher.SubscriberCount}");
        Log.Note("Делегат хранит ссылку на Target — объект подписчика. Значит, издатель держит подписчика, а не наоборот. " +
                 "Если издатель живёт долго (singleton, статическое событие, шина событий), а подписчики — коротко " +
                 "(view model, scoped-сервис), они никогда не соберутся. Отписывайтесь в Dispose.");
        GC.KeepAlive(publisher);

        Log.Section("3. Подписка как IDisposable-токен");
        var bus = new MessageBus();
        using (bus.Subscribe(message => Log.Info($"  получено: {message}")))
        {
            bus.Publish("первое сообщение");
        }
        bus.Publish("второе сообщение (подписки уже нет)");
        Log.Info($"подписчиков: {bus.SubscriberCount}");
        Log.Note("Так устроены IObservable.Subscribe, IOptionsMonitor.OnChange, CancellationToken.Register: " +
                 "отписка — это Dispose, её трудно забыть и удобно использовать в using.");

        Log.Section("4. Собственные add/remove и безопасный вызов");
        var audited = new AuditedPublisher();
        EventHandler handler = (_, _) => Log.Info("  обработчик вызван");
        audited.Changed += handler;
        audited.Raise();
        audited.Changed -= handler;
        Log.Note("Для обычного (field-like) события компилятор генерирует потокобезопасные add/remove (цикл Interlocked.CompareExchange). " +
                 "Вызывать событие — Changed?.Invoke(this, e): поле читается один раз. " +
                 "Старый вариант 'if (Changed != null) Changed(this, e)' — гонка: между проверкой и вызовом могут отписаться → NullReferenceException. " +
                 "Async-лямбда в обработчике события — это async void: исключение уронит процесс.");

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateSubscriber(LongLivedPublisher publisher, bool unsubscribe)
    {
        var subscriber = new Subscriber(publisher);
        if (unsubscribe)
            subscriber.Dispose();
        return new WeakReference(subscriber);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class FieldPublisher
    {
        public Action<string>? Changed;
    }

    private sealed class EventPublisher
    {
        public event EventHandler<string>? Changed;

        public void Raise(string value) => Changed?.Invoke(this, value);
    }

    private sealed class LongLivedPublisher
    {
        public event EventHandler? Tick;

        public int SubscriberCount => Tick?.GetInvocationList().Length ?? 0;
    }

    private sealed class Subscriber : IDisposable
    {
        private readonly LongLivedPublisher _publisher;
        private readonly byte[] _payload = new byte[10 * 1024 * 1024];

        public Subscriber(LongLivedPublisher publisher)
        {
            _publisher = publisher;
            _publisher.Tick += OnTick;
        }

        private void OnTick(object? sender, EventArgs e) => _ = _payload.Length;

        public void Dispose() => _publisher.Tick -= OnTick;
    }

    private sealed class MessageBus
    {
        private Action<string>? _handlers;

        public int SubscriberCount => _handlers?.GetInvocationList().Length ?? 0;

        public IDisposable Subscribe(Action<string> handler)
        {
            _handlers += handler;
            return new Subscription(() => _handlers -= handler);
        }

        public void Publish(string message) => _handlers?.Invoke(message);

        private sealed class Subscription(Action unsubscribe) : IDisposable
        {
            private Action? _unsubscribe = unsubscribe;

            // Повторный Dispose ничего не делает.
            public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }

    private sealed class AuditedPublisher
    {
        private EventHandler? _changed;

        public event EventHandler Changed
        {
            add
            {
                Log.Info("  add: новый подписчик");
                _changed += value;
            }
            remove
            {
                Log.Info("  remove: подписчик ушёл");
                _changed -= value;
            }
        }

        public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
    }
}
