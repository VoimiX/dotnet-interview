# DotNetInterview: примеры для собеседования Senior .NET Developer

.NET 10, решение `DotNetInterview.slnx`, пять проектов:

| Проект | Что внутри |
|---|---|
| `src/Async.Console` | 18 демо: каждое отвечает на типичный вопрос с собеседования, выводит ID потока и время и объясняет результат |
| `src/Async.Web` | ASP.NET Core (Minimal API): то, что проявляется только на сервере, — starvation, RequestAborted, fire-and-forget и DI scope, BackgroundService, стриминг, cache stampede |
| `src/AspNetCore.Pipeline` | Конвейер ASP.NET Core: middleware, MVC-фильтры и endpoint-фильтры minimal API. Каждый ответ содержит трассировку — в каком порядке отработали middleware и фильтры |
| `src/CSharp.Delegates` | 4 демо: лямбды и замыкания, делегаты, события, `Expression<Func<T>>` против `Func<T>` с мини-транслятором в SQL |
| `src/Threading.Synchronization` | 5 демо: `lock`/`Monitor`, `Interlocked` и lock-free, `volatile` и модель памяти, `ReaderWriterLockSlim`, deadlock |

Все консольные проекты запускаются одинаково: `dotnet run` — меню, `dotnet run -- 3` — одно демо, `dotnet run -- all` — все.

## Запуск

```bash
cd src/Async.Console
dotnet run            # интерактивное меню
dotnet run -- 14      # одно демо (thread pool starvation)
dotnet run -- all     # все подряд (~40 с, большую часть времени занимает starvation)
```

```bash
cd src/Async.Web
dotnet run            # http://localhost:5145, список эндпоинтов на "/"
```

```bash
cd src/AspNetCore.Pipeline
dotnet run            # http://localhost:5200, список эндпоинтов на "/"
```

Запросы собраны в `requests.http` каждого веб-проекта (их можно запускать из Rider или VS/VS Code).

## Лямбды, делегаты, события, деревья выражений (`CSharp.Delegates`)

| # | Тема | Что показано |
|---|---|---|
| 1 | Замыкания | Захват переменной, а не значения; `<>c__DisplayClass` и кэшируемый `<>c` (через reflection); маленькая лямбда удерживает 50 МБ соседа по scope; `for` против `foreach`; 88 байт на вызов с захватом против 0 у `static`-лямбды со state; скрытая аллокация на невыполняемой ветке; локальная функция без аллокаций |
| 2 | Делегаты | `Target` + `Method`, `Func<int,bool>` ≠ `Predicate<int>`; multicast `Func` возвращает последний результат; исключение обрывает цепочку; неизменяемость; `-=` с лямбдой не отписывает; вариантность `out`/`in` |
| 3 | События | Поле-делегат можно затереть и вызвать снаружи, `event` — нет; утечка подписчика (10 МБ живут после GC), отписка в `Dispose`; подписка как `IDisposable`; свои `add`/`remove` |
| 4 | Expression vs Func | Дерево выражения; транслятор `ExpressionVisitor` → SQL, захваченные переменные → параметры `@p0`; «could not be translated»; `Func` в `IQueryable.Where` уходит в `Enumerable.Where`; склейка предикатов с заменой параметра; цена `Compile()` (~75 мкс) |

## Многопоточность без async (`Threading.Synchronization`)

| # | Тема | Что показано |
|---|---|---|
| 1 | lock и Monitor | `counter++` теряет половину обновлений; lock (390 мс) против `Interlocked` (66 мс); во что компилируется lock; реентерабельность и привязка к потоку; lock на интернированной строке и на упакованном `int`; `Monitor.Wait`/`PulseAll` |
| 2 | Interlocked | Семантика возвращаемых значений; CAS-цикл с подсчётом повторов; copy-on-write через `ImmutableInterlocked`; `CompareExchange` создаёт объект 8 раз, `Lazy<T>` — 1 |
| 3 | Модель памяти | JIT выносит чтение поля из цикла — поток не видит флаг, с `volatile` видит; litmus-тест store→load (сотни перестановок без барьера, 0 с барьером); double-checked locking |
| 4 | ReaderWriterLockSlim | 4 параллельных читателя против 1 у lock; upgradeable read; `LockRecursionException`, привязка к потоку |
| 5 | Deadlock | Встречные переводы между счетами; решения: порядок блокировок и `TryEnter` с повтором; deadlock из-за события под lock; условия Коффмана и диагностика `dotnet-dump` |

В `Threading.Synchronization` оптимизации JIT включены и в Debug (`<Optimize>true</Optimize>`): иначе демо с `volatile` не воспроизводится.

## Конвейер ASP.NET Core (`AspNetCore.Pipeline`)

Самый внешний `TraceMiddleware` буферизует ответ и возвращает его в обёртке: `trace` — по шагам, кто и в каком порядке обработал запрос, `response` — исходный ответ. `?raw=1` отключает обёртку.

**Middleware**

| Эндпоинт | Что показывает |
|---|---|
| `GET /middleware/order` | «Луковица»: код до `next` — на пути запроса, после — на пути ответа в обратном порядке |
| `GET /branch/usewhen`, `GET /mapwhen/x` | `UseWhen` возвращается в основной конвейер, `MapWhen` — нет |
| `GET /secure/data` | Short-circuit: без `X-Api-Key: demo-key` middleware отвечает 401 и не вызывает `next` |
| `GET /middleware/lifetimes` | Convention-middleware создаётся один раз (scoped — только параметром `InvokeAsync`), `IMiddleware` — из DI на каждый запрос |
| `GET /middleware/response-started` | После начала ответа заголовки и статус менять нельзя; правильно — `Response.OnStarting` (`?mode=right`) |
| `POST /middleware/echo` | Повторное чтение тела: `EnableBuffering` + `Position = 0`; с `?buffering=false` эндпоинт получает пустое тело |
| `GET /middleware/throw` | `UseExceptionHandler` + `IExceptionHandler` (.NET 8) → ProblemDetails |
| `GET /middleware/rate-limited` | Встроенный rate limiter: третий запрос за 10 с — 429, эндпоинт не вызывается |

**MVC-фильтры**

| Эндпоинт | Что показывает |
|---|---|
| `GET /filters/order` | Authorization → Resource → [model binding] → Action → [action] → Result; внутри типа: global → controller → action |
| `GET /filters/order-override` | `IOrderedFilter.Order = -1` важнее scope |
| `GET /filters/cached/1` | Resource-фильтр отдаёт ответ из кэша: model binding, action-фильтры и action пропускаются |
| `POST /filters/validate` | `[ApiController]`: невалидная модель → 400 до action-фильтров (`ModelStateInvalidFilter`, Order = -2000) |
| `GET /filters/throw-in-action` | Exception-фильтр превращает исключение action в 400 |
| `GET /filters/throw-in-result` | Исключение в result-фильтре exception-фильтры не видят → `GlobalExceptionHandler`, 500 |
| `GET /filters/short-circuit-action` | Action-фильтр задаёт `Result`: action не вызывается, result-фильтры выполняются |
| `GET /filters/forbidden` | Authorization-фильтр → 403; выполняется только `IAlwaysRunResultFilter` |
| `GET /filters/di` | `[ServiceFilter]` (из контейнера, нужна регистрация) и `[TypeFilter]` (через `ActivatorUtilities`, с аргументами) |

**Endpoint-фильтры minimal API:** `GET /minimal/order` (фильтр группы → A → B), `POST /minimal/orders` (валидация с short-circuit), `GET /minimal/wrap` (фильтр подменяет результат).

Что видно в трассировке и стоит сказать на собеседовании:
- Когда exception-фильтр выставил `ExceptionHandled = true`, остальные exception-фильтры уже не вызываются.
- `UseExceptionHandler` очищает ответ, включая заголовки: `X-Correlation-Id`, выставленный напрямую, в ответ с 500 не попадает. Если заголовок нужен и в ответе с ошибкой, его надо выставить заново, например в `IExceptionHandler`.
- Middleware работает для всех запросов и знает только `HttpContext`. Фильтры работают только для MVC и видят `ActionArguments`, `ModelState`, результат action.

## Консольные демо

| # | Тема | Ключевой вопрос |
|---|---|---|
| 1 | Основы | Создаёт ли async поток? Что такое state machine? I/O-bound и CPU-bound |
| 2 | async void | Почему его нельзя дождаться? Ловушки `List.ForEach(async ...)`, безопасный fire-and-forget |
| 3 | Исключения | `WhenAll` и несколько ошибок, `AggregateException`, когда бросается исключение, `UnobservedTaskException` |
| 4 | Eliding | `return Task` без `await` вместе с `using` или `try/catch` |
| 5 | Отмена | Кооперативная отмена, linked tokens, `WaitAsync` не отменяет операцию, `Register` |
| 6 | Комбинаторы | Последовательно и параллельно, `WhenAny` (hedging), `Task.WhenEach` (.NET 9) |
| 7 | Throttling | `SemaphoreSlim`, `Parallel.ForEachAsync`, ошибка с `Parallel.ForEach` и async |
| 8 | ValueTask | Аллокации, повторный `await` → `InvalidOperationException`, `IValueTaskSource` |
| 9 | TaskCompletionSource | Обёртка над событиями, `RunContinuationsAsynchronously` (инлайн-продолжения) |
| 10 | Синхронизация | `lock` и `await`, async mutex на `SemaphoreSlim`, AsyncLazy |
| 11 | IAsyncEnumerable | `[EnumeratorCancellation]`, `break` и `finally`, встроенный LINQ (.NET 10) |
| 12 | Channels | Producer/consumer, backpressure |
| 13 | AsyncLocal | ExecutionContext, `[ThreadStatic]`, поток значений «только вниз», `SuppressFlow` |
| 14 | Starvation | Sync-over-async на 64 задачах: ~0,5 с асинхронно против ~20 с с блокировкой |
| 15 | Новые API | `ConfigureAwaitOptions`, `PeriodicTimer`, `IAsyncDisposable`, `TimeProvider` |
| 16 | Task.Run vs StartNew | `Task<Task>` и `Unwrap`, ловушка `TaskScheduler.Current`, `LongRunning`, фальшивая асинхронность, захват переменной цикла, исключения, `Task.Run` в ASP.NET Core |
| 17 | Parallel.ForEach vs WhenAll | CPU- и I/O-нагрузка с замерами, ловушка async без `Task.Run`, ограничение параллелизма, поведение при ошибках, итоговая таблица |
| 18 | Задачки | «Что выведет код?»: порядок выполнения, `StartNew` и `Task<Task>`, ленивый LINQ |

## Web-эндпоинты

| Эндпоинт | Что показывает |
|---|---|
| `GET /context` | `SynchronizationContext.Current == null`: в ASP.NET Core нет классического deadlock |
| `GET /starvation/load?mode=sync&n=200` | 200 запросов: sync ≈ 17 с, async ≈ 0,5 с |
| `GET /cancellation/slow` | `RequestAborted`: работа прекращается, когда клиент отключился |
| `GET /parallel/aggregate?timeoutMs=600` | `WhenAll`, общий таймаут через linked CTS, ответ 504 |
| `GET /stream/json`, `/stream/sse` | `IAsyncEnumerable` → потоковый JSON или Server-Sent Events |
| `POST /fire-and-forget/wrong` | `Task.Run` после ответа → `ObjectDisposedException` у DbContext и HttpContext |
| `POST /fire-and-forget/right` | Очередь на `Channel`, `BackgroundService`, собственный DI scope |
| `GET /fire-and-forget/parallel-dbcontext` | `WhenAll` на одном DbContext → ошибка |
| `GET /cache/stampede` | Наивный кэш: 50 вызовов фабрики; `Lazy<Task<T>>`: 1 вызов |
| `POST /sync-io/wrong` | Kestrel запрещает синхронный I/O |

## Шпаргалка ответов

- **async/await** — синтаксис над state machine. Сам по себе он не создаёт поток: метод выполняется синхронно до первого незавершённого `await`.
- **`.Result` / `.Wait()`** в ASP.NET Core не дают deadlock (нет `SynchronizationContext`), но держат поток пула → thread pool starvation. Лечение — async all the way. `ConfigureAwait(false)` в коде приложения ничего не меняет.
- **Task.Run** = `StartNew(..., DenyChildAttach, TaskScheduler.Default)` + `Unwrap`. У `StartNew` по умолчанию `TaskScheduler.Current`, а с async-лямбдой он возвращает `Task<Task>`. `Task.Run` поверх синхронного I/O — не асинхронность, а блокировка другого потока. В обработчике ASP.NET Core `Task.Run` ничего не ускоряет.
- **Parallel.ForEach** — для CPU-нагрузки: делит работу по ядрам и блокирует вызывающий поток, async-лямбды в нём нельзя. **Task.WhenAll** — для I/O: только ждёт уже запущенные задачи и не ограничивает их число. **Parallel.ForEachAsync** — асинхронный I/O с ограничением `MaxDegreeOfParallelism`.
- **async void** нельзя дождаться, а его исключение уходит прямо в `ThreadPool` и роняет процесс. Допустим только как обработчик события.
- **await Task.WhenAll** бросает одно исключение. Все ошибки лежат в `task.Exception.InnerExceptions`.
- **Отмена** кооперативная. `WaitAsync` и `WhenAny` с таймаутом перестают ждать, но саму операцию не отменяют.
- **ValueTask** нужен для горячих путей с частым синхронным результатом. Его можно await'ить только один раз; для `WhenAll` и кэширования используйте `.AsTask()`.
- **TaskCompletionSource** создавайте с `RunContinuationsAsynchronously` и завершайте через `TrySet*`.
- **lock** нельзя сочетать с `await`. Вместо него используйте `SemaphoreSlim(1,1)`, он не реентерабелен.
- **Thread pool starvation**: низкий CPU, растут `ThreadCount` и `QueueLength`, высокая latency. Диагностика: `dotnet-counters`, `dotnet-dump`.
- **BackgroundService** — singleton: scoped-сервисы получайте через `IServiceScopeFactory`. С .NET 6 необработанное исключение в нём останавливает хост.
- **DbContext** не потокобезопасен: никаких `WhenAll` на одном контексте; для параллельной работы используйте `IDbContextFactory`.
