using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Birko.Data.SQL.Conditions;

namespace Birko.Data.SQL
{
    public static partial class DataBase
    {
        /// <summary>
        /// TASK-860 — the calls the SQL parsers translate by rewriting them into nodes they already have, before
        /// <c>ExpressionNormalizer</c> runs: <c>GetValueOrDefault()</c> / <c>GetValueOrDefault(d)</c> into <c>??</c>,
        /// <c>a.Equals(b)</c> (the <c>IEquatable&lt;T&gt;</c> overload) and <c>string.Equals(a, b)</c> into <c>==</c>, and a
        /// <c>bool</c> XOR into <c>!=</c>. Only nodes that read the row are touched; a parameter-free call is funcletized.
        /// </summary>
        /// <remarks>
        /// Each is exact: <c>GetValueOrDefault</c> is defined as <c>?? default</c>, <c>IEquatable&lt;T&gt;.Equals</c> as <c>==</c>
        /// for every type the mapping has (string is ordinal in both), and on a non-nullable <c>bool</c> <c>a ^ b</c> is
        /// <c>a != b</c>, which TASK-863 expands. Before (measured on all four providers): the parser dropped
        /// <c>GetValueOrDefault</c> and compared the bare column, so <c>x.Score.GetValueOrDefault() != 5</c> missed every NULL row,
        /// and <c>x.Hits.Equals(x.Score.Value)</c> matched nothing. <c>Equals(object)</c> and a lifted <c>bool?</c> XOR are not
        /// rewritten, and the parser refuses them. A string <c>Equals</c> with a <see cref="StringComparison"/> becomes the
        /// static <c>string.Equals(row, value, comparison)</c>, which the parser translates or refuses (TASK-869).
        /// </remarks>
        internal static Expression RewriteTranslatableCalls(Expression body) => new TranslatableCallRewriter().Visit(body)!;

        private sealed class TranslatableCallRewriter : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                if (!ContainsParameter(node)) return visited;
                var method = visited.Method;

                if (method.Name == nameof(Nullable<int>.GetValueOrDefault) && visited.Object != null
                    && Nullable.GetUnderlyingType(visited.Object.Type) is { } underlying)
                {
                    var fallback = visited.Arguments.Count == 1
                        ? visited.Arguments[0]
                        : Expression.Constant(Activator.CreateInstance(underlying), underlying);
                    return Expression.Coalesce(visited.Object, fallback);
                }

                if (method.Name == nameof(object.Equals) && method.ReturnType == typeof(bool))
                {
                    var parameters = method.GetParameters();
                    // a.Equals(b) — the typed overload only: Equals(object) boxes, and a StringComparison overload is a
                    // case or culture rule SQL equality does not have.
                    if (!method.IsStatic && visited.Object != null && parameters.Length == 1
                        && parameters[0].ParameterType == visited.Object.Type)
                        return Expression.Equal(visited.Object, visited.Arguments[0]);
                    // x.Opt.Equals(v) on a Nullable<T>, and x.Status.Equals(St.Open) on an enum, box v; when v has the
                    // receiver's own type that is ==, the lifted one for a nullable (null == null included). Enum.Equals
                    // compares the type too, so a boxed value of any other type is left alone, and refused.
                    if (!method.IsStatic && visited.Object != null && parameters.Length == 1
                        && parameters[0].ParameterType == typeof(object)
                        && visited.Arguments[0] is UnaryExpression { NodeType: ExpressionType.Convert } boxed
                        && (Nullable.GetUnderlyingType(visited.Object.Type) ?? visited.Object.Type) is { } valueType
                        && (valueType != visited.Object.Type || valueType.IsEnum)
                        && (boxed.Operand.Type == valueType || boxed.Operand.Type == visited.Object.Type))
                        return EqualValues(visited.Object, boxed.Operand);
                    if (method.IsStatic && method.DeclaringType == typeof(string) && parameters.Length == 2
                        && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string))
                        return Expression.Equal(visited.Arguments[0], visited.Arguments[1]);
                    // TASK-869: a.Equals(b, comparison) is string.Equals(a, b, comparison), with the row's side first, so the
                    // parser has one shape. Static, a null receiver is false rather than a NullReferenceException.
                    if (method.DeclaringType == typeof(string) && IsStringComparisonEquals(method))
                    {
                        var (a, b, comparison) = method.IsStatic
                            ? (visited.Arguments[0], visited.Arguments[1], visited.Arguments[2])
                            : (visited.Object!, visited.Arguments[0], visited.Arguments[1]);
                        if (!ContainsParameter(a) && ContainsParameter(b)) (a, b) = (b, a);
                        return Expression.Call(StaticStringEqualsWithComparison, a, b, comparison);
                    }
                }

                // Contains / StartsWith / EndsWith with a char is the string overload with a one-character string. The char
                // used to be bound as itself, which no provider compares as text (measured: Contains('b') matched only "B").
                if (method.DeclaringType == typeof(string) && !method.IsStatic && visited.Object != null
                    && method.Name is "Contains" or "StartsWith" or "EndsWith"
                    && visited.Arguments.Count > 0 && visited.Arguments[0].Type == typeof(char))
                {
                    var types = method.GetParameters().Select(p => p.ParameterType == typeof(char) ? typeof(string) : p.ParameterType).ToArray();
                    var overload = typeof(string).GetMethod(method.Name, types);
                    if (overload != null)
                    {
                        var arguments = visited.Arguments.ToArray();
                        arguments[0] = Expression.Call(arguments[0], typeof(char).GetMethod(nameof(char.ToString), Type.EmptyTypes)!);
                        return Expression.Call(visited.Object, overload, arguments);
                    }
                }
                return visited;
            }

            /// <summary><c>a == b</c> for values of one type; an enum (or nullable enum) is compared as its integer.</summary>
            private static Expression EqualValues(Expression left, Expression right)
            {
                var type = left.Type;
                var underlying = Nullable.GetUnderlyingType(type) ?? type;
                if (right.Type != type) right = Expression.Convert(right, type);
                if (!underlying.IsEnum) return Expression.Equal(left, right);
                var integer = Enum.GetUnderlyingType(underlying);
                var target = type == underlying ? integer : typeof(Nullable<>).MakeGenericType(integer);
                return Expression.Equal(Expression.Convert(left, target), Expression.Convert(right, target));
            }

            protected override Expression VisitBinary(BinaryExpression node)
            {
                var visited = base.VisitBinary(node);
                if (visited is BinaryExpression { NodeType: ExpressionType.ExclusiveOr } xor
                    && xor.Type == typeof(bool) && xor.Left.Type == typeof(bool) && xor.Right.Type == typeof(bool)
                    && ContainsParameter(node))
                    return Expression.NotEqual(xor.Left, xor.Right);
                return visited;
            }
        }

        /// <summary>
        /// TASK-860 — refuses a conversion that is not exact in SQL: a narrowing or lossy numeric cast (<c>(int)x.Amount</c>
        /// truncates in C#, rounds or keeps the fraction in SQL), a conversion to or from <c>char</c> or <c>bool</c>, an
        /// unboxing. A nullable lift, an enum to its integer, a widening integer cast and a reference conversion are
        /// transparent; an integer to <c>decimal</c> or <c>double</c> is rendered as an explicit cast inside a value fragment
        /// (<see cref="SqlValueFragmentDialect.Conversion"/>), so no division of two integers happens where C# divides
        /// fractions. Before, every conversion was dropped.
        /// </summary>
        private static void RequireTranslatableConversion(Type from, Type to)
        {
            if (SqlConversion.Classify(from, to) != SqlConversionKind.Lossy) return;
            throw new NotSupportedException(
                $"Cannot translate the conversion from {Describe(from)} to {Describe(to)} over the row to SQL: SQL has no form "
                + "of it that gives C#'s result on every provider (C# truncates a fraction and wraps an integer that overflows; "
                + "a char or bool is no number in a column). Before this was refused the conversion was dropped and the "
                + "unconverted value compared. Compare the column itself, or convert the value you compare it with.");
        }

        private static string Describe(Type type)
            => Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

        /// <summary>
        /// TASK-860 — the operand of a comparison that reads the row must be one the parser translates: a column (through
        /// transparent conversions), a nullable's <c>.Value</c> / <c>.HasValue</c>, a column's <c>.Date</c>, a case fold
        /// (<c>ToLower()</c> / <c>ToUpper()</c> and their invariant forms), or a value fragment (arithmetic, <c>??</c>, a
        /// ternary, the <c>Math</c> calls TASK-857 translates). Anything else is refused by name. Before, the comparison
        /// parser recursed into it and kept whatever column it found: <c>x.Code.Substring(1) == "bc"</c> compared
        /// <c>Code</c>, <c>string.Compare(x.Code, "b") &gt; 0</c> compared <c>Code</c> with 0, <c>x.At.Year == 2026</c>
        /// failed with an unrelated error.
        /// </summary>
        private static void RequireTranslatableComparand(Expression operand, Type? exprType)
        {
            while (operand is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } conversion)
            {
                RequireTranslatableConversion(conversion.Operand.Type, conversion.Type);
                operand = conversion.Operand;
            }
            if (!ContainsParameter(operand) || IsValueExpressionOperand(operand)) return;

            switch (operand)
            {
                case MemberExpression member when TryResolveParameterColumn(member, exprType, out _):
                    return;
                case MemberExpression { Member.Name: "Value" or "HasValue", Expression: { } inner } nullable
                        when nullable.Member.ReflectedType != null && Nullable.GetUnderlyingType(nullable.Member.ReflectedType) != null:
                    RequireTranslatableComparand(inner, exprType);
                    return;
                case MemberExpression { Member.Name: "Date", Expression: { } dated } date when date.Member.DeclaringType == typeof(DateTime):
                    RequireTranslatableComparand(dated, exprType);
                    return;
                case MemberExpression member:
                    throw RefuseMember(member);
                case MethodCallExpression call when IsCaseFold(call):
                    RequireTranslatableComparand(call.Object!, exprType);
                    return;
                case MethodCallExpression call:
                    throw RefuseCall(call);
                default:
                    throw RefuseNode(operand);
            }
        }

        /// <summary>
        /// TASK-860 — a call over the row in predicate position, or reached from one, is one the condition parser translates:
        /// a string <c>StartsWith</c> / <c>EndsWith</c> / <c>Contains</c> on a column with a value pattern (LIKE), a set
        /// <c>Contains</c> of a value list (IN), or a case fold. Everything else is refused by name: before, its arguments
        /// were parsed into the condition and its operation lost — <c>string.IsNullOrWhiteSpace(x.Name)</c> rendered
        /// <c>Name = NULL</c>, <c>"Abcdef".StartsWith(x.Code)</c> rendered <c>Code LIKE 'Abcdef%'</c> (the reverse question).
        /// </summary>
        /// <remarks>
        /// TASK-869 (the user's decision, option A): the no-argument LIKE family keeps following the column's collation, as
        /// <c>==</c> does. With a <see cref="StringComparison"/> argument, <c>Ordinal</c> and <c>OrdinalIgnoreCase</c> translate
        /// exactly (<see cref="SqlValueFragmentDialect.StringMatch"/>) and every other comparison is refused by name, as is
        /// the <c>(string, bool, CultureInfo)</c> overload; before, the argument was ignored. <c>string.Equals(row, value,
        /// comparison)</c> translates the same way.
        /// </remarks>
        private static void RequireTranslatableCall(MethodCallExpression call)
        {
            var method = call.Method;
            if (method.DeclaringType == typeof(string) && method.Name is "StartsWith" or "EndsWith" or "Contains")
            {
                if (call.Object != null && ContainsParameter(call.Object))
                {
                    TranslatableComparison(call);
                    return;
                }
                throw new NotSupportedException(
                    $"Cannot translate String.{method.Name} to SQL when the string searched is a value and the pattern a "
                    + "column: LIKE answers the reverse question. Compare against a value instead.");
            }
            if (method.DeclaringType == typeof(string) && IsStringComparisonEquals(method) && method.IsStatic)
            {
                TranslatableComparison(call);
                if (!ContainsParameter(call.Arguments[1])) return;
                throw new NotSupportedException(
                    "Cannot translate String.Equals with a StringComparison to SQL when both strings are read from the row: only "
                    + "a column compared with a value is translated. Compare in memory.");
            }
            if (method.Name == "Contains")
            {
                // A value list's Contains: List<T>.Contains(x.Col), Enumerable / MemoryExtensions.Contains(list, x.Col).
                var source = method.IsStatic ? call.Arguments.FirstOrDefault() : call.Object;
                if (source != null && !ContainsParameter(source)) return;
                throw new NotSupportedException(
                    $"Cannot translate {method.DeclaringType?.Name}.Contains over a collection read from the row to SQL: only a "
                    + "list of values searched for a column (IN) is translated.");
            }
            if (IsCaseFold(call)) return;
            throw RefuseCall(call);
        }

        /// <summary>
        /// TASK-869 — a <c>Contains</c> / <c>StartsWith</c> / <c>EndsWith</c> with an explicit Ordinal or OrdinalIgnoreCase,
        /// already parsed into <paramref name="condition"/>: marks it for the provider's exact form. A null term is refused,
        /// as C# throws <see cref="ArgumentNullException"/> for it (the parser would otherwise have made it <c>IS NULL</c>).
        /// </summary>
        private static void WithComparison(Condition condition, StringComparison comparison, string method)
        {
            if (condition.Type is not (ConditionType.Like or ConditionType.StartsWith or ConditionType.EndsWith))
                throw new NotSupportedException(
                    $"Cannot translate String.{method} with a null value to SQL: C# throws ArgumentNullException for it.");
            condition.Comparison = comparison;
        }

        /// <summary>
        /// TASK-869 — <c>string.Equals(row, value, comparison)</c> (the shape <see cref="RewriteTranslatableCalls"/> leaves for
        /// every string <c>Equals</c> with a comparison) as a <see cref="ConditionType.TextEqual"/> condition, rendered by the
        /// provider's exact form. As in C#, a NULL column is not equal to a value (so a negation includes the NULL rows,
        /// TASK-859) and a null value is the column's <c>IS NULL</c>. The row's side must be a column or a case fold of one.
        /// </summary>
        private static Condition ParseStringEquals(MethodCallExpression call, Condition condition, Type? exprType)
        {
            var comparison = TranslatableComparison(call)
                ?? throw new InvalidOperationException("string.Equals(a, b, comparison) carries a comparison.");
            var column = call.Arguments[0];
            var inner = column is MethodCallExpression fold && IsCaseFold(fold) ? fold.Object! : column;
            if (UnwrapConvert(inner) is not MemberExpression member || !TryResolveParameterColumn(member, exprType, out _))
                throw new NotSupportedException(
                    "Cannot translate String.Equals with a StringComparison to SQL unless it compares a column (or its ToLower / "
                    + "ToUpper) with a value. Compare in memory.");

            condition.Type = ConditionType.TextEqual;
            condition.Comparison = comparison;
            ParseConditionExpression(call.Arguments[1], condition, exprType);
            ParseConditionExpression(column, condition, exprType);
            if (condition.Type != ConditionType.TextEqual)
            {
                condition.Comparison = null;
                return condition;
            }
            if (!string.IsNullOrEmpty(condition.Name) && OperandCanBeNull(inner, exprType))
            {
                condition.NullableOperands = new[] { new NullableOperand(condition.Name) };
                condition.NullOperandMatches = false;
            }
            return condition;
        }

        /// <summary><c>ToLower()</c>, <c>ToUpper()</c> and their invariant forms on a string: <c>LOWER</c> / <c>UPPER</c>.</summary>
        private static bool IsCaseFold(MethodCallExpression call)
            => call.Object != null && call.Method.DeclaringType == typeof(string) && call.Arguments.Count == 0
                && call.Method.Name is "ToLower" or "ToLowerInvariant" or "ToUpper" or "ToUpperInvariant";

        /// <summary><c>string.Equals(string, string, StringComparison)</c>, the shape TASK-869's rewrite leaves.</summary>
        private static readonly MethodInfo StaticStringEqualsWithComparison =
            typeof(string).GetMethod(nameof(string.Equals), new[] { typeof(string), typeof(string), typeof(StringComparison) })!;

        /// <summary><c>string.Equals(a, b, comparison)</c> or <c>a.Equals(b, comparison)</c>.</summary>
        private static bool IsStringComparisonEquals(MethodInfo method)
        {
            if (method.Name != nameof(string.Equals) || method.DeclaringType != typeof(string)) return false;
            var parameters = method.GetParameters();
            return method.IsStatic
                ? parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(string)
                    && parameters[2].ParameterType == typeof(StringComparison)
                : parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(StringComparison);
        }

        /// <summary>
        /// TASK-869 — the <see cref="StringComparison"/> a string call over the row compares with: null for an overload
        /// without one (the collation-following LIKE / <c>=</c>), <see cref="StringComparison.Ordinal"/> or
        /// <see cref="StringComparison.OrdinalIgnoreCase"/>, which translate exactly. Anything else is refused by name before
        /// the statement runs: a culture comparison (<c>CurrentCulture*</c>, <c>InvariantCulture*</c>) uses .NET's culture
        /// data, which no SQL collation reproduces, and so does the <c>(string, bool ignoreCase, CultureInfo)</c> overload.
        /// Before, the argument was ignored and the column's collation decided.
        /// </summary>
        internal static StringComparison? TranslatableComparison(MethodCallExpression call)
        {
            StringComparison? comparison = null;
            var parameters = call.Method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                var type = parameters[i].ParameterType;
                if (type == typeof(bool) || type == typeof(System.Globalization.CultureInfo))
                    throw new NotSupportedException(
                        $"Cannot translate String.{call.Method.Name} with an ignoreCase flag and a CultureInfo to SQL: a culture "
                        + "comparison has no SQL form that matches .NET's culture data. Use StringComparison.Ordinal or "
                        + "StringComparison.OrdinalIgnoreCase.");
                if (type != typeof(StringComparison)) continue;
                var argument = call.Arguments[i];
                if (ContainsParameter(argument))
                    throw new NotSupportedException(
                        $"Cannot translate String.{call.Method.Name} to SQL when its StringComparison is read from the row.");
                var value = argument is ConstantExpression constant
                    ? constant.Value
                    : Expression.Lambda(argument).Compile().DynamicInvoke();
                comparison = value is StringComparison known ? known : throw new NotSupportedException(
                    $"Cannot translate String.{call.Method.Name} to SQL: its StringComparison is null.");
            }
            if (comparison is null or StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase) return comparison;
            throw new NotSupportedException(
                $"Cannot translate String.{call.Method.Name} with StringComparison.{comparison} to SQL: only Ordinal and "
                + "OrdinalIgnoreCase are translated, exactly, on every provider. A culture comparison uses .NET's culture data, "
                + "which no SQL collation reproduces. Before this was refused the comparison was ignored and the column's "
                + "collation decided. Use StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase, or the overload "
                + "without a comparison, which follows the column's collation.");
        }

        private static NotSupportedException RefuseCall(MethodCallExpression call)
            => new($"Cannot translate {call.Method.DeclaringType?.Name}.{call.Method.Name} over the row to SQL: it is not one of "
                + "the calls the SQL filter parser translates exactly (StartsWith, EndsWith, Contains, string.IsNullOrEmpty, "
                + "ToLower/ToUpper without a culture, Equals(T), Equals with StringComparison.Ordinal / OrdinalIgnoreCase, "
                + "GetValueOrDefault, and TASK-857's Math calls). Before this was "
                + "refused the call was dropped and its argument compared instead. Compute the value in memory, or compare "
                + "the column itself.");

        private static NotSupportedException RefuseMember(MemberExpression member)
            => new($"Cannot translate the member {member.Member.DeclaringType?.Name}.{member.Member.Name} over the row to SQL: "
                + "it is neither a mapped column of the entity nor a member the SQL filter parser translates (a nullable's "
                + "Value / HasValue, a DateTime column's Date). Compare the column itself.");

        private static NotSupportedException RefuseNode(Expression node)
        {
            var name = node.NodeType == ExpressionType.Not && node.Type != typeof(bool) && node.Type != typeof(bool?)
                ? "bitwise complement (~)"
                : node is BinaryExpression { Method: { } method }
                    ? $"{node.NodeType} ({method.DeclaringType?.Name}.{method.Name})"
                    : node.NodeType.ToString();
            // The node's type only, never its text: a rendered tree carries the values a closure captured.
            return new($"Cannot translate the {name} operator ({node.GetType().Name}) over the row to SQL: the SQL parser has "
                + "no exact translation for it on every provider. Before this was refused it was dropped or misread. "
                + "Compute the value in memory, or compare the column itself.");
        }
    }
}
