using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.CosmosDB.Stores;

public static partial class CosmosFilterRewrite
{
    /// <summary>
    /// TASK-902: every comparison of a <see cref="DateTime"/> read from the document compares a wall-clock key, the stored
    /// wall clock with its fraction padded to seven digits (<c>yyyy-MM-ddTHH:mm:ss</c> + <c>fffffff</c>), never the stored
    /// text. Returns <paramref name="filter"/> by reference when nothing changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> Birko's serializer (System.Text.Json defaults) writes a DateTime as ISO 8601 text with as many
    /// fraction digits as the value needs, none for a whole second, then <c>Z</c> (UTC), the writer's offset (local) or
    /// nothing (unspecified). Cosmos DB compares that text, so <c>"…00.5Z"</c> sorted before <c>"…00Z"</c>,
    /// <c>"…00"</c> before <c>"…00+01:00"</c>, and an equality held only between equal texts. C# compares the ticks,
    /// whatever the kind. Measured on the emulator (vnext-preview, 2026-11-10): 71 of 84 filter shapes answered other rows
    /// on a read, alike on every path.</para>
    /// <para><b>The key.</b> The first 19 characters are the wall clock to the second in every stored form; the fraction is
    /// what follows a <c>.</c> up to the suffix (<c>Z</c>, <c>+</c> or <c>-</c>, none of which is a digit), padded with
    /// zeros. A value from the caller is keyed in C# from its ticks, so its kind does not matter, as in C#. A value stored
    /// with an offset reads back converted to the reader's local time, so the key is C#'s order when the stored offset is
    /// the reader's own, the assumption TASK-897's aggregates and the time bucket already make.</para>
    /// <para><b>Index.</b> The key is computed, so it cannot use the index. Against a value, the comparison is preceded by a
    /// plain text range on the member that holds every row the key can match (<c>m &gt;= "yyyy-MM-ddTHH:mm:ss"</c> of the
    /// value's second, <c>m &lt;</c> the next second's), so the range index narrows the rows and the key decides. A
    /// comparison of two members, <c>!=</c>, and a list have no such range.</para>
    /// <para><b>Also translated:</b> <c>a.Equals(b)</c>, <c>DateTime.Equals(a, b)</c>, <c>DateTime.Compare(a, b)</c> and
    /// <c>a.CompareTo(b)</c> compared with 0 (the provider threw for each), a list's <c>Contains</c> (a list of keys), and
    /// <c>m.Date</c> compared with another member (a date-only key; against a value DateTruncation has already made it a
    /// range).</para>
    /// <para><b>Refused by name</b> (<see cref="NotSupportedException"/>, before any request): any comparison or list of a
    /// <see cref="DateTimeOffset"/> read from the document (C# compares the instant, the stored text holds the writer's
    /// offset, and the provider renders no conversion: measured, <c>O == o</c> missed the same instant written with
    /// another offset); a member of a stored DateTime or DateTimeOffset (<c>Year</c>, <c>Hour</c>, <c>Ticks</c>,
    /// <c>TimeOfDay</c>, ...) outside the <c>.Date</c> forms above, which the provider rendered as a JSON property of a
    /// string (<c>root["T"]["Year"]</c>) and matched nothing; a <c>Compare</c> result compared with anything but 0.</para>
    /// </remarks>
    /// <exception cref="NotSupportedException">A DateTime or DateTimeOffset shape no Cosmos DB rendering answers as C# does.</exception>
    public static Expression<Func<T, bool>>? DateTimeKeys<T>(Expression<Func<T, bool>>? filter)
    {
        if (filter == null) return null;
        var body = new DateTimeRewriter().Visit(filter.Body)!;
        return ReferenceEquals(body, filter.Body) ? filter : Expression.Lambda<Func<T, bool>>(body, filter.Parameters);
    }

    /// <summary>
    /// TASK-902: the wall-clock key of a caller's value, as <see cref="WallClockKey(Expression)"/> computes it for a stored
    /// one: <c>yyyy-MM-ddTHH:mm:ss</c> and the seven fraction digits. Its kind is not part of it, as C# ignores it.
    /// </summary>
    internal static string WallClockKeyOf(DateTime value)
        => value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture)
           + (value.Ticks % TimeSpan.TicksPerSecond).ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>The first 19 characters of a stored DateTime that <paramref name="value"/>'s second holds.</summary>
    private static string SecondOf(DateTime value) => value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// TASK-902: the wall-clock key of a stored DateTime, as an expression both renderers render (the store's LINQ provider
    /// and the view translator): <c>SUBSTRING(t, 0, 19)</c> and the fraction, padded to seven digits. The one producer
    /// of the key in a filter.
    /// </summary>
    /// <param name="operand">A <see cref="DateTime"/> or <see cref="Nullable{DateTime}"/> read from the document: a member,
    /// a member read as its default (TASK-899), or a member's <c>.Date</c>.</param>
    internal static Expression WallClockKey(Expression operand)
    {
        var unwrapped = Birko.Data.Expressions.SpanContains.UnwrapConvert(operand) ?? operand;
        if (unwrapped is MemberExpression { Member.Name: nameof(DateTime.Date), Expression: { } dated }
            && dated.Type == typeof(DateTime))
        {
            // A date: its first ten characters, at midnight.
            return Concat(Substring(Text(dated), 0, 10), Expression.Constant("T00:00:00" + new string('0', WallClockFractionDigits)));
        }

        var (second, fraction) = WallClockParts(Text(unwrapped));
        return Concat(second, fraction);
    }

    /// <summary>The length of the wall clock to the second, <c>yyyy-MM-ddTHH:mm:ss</c>, at the start of every stored form.</summary>
    private const int WallClockSecondLength = 19;

    /// <summary>The fraction digits a wall-clock key carries: a DateTime's seven.</summary>
    private const int WallClockFractionDigits = 7;

    /// <summary>
    /// TASK-905: the two parts of the wall-clock key of a stored DateTime's text: its first 19 characters (the second) and the
    /// fraction, padded to seven digits. The one definition of the key, which the filter's key
    /// (<see cref="WallClockKey(Expression)"/>, the parts joined) and the aggregates' (<see cref="WallClockKeySql"/>, the parts
    /// joined by a <c>.</c>, so the key reads back as a DateTime) both take.
    /// </summary>
    /// <remarks>What follows the second is <c>.fffffff</c> (one to seven digits) or nothing, then the suffix (<c>Z</c>, an
    /// offset's <c>+</c> or <c>-</c>, or nothing). The suffix's first character is made a <c>Z</c>, the <c>.</c> dropped and a
    /// <c>Z</c> appended, so the digits run up to the first <c>Z</c>.</remarks>
    private static (Expression Second, Expression Fraction) WallClockParts(Expression text)
    {
        var rest = Concat(
            Replace(Replace(Replace(Substring(text, WallClockSecondLength, 20), "+", "Z"), "-", "Z"), ".", ""),
            Expression.Constant("Z"));
        var digits = Substring(rest, Expression.Constant(0), Expression.Call(rest, StringIndexOf, Expression.Constant("Z")));
        var fraction = Substring(Concat(digits, Expression.Constant(new string('0', WallClockFractionDigits))), 0, WallClockFractionDigits);
        return (Substring(text, 0, WallClockSecondLength), fraction);
    }

    /// <summary>
    /// TASK-905: the wall-clock key of the stored DateTime at <paramref name="column"/> (a Cosmos SQL accessor), as SQL text for
    /// an aggregate: <see cref="WallClockParts"/> joined by a <c>.</c>, so a MIN/MAX of it reads back as a DateTime, then the
    /// stored suffix when <paramref name="withSuffix"/> (a DateTime group is grouped on the wall clock alone). TASK-897's
    /// <c>CosmosAggregationHelper.WallClockKey</c>, now rendered from the filter's own key parts.
    /// </summary>
    internal static string WallClockKeySql(string column, bool withSuffix)
    {
        var (second, fraction) = WallClockParts(StoredTextLeaf);
        var key = KeySql(Concat(Concat(second, Expression.Constant(".")), fraction), column);
        if (!withSuffix) return key;
        // The suffix, as it was stored: "Z", an offset ("+01:00"), or nothing.
        var x = column;
        var suffixLength = $"(ENDSWITH({x}, \"Z\") ? 1 : (SUBSTRING({x}, LENGTH({x}) - 6, 1) IN (\"+\", \"-\") ? 6 : 0))";
        return $"CONCAT({key}, RIGHT({x}, {suffixLength}))";
    }

    /// <summary>The stored text a <see cref="KeySql"/> rendering reads: the column it is given.</summary>
    private static readonly ParameterExpression StoredTextLeaf = Expression.Parameter(typeof(string), "storedText");

    /// <summary>
    /// TASK-905: a key built of <see cref="Substring(Expression, int, int)"/>, <see cref="Replace"/>, <c>IndexOf</c> and
    /// <see cref="Concat"/> over <see cref="StoredTextLeaf"/>, as Cosmos SQL text over <paramref name="column"/>: what the
    /// store's LINQ provider renders for the same tree. Its constants are this file's, never a caller's.
    /// </summary>
    private static string KeySql(Expression key, string column)
    {
        switch (key)
        {
            case ParameterExpression leaf when ReferenceEquals(leaf, StoredTextLeaf):
                return column;
            case ConstantExpression { Value: int number }:
                return number.ToString(CultureInfo.InvariantCulture);
            case ConstantExpression { Value: string text }:
                return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            case MethodCallExpression call when call.Method == StringSubstring:
                return $"SUBSTRING({KeySql(call.Object!, column)}, {KeySql(call.Arguments[0], column)}, {KeySql(call.Arguments[1], column)})";
            case MethodCallExpression call when call.Method == StringReplace:
                return $"REPLACE({KeySql(call.Object!, column)}, {KeySql(call.Arguments[0], column)}, {KeySql(call.Arguments[1], column)})";
            case MethodCallExpression call when call.Method == StringIndexOf:
                return $"INDEX_OF({KeySql(call.Object!, column)}, {KeySql(call.Arguments[0], column)})";
            case MethodCallExpression call when call.Method == StringConcat:
                return $"CONCAT({KeySql(call.Arguments[0], column)}, {KeySql(call.Arguments[1], column)})";
            default:
                throw new InvalidOperationException($"A wall-clock key holds a {key.NodeType} no key holds. (TASK-905)");
        }
    }

    /// <summary>
    /// TASK-902: <paramref name="node"/> is a stored DateTime read as its text, <c>(string)(object)m</c>: the leaf of a
    /// wall-clock key, which renders as the member itself.
    /// </summary>
    internal static bool IsDateTimeText(Expression node)
        => node is UnaryExpression { NodeType: ExpressionType.Convert, Operand: UnaryExpression { NodeType: ExpressionType.Convert } boxed } text
           && text.Type == typeof(string)
           && boxed.Type == typeof(object)
           && (Nullable.GetUnderlyingType(boxed.Operand.Type) ?? boxed.Operand.Type) == typeof(DateTime);

    /// <summary>
    /// TASK-902: the stored DateTime a wall-clock key reads (unwrapped as <c>SpanContains.UnwrapConvert</c> unwraps), or
    /// <c>null</c> when <paramref name="node"/> is not a key.
    /// </summary>
    internal static Expression? KeyedOperand(Expression node)
    {
        if (node.Type != typeof(string)) return null;
        var finder = new DateTimeTextFinder();
        finder.Visit(node);
        return finder.Found == null ? null : Birko.Data.Expressions.SpanContains.UnwrapConvert(((UnaryExpression)((UnaryExpression)finder.Found).Operand).Operand);
    }

    private sealed class DateTimeTextFinder : ExpressionVisitor
    {
        public Expression? Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (Found != null || node == null) return node;
            if (IsDateTimeText(node))
            {
                Found = node;
                return node;
            }
            return base.Visit(node);
        }
    }

    private static Expression Text(Expression dateTime)
    {
        var operand = dateTime;
        // Nullable<DateTime>.Value is the stored value itself.
        if (operand is MemberExpression { Member.Name: "Value", Expression: { } inner } && Nullable.GetUnderlyingType(inner.Type) != null)
        {
            operand = inner;
        }
        return Expression.Convert(Expression.Convert(operand, typeof(object)), typeof(string));
    }

    private static readonly MethodInfo StringSubstring = typeof(string).GetMethod(nameof(string.Substring), new[] { typeof(int), typeof(int) })!;
    private static readonly MethodInfo StringReplace = typeof(string).GetMethod(nameof(string.Replace), new[] { typeof(string), typeof(string) })!;
    private static readonly MethodInfo StringIndexOf = typeof(string).GetMethod(nameof(string.IndexOf), new[] { typeof(string) })!;
    private static readonly MethodInfo StringConcat = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })!;

    private static Expression Substring(Expression s, int start, int length)
        => Substring(s, Expression.Constant(start), Expression.Constant(length));

    private static Expression Substring(Expression s, Expression start, Expression length) => Expression.Call(s, StringSubstring, start, length);

    private static Expression Replace(Expression s, string from, string to)
        => Expression.Call(s, StringReplace, Expression.Constant(from), Expression.Constant(to));

    private static Expression Concat(Expression a, Expression b) => Expression.Call(StringConcat, a, b);

    // The equality pair of the ordinal string comparisons above: a comparison of two keys, recognised by a second pass by
    // its declaring type. Only reached if the tree were ever compiled.
    private static bool OrdinalEqual(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
    private static bool OrdinalNotEqual(string? a, string? b) => !string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>The ordinal string comparison for <paramref name="op"/>, any of the six.</summary>
    private static MethodInfo KeyComparison(ExpressionType op) => op switch
    {
        ExpressionType.Equal => typeof(CosmosFilterRewrite).GetMethod(nameof(OrdinalEqual), BindingFlags.NonPublic | BindingFlags.Static)!,
        ExpressionType.NotEqual => typeof(CosmosFilterRewrite).GetMethod(nameof(OrdinalNotEqual), BindingFlags.NonPublic | BindingFlags.Static)!,
        _ => OrdinalMethod(op),
    };

    private static Expression CompareKeys(ExpressionType op, Expression a, Expression b)
        => Expression.MakeBinary(op, a, b, false, KeyComparison(op));

    private static bool IsDateTime(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(DateTime);

    private static bool IsDateTimeOffset(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(DateTimeOffset);

    private static NotSupportedException DateTimeRefusal(string what, string why)
        => new($"A Cosmos DB filter cannot translate {what} as C# evaluates it: {why}. (TASK-902)");

    private sealed class DateTimeRewriter : ExpressionVisitor
    {
        protected override Expression VisitUnary(UnaryExpression node)
            => IsDateTimeText(node) ? node : base.VisitUnary(node);

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.LessThan
                or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual))
            {
                return base.VisitBinary(node);
            }

            // Produced by a pass: a key or an ordinal string comparison.
            if (node.Method?.DeclaringType == typeof(CosmosFilterRewrite)) return node;

            if (TryCompareCall(node, out var compared)) return Visit(compared)!;

            if ((IsDateTimeOffset(node.Left.Type) || IsDateTimeOffset(node.Right.Type)) && (Reads(node.Left) || Reads(node.Right)))
            {
                throw DateTimeRefusal("a comparison of a DateTimeOffset member", "C# compares the instant, Cosmos DB the stored "
                    + "text, which holds the writer's offset, and its LINQ provider renders no conversion to UTC; compare a "
                    + "DateTime member stored in UTC instead");
            }

            if (!IsDateTime(node.Left.Type) || !IsDateTime(node.Right.Type)) return base.VisitBinary(node);
            return Keyed(node.NodeType, node.Left, node.Right) ?? node;
        }

        /// <summary><paramref name="a"/> <paramref name="op"/> <paramref name="b"/> over wall-clock keys, or null when neither reads the document.</summary>
        private Expression? Keyed(ExpressionType op, Expression a, Expression b)
        {
            var readsA = Reads(a);
            var readsB = Reads(b);
            if (!readsA && !readsB) return null;
            if (!readsA)
            {
                (a, b) = (b, a);
                op = Mirror(op);
            }

            RequireKeyable(a);
            if (readsB)
            {
                RequireKeyable(b);
                return CompareKeys(op, WallClockKey(a), WallClockKey(b));
            }

            if (Evaluate(b) is not DateTime value) return null;   // a null: the null semantics answered it
            var keyed = CompareKeys(op, WallClockKey(a), Expression.Constant(WallClockKeyOf(value)));

            // A text range on the member itself, which the index can serve, holding every row the key can match: the
            // first 19 characters of every stored form are its second.
            var unwrapped = Birko.Data.Expressions.SpanContains.UnwrapConvert(a) ?? a;
            if (unwrapped is not MemberExpression || op == ExpressionType.NotEqual) return keyed;
            if (unwrapped is MemberExpression { Member.Name: nameof(DateTime.Date) } m && m.Expression?.Type == typeof(DateTime)) return keyed;

            var text = Text(unwrapped);
            Expression? range = null;
            if (op is ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.Equal)
            {
                range = CompareKeys(ExpressionType.GreaterThanOrEqual, text, Expression.Constant(SecondOf(value)));
            }
            var second = new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond);
            if (op is ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.Equal
                && second < DateTime.MaxValue.AddSeconds(-1))
            {
                var below = CompareKeys(ExpressionType.LessThan, text, Expression.Constant(SecondOf(second.AddSeconds(1))));
                range = range == null ? below : Expression.AndAlso(range, below);
            }
            return range == null ? keyed : Expression.AndAlso(range, keyed);
        }

        /// <summary>Only a member, a member read as its default, or a member's <c>.Date</c> has a key.</summary>
        private static void RequireKeyable(Expression operand)
        {
            var unwrapped = Birko.Data.Expressions.SpanContains.UnwrapConvert(operand) ?? operand;
            switch (unwrapped)
            {
                case MemberExpression { Member.Name: nameof(DateTime.Date), Expression: { } dated } when dated.Type == typeof(DateTime):
                    RequireKeyable(dated);
                    return;
                case MemberExpression { Expression: { } owner } when owner.Type == typeof(DateTime) || IsDateTimeOffset(owner.Type):
                    break;
                case MemberExpression:
                case ConditionalExpression { Test: MethodCallExpression test } when IsDefinedCall(test):
                case ConditionalExpression:
                    return;
            }
            throw DateTimeRefusal("a DateTime computed from a document member (" + unwrapped.NodeType + ")",
                "only a member's stored text can be compared as its value");
        }

        /// <summary>
        /// <c>DateTime.Compare(a, b) op 0</c> or <c>a.CompareTo(b) op 0</c> (either side): <c>a op b</c>. The provider threw
        /// for both.
        /// </summary>
        private static bool TryCompareCall(BinaryExpression node, out Expression compared)
        {
            compared = node;
            var op = node.NodeType;
            MethodCallExpression? call = node.Left as MethodCallExpression;
            var other = node.Right;
            if (!IsDateTimeCompare(call))
            {
                call = node.Right as MethodCallExpression;
                other = node.Left;
                op = Mirror(op);
                if (!IsDateTimeCompare(call)) return false;
            }

            var a = call!.Object ?? call.Arguments[0];
            var b = call.Object == null ? call.Arguments[1] : call.Arguments[0];
            if (!Reads(a) && !Reads(b)) return false;
            if (Reads(other) || Evaluate(other) is not int k || k != 0)
            {
                throw DateTimeRefusal("DateTime." + call.Method.Name, "a comparison's result is translated only when compared with the constant 0");
            }
            compared = Expression.MakeBinary(op, a, b);
            return true;
        }

        private static bool IsDateTimeCompare(MethodCallExpression? call)
            => call != null && call.Method.DeclaringType == typeof(DateTime)
               && (call.Method.Name == nameof(DateTime.Compare) && call.Object == null && call.Arguments.Count == 2
                   || call.Method.Name == nameof(DateTime.CompareTo) && call.Object != null && call.Arguments.Count == 1
                      && call.Arguments[0].Type == typeof(DateTime));

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var method = node.Method;
            if (method.DeclaringType == typeof(DateTime) && method.Name == nameof(DateTime.Equals) && Reads(node)
                && node.Arguments.All(arg => arg.Type == typeof(DateTime)))
            {
                var (a, b) = node.Object != null ? (node.Object, node.Arguments[0]) : (node.Arguments[0], node.Arguments[1]);
                if (node.Arguments.Count == (node.Object != null ? 1 : 2))
                {
                    return Keyed(ExpressionType.Equal, a, b) ?? node;
                }
            }

            if (TryContains(node, out var source, out var item, out var elementType))
            {
                if (IsDateTimeOffset(elementType))
                {
                    throw DateTimeRefusal("a list of DateTimeOffset values tested against a member", "C# compares the instant, "
                        + "Cosmos DB the stored text, which holds the writer's offset");
                }
                if (IsDateTime(elementType))
                {
                    RequireKeyable(item);
                    var keys = new List<string>();
                    if (Evaluate(source) is IEnumerable values)
                    {
                        foreach (var v in values)
                        {
                            if (v is DateTime d) keys.Add(WallClockKeyOf(d));   // a null: the null semantics took it out
                        }
                    }
                    return Expression.Call(EnumerableContains.MakeGenericMethod(typeof(string)),
                        Expression.Constant(keys.Distinct(StringComparer.Ordinal).ToArray(), typeof(string[])), WallClockKey(item));
                }
            }
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null && (node.Expression.Type == typeof(DateTime) || node.Expression.Type == typeof(DateTimeOffset))
                && Reads(node.Expression))
            {
                throw DateTimeRefusal($"{node.Expression.Type.Name}.{node.Member.Name} of a document member",
                    "Cosmos DB stores the value as text, and the LINQ provider reads the member as a JSON property of that text, "
                    + "which matches nothing; compare the member itself with a value (a .Date compared with a value or another "
                    + "member's .Date is translated)");
            }
            return base.VisitMember(node);
        }
    }
}
