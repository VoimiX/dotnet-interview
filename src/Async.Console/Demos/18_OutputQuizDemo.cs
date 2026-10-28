using System.Text;

namespace Async.Demos;

public sealed class OutputQuizDemo : IDemo
{
    public string Title => "Задачки 'что выведет этот код?'";

    public string Question => "Классические live-coding вопросы на порядок выполнения async-кода.";

    private sealed record Quiz(string Code, Func<StringBuilder, Task> Run, string Explanation);

    private static readonly Quiz[] Quizzes =
    [
        new(
            """
            async Task Outer()
            {
                Print("1");
                var t = Inner();
                Print("3");
                await t;
                Print("5");
            }
            async Task Inner() { Print("2"); await Task.Delay(10); Print("4"); }
            """,
            Quiz1,
            "Inner выполняется синхронно до первого await, затем управление возвращается в Outer."),

        new(
            """
            async Task Method() { Print("A"); await Task.CompletedTask; Print("B"); }

            var t = Method();
            Print("C");
            """,
            Quiz2,
            "await уже завершённой задачи не уступает управление — метод дорабатывает синхронно."),

        new(
            """
            await Task.Factory.StartNew(async () =>
            {
                await Task.Delay(100);
                Print("inner");
            });
            Print("after-await");
            """,
            Quiz3,
            "StartNew с async-лямбдой возвращает Task<Task>: await ждёт только запуск внешней задачи. " +
            "Используйте Task.Run (умеет разворачивать) или .Unwrap()."),

        new(
            """
            var calls = 0;
            var tasks = Enumerable.Range(1, 3).Select(async i =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(10);
                return i;
            });
            await Task.WhenAll(tasks);
            var first = await tasks.First();
            Print($"calls = {calls}");
            """,
            Quiz4,
            "LINQ ленивый: каждый перебор tasks заново запускает операции. Материализуйте: .ToArray()/.ToList()."),

        new(
            """
            // Консоль или ASP.NET Core: будет ли deadlock?
            async Task<string> GetAsync() { await Task.Delay(50); return "ok"; }
            Print(GetAsync().Result);
            """,
            Quiz5,
            "Deadlock не будет: SynchronizationContext нет, продолжение выполнится в ThreadPool. " +
            "Но поток заблокирован на всё время ожидания — под нагрузкой это thread pool starvation (демо 14)."),
    ];

    public async Task RunAsync()
    {
        for (var i = 0; i < Quizzes.Length; i++)
        {
            var quiz = Quizzes[i];
            Log.Section($"Задачка {i + 1}");
            Log.Code(quiz.Code);

            var output = new StringBuilder();
            await quiz.Run(output);

            Log.Good($"Вывод: {output.ToString().Trim()}");
            Log.Note(quiz.Explanation);
        }
    }

    private static async Task Quiz1(StringBuilder sb)
    {
        sb.Append("1 ");
        var t = Inner();
        sb.Append("3 ");
        await t;
        sb.Append("5 ");

        async Task Inner()
        {
            sb.Append("2 ");
            await Task.Delay(10);
            sb.Append("4 ");
        }
    }

    private static Task Quiz2(StringBuilder sb)
    {
        _ = Method();
        sb.Append("C ");
        return Task.CompletedTask;

        async Task Method()
        {
            sb.Append("A ");
            await Task.CompletedTask;
            sb.Append("B ");
        }
    }

    private static async Task Quiz3(StringBuilder sb)
    {
        await Task.Factory.StartNew(async () =>
        {
            await Task.Delay(100);
            lock (sb)
                sb.Append("inner ");
        });
        lock (sb)
            sb.Append("after-await ");
        await Task.Delay(200);
    }

    private static async Task Quiz4(StringBuilder sb)
    {
        var calls = 0;
        var tasks = Enumerable.Range(1, 3).Select(async i =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(10);
            return i;
        });
        await Task.WhenAll(tasks);
        _ = await tasks.First();
        sb.Append($"calls = {calls}");
    }

    private static Task Quiz5(StringBuilder sb)
    {
        sb.Append(GetAsync().Result);
        return Task.CompletedTask;

        static async Task<string> GetAsync()
        {
            await Task.Delay(50);
            return "ok";
        }
    }
}
