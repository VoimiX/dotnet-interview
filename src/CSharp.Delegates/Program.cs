using System.Text;
using CSharp.Delegates;
using CSharp.Delegates.Demos;

Console.OutputEncoding = Encoding.UTF8;

IDemo[] demos =
[
    new ClosuresDemo(),
    new DelegatesDemo(),
    new EventsDemo(),
    new ExpressionsDemo(),
];

// dotnet run -- 4     → запустить демо №4
// dotnet run -- all   → запустить все
if (args.Length > 0)
{
    await RunSelectionAsync(args[0]);
    return;
}

while (true)
{
    PrintMenu();
    Console.Write("Номер демо (all — все, q — выход): ");
    var input = Console.ReadLine()?.Trim();
    if (input is null || input.Equals("q", StringComparison.OrdinalIgnoreCase))
        return;

    await RunSelectionAsync(input);
}

void PrintMenu()
{
    Console.WriteLine();
    for (var i = 0; i < demos.Length; i++)
        Console.WriteLine($"{i + 1,2}. {demos[i].Title}");
}

async Task RunSelectionAsync(string selection)
{
    if (selection.Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var demo in demos)
            await RunDemoAsync(demo);
        return;
    }

    if (int.TryParse(selection, out var number) && number >= 1 && number <= demos.Length)
    {
        await RunDemoAsync(demos[number - 1]);
        return;
    }

    Console.WriteLine($"Неизвестный выбор: {selection}");
}

static async Task RunDemoAsync(IDemo demo)
{
    Log.Header(demo.Title, demo.Question);
    try
    {
        await demo.RunAsync();
    }
    catch (Exception ex)
    {
        Log.Bad($"Демо завершилось исключением: {ex}");
    }
}
