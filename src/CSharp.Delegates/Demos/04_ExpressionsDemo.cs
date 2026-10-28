using System.Diagnostics;
using System.Linq.Expressions;
using CSharp.Delegates.Expressions;

namespace CSharp.Delegates.Demos;

public sealed record User(int Id, string Name, int Age, string City);

public sealed class ExpressionsDemo : IDemo
{
    public string Title => "Expression<Func<T>> vs Func<T>: как EF Core переводит лямбды в SQL";

    public string Question =>
        "Чем Expression<Func<T, bool>> отличается от Func<T, bool>? Почему EF Core может перевести первое в SQL, а второе — нет? " +
        "Что будет, если передать Func в IQueryable.Where?";

    private static readonly User[] Users =
    [
        new(1, "Anna", 25, "Moscow"),
        new(2, "Boris", 17, "Kazan"),
        new(3, "Alex", 31, "Moscow"),
        new(4, "Olga", 42, "Sochi"),
    ];

    public Task RunAsync()
    {
        Log.Section("1. Func — скомпилированный код, Expression — данные (дерево)");
        Func<User, bool> func = u => u.Age >= 18;
        Expression<Func<User, bool>> expression = u => u.Age >= 18;

        Log.Info($"Func.ToString():       {func}  (Method = {func.Method.Name} — это просто IL, внутрь не заглянуть)");
        Log.Info($"Expression.ToString(): {expression}");
        Log.Info("дерево выражения:");
        PrintTree(expression);
        Log.Note("Для Expression<...> компилятор не генерирует IL, а строит объекты: Lambda → Binary(GreaterThanOrEqual) → Member(Age) + Constant(18). " +
                 "Это дерево можно обойти и перевести во что угодно — например, в SQL.");

        Log.Section("2. Мини-версия того, что делает EF Core: обход дерева → SQL");
        var minAge = 18;
        var city = "Moscow";
        Expression<Func<User, bool>> filter = u => u.Age >= minAge && u.City == city && u.Name.StartsWith("A");
        PrintSql(filter);
        Log.Good($"тот же фильтр в памяти через Compile(): [{string.Join(", ", Users.Where(filter.Compile()).Select(u => u.Name))}]");
        Log.Note("Захваченные переменные (minAge, city) в дереве — доступ к полю DisplayClass. Транслятор вычисляет их значение " +
                 "и превращает в параметры @p0, @p1 (как EF Core: план запроса кэшируется, нет SQL-инъекций). Литерал 18 подставлен как есть.");

        Log.Section("3. Произвольный C#-метод в SQL не переводится");
        Expression<Func<User, bool>> custom = u => IsAdult(u);
        try
        {
            PrintSql(custom);
        }
        catch (NotSupportedException ex)
        {
            Log.Bad($"{ex.GetType().Name}: {ex.Message}");
        }
        Log.Note("В дереве — лишь вызов метода IsAdult, его тела там нет. EF Core в такой ситуации бросает " +
                 "'The LINQ expression ... could not be translated' (с EF Core 3.0 без тихого выполнения на клиенте).");

        Log.Section("4. Func в IQueryable.Where: тихо уходим в память");
        IQueryable<User> query = Users.AsQueryable();
        var withExpression = query.Where(u => u.Age >= 18);
        Func<User, bool> predicate = u => u.Age >= 18;
        var withFunc = query.Where(predicate);

        Log.Good($"Where(лямбда):  IQueryable = {withExpression is IQueryable<User>}, Expression = {withExpression.Expression}");
        Log.Bad($" Where(Func):    IQueryable = {withFunc is IQueryable<User>}, тип = {withFunc.GetType().Name}");
        Log.Note("Лямбда приводится к Expression → Queryable.Where, провайдер строит SQL. Переменная Func → Enumerable.Where: " +
                 "в EF Core это SELECT всей таблицы и фильтрация в памяти. То же — если запрос сохранён в IEnumerable<T> " +
                 "или вызван AsEnumerable() раньше времени: перегрузку выбирает СТАТИЧЕСКИЙ тип.");

        Log.Section("5. Динамические фильтры: склейка выражений");
        Expression<Func<User, bool>> adult = u => u.Age >= 18;
        Expression<Func<User, bool>> fromMoscow = x => x.City == "Moscow";

        var naive = Expression.Lambda<Func<User, bool>>(Expression.AndAlso(adult.Body, fromMoscow.Body), adult.Parameters);
        try
        {
            naive.Compile();
        }
        catch (InvalidOperationException ex)
        {
            Log.Bad($"наивная склейка тел: {ex.Message}");
        }

        var combined = adult.AndAlso(fromMoscow);
        Log.Good($"с заменой параметра: {combined}");
        PrintSql(combined);
        Log.Note("У каждой лямбды свой ParameterExpression (u и x — разные объекты). Нужен ExpressionVisitor, " +
                 "заменяющий параметр, — так устроены specification pattern и PredicateBuilder (LinqKit).");

        Log.Section("6. Compile() дорогой — кэшируйте делегат");
        const int compilations = 1_000;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < compilations; i++)
            _ = expression.Compile();
        var compileMs = sw.Elapsed.TotalMilliseconds;

        var compiled = expression.Compile();
        sw.Restart();
        var matches = 0;
        for (var i = 0; i < 1_000_000; i++)
            if (compiled(Users[i % Users.Length]))
                matches++;
        Log.Info($"{compilations:N0} × Compile(): {compileMs:F0} мс (~{compileMs * 1000 / compilations:F0} мкс на штуку); " +
                 $"1 000 000 вызовов готового делегата: {sw.Elapsed.TotalMilliseconds:F0} мс");
        Log.Note("Compile() генерирует IL через Reflection.Emit. Вызывать его на каждый запрос нельзя — кэшируйте результат. " +
                 "Ограничения деревьев: только лямбда-выражения (не блок { }), без async, присваиваний, ?. и throw-выражений.");

        return Task.CompletedTask;
    }

    private static bool IsAdult(User user) => user.Age >= 18;

    private static void PrintSql(Expression<Func<User, bool>> predicate)
    {
        var (sql, parameters) = SqlTranslator.Translate(predicate);
        Log.Info($"SQL: {sql}");
        if (parameters.Count > 0)
            Log.Info($"параметры: {string.Join(", ", parameters.Select(p => $"{p.Key} = {p.Value}"))}");
    }

    private static void PrintTree(Expression node, int depth = 1)
    {
        var indent = new string(' ', depth * 2);
        switch (node)
        {
            case LambdaExpression lambda:
                Log.Code($"{indent}Lambda ({string.Join(", ", lambda.Parameters.Select(p => p.Name))})");
                PrintTree(lambda.Body, depth + 1);
                break;
            case BinaryExpression binary:
                Log.Code($"{indent}{binary.NodeType}");
                PrintTree(binary.Left, depth + 1);
                PrintTree(binary.Right, depth + 1);
                break;
            case MemberExpression member:
                Log.Code($"{indent}MemberAccess .{member.Member.Name}");
                if (member.Expression is not null)
                    PrintTree(member.Expression, depth + 1);
                break;
            case ParameterExpression parameter:
                Log.Code($"{indent}Parameter {parameter.Name} : {parameter.Type.Name}");
                break;
            case ConstantExpression constant:
                Log.Code($"{indent}Constant {constant.Value} : {constant.Type.Name}");
                break;
            default:
                Log.Code($"{indent}{node.NodeType}: {node}");
                break;
        }
    }
}
