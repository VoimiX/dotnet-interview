using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace CSharp.Delegates.Expressions;

/// <summary>
/// Учебный транслятор Expression → SQL WHERE. Принцип тот же, что у EF Core:
/// ExpressionVisitor обходит дерево, колонки берутся из обращений к параметру лямбды,
/// захваченные переменные вычисляются и становятся параметрами запроса.
/// </summary>
public sealed class SqlTranslator : ExpressionVisitor
{
    private readonly StringBuilder _sql = new();
    private readonly List<KeyValuePair<string, object?>> _parameters = [];

    public static (string Sql, IReadOnlyList<KeyValuePair<string, object?>> Parameters) Translate<T>(Expression<Func<T, bool>> predicate)
    {
        var translator = new SqlTranslator();
        translator.Visit(predicate.Body);
        return ($"SELECT * FROM {typeof(T).Name}s WHERE {translator._sql}", translator._parameters);
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        var op = node.NodeType switch
        {
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " <> ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",
            _ => throw new NotSupportedException($"Оператор {node.NodeType} не переводится в SQL"),
        };

        _sql.Append('(');
        Visit(node.Left);
        _sql.Append(op);
        Visit(node.Right);
        _sql.Append(')');
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ParameterExpression)
        {
            _sql.Append(node.Member.Name); // u.Age → колонка Age
            return node;
        }

        AppendParameter(Evaluate(node)); // захваченная переменная → @pN
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        _sql.Append(FormatLiteral(node.Value));
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(string) && node.Object is not null && node.Arguments.Count == 1)
        {
            var pattern = node.Method.Name switch
            {
                nameof(string.StartsWith) => "{0}%",
                nameof(string.EndsWith) => "%{0}",
                nameof(string.Contains) => "%{0}%",
                _ => null,
            };

            if (pattern is not null)
            {
                Visit(node.Object);
                _sql.Append(" LIKE ");
                AppendParameter(string.Format(CultureInfo.InvariantCulture, pattern, Evaluate(node.Arguments[0])));
                return node;
            }
        }

        throw new NotSupportedException($"Метод {node.Method.DeclaringType?.Name}.{node.Method.Name} не переводится в SQL");
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        switch (node.NodeType)
        {
            case ExpressionType.Not:
                _sql.Append("NOT ");
                Visit(node.Operand);
                return node;
            case ExpressionType.Convert:
                Visit(node.Operand);
                return node;
            default:
                throw new NotSupportedException($"Оператор {node.NodeType} не переводится в SQL");
        }
    }

    private void AppendParameter(object? value)
    {
        var name = $"@p{_parameters.Count}";
        _parameters.Add(new KeyValuePair<string, object?>(name, value));
        _sql.Append(name);
    }

    private static object? Evaluate(Expression expression) => expression switch
    {
        ConstantExpression constant => constant.Value,
        MemberExpression { Expression: ConstantExpression closure, Member: FieldInfo field } => field.GetValue(closure.Value),
        MemberExpression { Expression: ConstantExpression closure, Member: PropertyInfo property } => property.GetValue(closure.Value),
        _ => Expression.Lambda(expression).Compile().DynamicInvoke(),
    };

    private static string FormatLiteral(object? value) => value switch
    {
        null => "NULL",
        string text => $"'{text.Replace("'", "''")}'",
        bool flag => flag ? "1" : "0",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL",
    };
}
