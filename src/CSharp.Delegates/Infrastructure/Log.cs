using System.Diagnostics;

namespace CSharp.Delegates;

/// <summary>
/// Потокобезопасный вывод: каждая строка содержит время с начала запуска и ID потока,
/// чтобы было видно, где выполняется код до и после await.
/// </summary>
public static class Log
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Lock Gate = new();

    public static void Header(string title, string question)
    {
        lock (Gate)
        {
            Console.WriteLine();
            WriteColored(new string('=', 100), ConsoleColor.Magenta);
            WriteColored(title, ConsoleColor.Magenta);
            WriteColored($"Вопрос: {question}", ConsoleColor.DarkMagenta);
            WriteColored(new string('=', 100), ConsoleColor.Magenta);
        }
    }

    public static void Section(string title)
    {
        lock (Gate)
        {
            Console.WriteLine();
            WriteColored($"--- {title} ---", ConsoleColor.Cyan);
        }
    }

    public static void Info(string message) => Write(message, ConsoleColor.Gray);

    public static void Good(string message) => Write($"✔ {message}", ConsoleColor.Green);

    public static void Bad(string message) => Write($"✘ {message}", ConsoleColor.Red);

    /// <summary>Пояснение "почему так" — то, что стоит сказать на собеседовании.</summary>
    public static void Note(string message)
    {
        lock (Gate)
            WriteColored($"  💡 {message}", ConsoleColor.Yellow);
    }

    public static void Code(string code)
    {
        lock (Gate)
            WriteColored(code, ConsoleColor.DarkGray);
    }

    private static void Write(string message, ConsoleColor color)
    {
        var line = $"[{Clock.Elapsed.TotalMilliseconds,8:F0} ms | T{Environment.CurrentManagedThreadId,-3}] {message}";
        lock (Gate)
            WriteColored(line, color);
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
