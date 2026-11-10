using System;
using System.Globalization;
using System.Linq.Expressions;

namespace Birko.Data.CosmosDB.Stores;

public static partial class CosmosFilterRewrite
{
    /// <summary>
    /// TASK-905: the shapes of a <see cref="TimeSpan"/> read from the document, before <see cref="NullSemantics{T}"/> sees
    /// them: <c>a.Equals(b)</c> and <c>TimeSpan.Equals(a, b)</c> become <c>a == b</c>, and <c>TimeSpan.Compare(a, b)</c> or
    /// <c>a.CompareTo(b)</c> compared with 0 becomes <c>a op b</c> (the provider threw for each), so a negation or a missing
    /// member is answered as for any comparison. Every shape no rendering answers as C# is refused by name. Returns
    /// <paramref name="filter"/> by reference when nothing changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Refused</b> (<see cref="NotSupportedException"/>, before any request): a member of a stored TimeSpan
    /// (<c>Ticks</c>, <c>TotalSeconds</c>, <c>Days</c>, ...), which the provider renders as a JSON property of the stored text
    /// and matches nothing; a TimeSpan computed from a member (<c>+</c>, <c>-</c>, <c>Add</c>, <c>Duration()</c>, ...), which
    /// it renders over the text; a <c>Compare</c> result compared with anything but 0.</para>
    /// </remarks>
    /// <exception cref="NotSupportedException">A TimeSpan shape no Cosmos DB rendering answers as C# does.</exception>
    public static Expression<Func<T, bool>>? TimeSpanShapes<T>(Expression<Func<T, bool>>? filter)
    {
        if (filter == null) return null;
        var body = new TimeSpanShapeRewriter().Visit(filter.Body)!;
        return ReferenceEquals(body, filter.Body) ? filter : Expression.Lambda<Func<T, bool>>(body, filter.Parameters);
    }

    /// <summary>
    /// TASK-905: every ordering (<c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>) of a <see cref="TimeSpan"/> read from
    /// the document compares its sign and a fixed-width magnitude key, never the stored text. Returns
    /// <paramref name="filter"/> by reference when nothing changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> Birko's serializer (System.Text.Json defaults) writes a TimeSpan in the constant format,
    /// <c>[-][d.]hh:mm:ss[.fffffff]</c>: the day count only when there is one and unpadded, the seven fraction digits only
    /// when there is a fraction. Cosmos DB compares that text, so <c>"1.02:03:04"</c> sorted before <c>"23:00:00"</c>,
    /// <c>"10.00:00:00"</c> before <c>"2.00:00:00"</c> and <c>"-00:00:01"</c> before <c>"-00:00:02"</c>. Measured on the
    /// emulator (vnext-preview, 2026-11-10): 68 of 124 filter shapes answered other rows on some path (reads, counts, the
    /// aggregate WHERE, update/delete), every ordering among them. Equality is exact already (the text is canonical: one
    /// text per value), so <c>==</c>, <c>!=</c> and a list's <c>Contains</c> keep their rendering, and their index.</para>
    /// <para><b>The key.</b> The sign is the text's first character: <c>t &lt; "0"</c> is negative (<c>-</c> sorts below
    /// every digit), <c>t &gt;= "0"</c> is not, both a plain range the index serves. The magnitude key is the text without
    /// <c>-</c> and <c>.</c> (<c>[d]hh:mm:ss[fffffff]</c>), its day-and-hour digits before the first <c>:</c> left-padded to
    /// ten (eight day digits hold <see cref="TimeSpan.MaxValue"/>), the rest padded with fraction zeros to thirteen:
    /// 23 characters that order as the magnitude. A negative value orders by the reverse of its magnitude, so a comparison is
    /// split by sign; a caller's value is keyed in C# (<see cref="TimeSpanKeyOf"/>) and its sign decides the split. Two members
    /// compared for equality compare their texts, as an ordinal comparison the view translator can render.</para>
    /// <para>A null or missing member makes every leg undefined, as the plain comparison was; <see cref="NullSemantics{T}"/>
    /// has already guarded it where that would change the answer.</para>
    /// </remarks>
    /// <exception cref="NotSupportedException">An ordering of a TimeSpan computed from a document member.</exception>
    public static Expression<Func<T, bool>>? TimeSpanKeys<T>(Expression<Func<T, bool>>? filter)
    {
        if (filter == null) return null;
        var body = new TimeSpanKeyRewriter().Visit(filter.Body)!;
        return ReferenceEquals(body, filter.Body) ? filter : Expression.Lambda<Func<T, bool>>(body, filter.Parameters);
    }

    /// <summary>The day-and-hour digits of a magnitude key: eight day digits and two hour digits.</summary>
    private const string TimeSpanDayHourPadding = "0000000000";

    /// <summary>
    /// TASK-905: the magnitude key of a caller's value, as <see cref="TimeSpanKey"/> computes it for a stored one:
    /// <c>dddddddd</c> <c>hh</c> <c>:mm:ss</c> <c>fffffff</c>. Its sign is not part of it.
    /// </summary>
    internal static string TimeSpanKeyOf(TimeSpan value)
    {
        // The magnitude of TimeSpan.MinValue does not fit a TimeSpan.
        var magnitude = value.Ticks < 0 ? (ulong)(-(value.Ticks + 1)) + 1UL : (ulong)value.Ticks;
        var days = magnitude / TimeSpan.TicksPerDay;
        var rest = magnitude % TimeSpan.TicksPerDay;
        var hours = rest / TimeSpan.TicksPerHour;
        var minutes = rest % TimeSpan.TicksPerHour / TimeSpan.TicksPerMinute;
        var seconds = rest % TimeSpan.TicksPerMinute / TimeSpan.TicksPerSecond;
        var fraction = rest % TimeSpan.TicksPerSecond;
        return string.Concat(
            days.ToString("D8", CultureInfo.InvariantCulture),
            hours.ToString("D2", CultureInfo.InvariantCulture), ":",
            minutes.ToString("D2", CultureInfo.InvariantCulture), ":",
            seconds.ToString("D2", CultureInfo.InvariantCulture),
            fraction.ToString("D7", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// TASK-905: the magnitude key of a stored TimeSpan's text, as an expression both renderers render: the text without
    /// <c>-</c> and <c>.</c>, the digits before its first <c>:</c> padded to ten, the rest to thirteen. The one producer of
    /// the key in a filter.
    /// </summary>
    private static Expression TimeSpanKey(Expression text)
    {
        var bare = Replace(Replace(text, "-", ""), ".", "");
        var colon = Expression.Call(bare, StringIndexOf, Expression.Constant(":"));
        var dayHour = Concat(
            Substring(Expression.Constant(TimeSpanDayHourPadding), colon, Expression.Constant(TimeSpanDayHourPadding.Length)),
            Substring(bare, Expression.Constant(0), colon));
        // ":mm:ss" and the fraction: thirteen characters.
        var rest = Substring(Concat(Substring(bare, colon, Expression.Constant(20)), Expression.Constant("0000000")), 0, 13);
        return Concat(dayHour, rest);
    }

    /// <summary>
    /// TASK-905: <paramref name="node"/> is a stored DateTime or TimeSpan read as its text, <c>(string)(object)m</c>: the leaf
    /// of a key, which renders as the member itself.
    /// </summary>
    internal static bool IsStoredText(Expression node)
        => IsDateTimeText(node)
           || node is UnaryExpression { NodeType: ExpressionType.Convert, Operand: UnaryExpression { NodeType: ExpressionType.Convert } boxed } text
              && text.Type == typeof(string)
              && boxed.Type == typeof(object)
              && IsTimeSpan(boxed.Operand.Type);

    private static bool IsTimeSpan(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(TimeSpan);

    private static NotSupportedException TimeSpanRefusal(string what, string why)
        => new($"A Cosmos DB filter cannot translate {what} as C# evaluates it: {why}. (TASK-905)");

    private sealed class TimeSpanShapeRewriter : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.LessThan
                    or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                && TryCompareCall(node, out var compared))
            {
                return Visit(compared)!;
            }

            if (node.NodeType is ExpressionType.Add or ExpressionType.AddChecked or ExpressionType.Subtract
                    or ExpressionType.SubtractChecked or ExpressionType.Multiply or ExpressionType.MultiplyChecked
                    or ExpressionType.Divide
                && IsTimeSpan(node.Type) && Reads(node))
            {
                throw TimeSpanRefusal($"a TimeSpan computed from a document member ({node.NodeType})",
                    "Cosmos DB stores the value as text, so arithmetic on it is arithmetic on text; compare the member itself "
                    + "with a value computed in the caller");
            }
            return base.VisitBinary(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is ExpressionType.Negate or ExpressionType.NegateChecked or ExpressionType.UnaryPlus
                && IsTimeSpan(node.Type) && Reads(node))
            {
                throw TimeSpanRefusal($"a TimeSpan computed from a document member ({node.NodeType})",
                    "Cosmos DB stores the value as text, so arithmetic on it is arithmetic on text; compare the member itself "
                    + "with a value computed in the caller");
            }
            return base.VisitUnary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType != typeof(TimeSpan) || !Reads(node)) return base.VisitMethodCall(node);

            if (node.Method.Name == nameof(TimeSpan.Equals) && node.Arguments.Count == (node.Object != null ? 1 : 2)
                && node.Arguments[node.Arguments.Count - 1].Type == typeof(TimeSpan))
            {
                var (a, b) = node.Object != null ? (node.Object, node.Arguments[0]) : (node.Arguments[0], node.Arguments[1]);
                return Visit(Expression.Equal(a, b))!;
            }

            throw TimeSpanRefusal($"TimeSpan.{node.Method.Name} over a document member", node.Method.Name is nameof(TimeSpan.Compare)
                    or nameof(TimeSpan.CompareTo)
                    ? "a comparison's result is translated only when compared with the constant 0"
                    : "Cosmos DB stores the value as text, so a TimeSpan method over it is a method over text; compare the member "
                      + "itself with a value computed in the caller");
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null && node.Expression.Type == typeof(TimeSpan) && Reads(node.Expression))
            {
                throw TimeSpanRefusal($"TimeSpan.{node.Member.Name} of a document member",
                    "Cosmos DB stores the value as text, and the LINQ provider reads the member as a JSON property of that text, "
                    + "which matches nothing; compare the member itself with a value");
            }
            return base.VisitMember(node);
        }

        /// <summary><c>TimeSpan.Compare(a, b) op 0</c> or <c>a.CompareTo(b) op 0</c> (either side): <c>a op b</c>.</summary>
        private static bool TryCompareCall(BinaryExpression node, out Expression compared)
        {
            compared = node;
            var op = node.NodeType;
            var call = node.Left as MethodCallExpression;
            var other = node.Right;
            if (!IsTimeSpanCompare(call))
            {
                call = node.Right as MethodCallExpression;
                other = node.Left;
                op = Mirror(op);
                if (!IsTimeSpanCompare(call)) return false;
            }

            var a = call!.Object ?? call.Arguments[0];
            var b = call.Object == null ? call.Arguments[1] : call.Arguments[0];
            if (!Reads(a) && !Reads(b)) return false;
            if (Reads(other) || Evaluate(other) is not int k || k != 0)
            {
                throw TimeSpanRefusal("TimeSpan." + call.Method.Name, "a comparison's result is translated only when compared with the constant 0");
            }
            compared = Expression.MakeBinary(op, a, b);
            return true;
        }

        private static bool IsTimeSpanCompare(MethodCallExpression? call)
            => call != null && call.Method.DeclaringType == typeof(TimeSpan)
               && (call.Method.Name == nameof(TimeSpan.Compare) && call.Object == null && call.Arguments.Count == 2
                   || call.Method.Name == nameof(TimeSpan.CompareTo) && call.Object != null && call.Arguments.Count == 1
                      && call.Arguments[0].Type == typeof(TimeSpan));
    }

    private sealed class TimeSpanKeyRewriter : ExpressionVisitor
    {
        protected override Expression VisitUnary(UnaryExpression node)
            => IsStoredText(node) ? node : base.VisitUnary(node);

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not (ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan
                or ExpressionType.GreaterThanOrEqual or ExpressionType.Equal or ExpressionType.NotEqual))
            {
                return base.VisitBinary(node);
            }

            // Produced by a pass: a key or an ordinal string comparison.
            if (node.Method?.DeclaringType == typeof(CosmosFilterRewrite)) return node;
            if (!IsTimeSpan(node.Left.Type) || !IsTimeSpan(node.Right.Type)) return base.VisitBinary(node);

            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
            {
                // The text is canonical, so equality against a value keeps its rendering and its index. Two members are
                // compared as their texts, a comparison the view translator renders over computed operands (a member read
                // as its default) as the store's provider does; it refused the plain form.
                if (!Reads(node.Left) || !Reads(node.Right)) return node;
                return CompareKeys(node.NodeType, Text(Stored(node.Left)), Text(Stored(node.Right)));
            }

            var (op, a, b) = (node.NodeType, node.Left, node.Right);
            var readsB = Reads(b);
            if (!Reads(a))
            {
                if (!readsB) return node;
                (a, b) = (b, a);
                op = Mirror(op);
                readsB = false;
            }

            var textA = Text(Stored(a));
            var negativeA = CompareKeys(ExpressionType.LessThan, textA, Expression.Constant("0"));
            var positiveA = CompareKeys(ExpressionType.GreaterThanOrEqual, textA, Expression.Constant("0"));
            var keyA = TimeSpanKey(textA);
            var below = op is ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

            if (!readsB)
            {
                if (Evaluate(b) is not TimeSpan value) return node;   // a null: the null semantics answered it
                var key = Expression.Constant(TimeSpanKeyOf(value));
                if (value < TimeSpan.Zero)
                {
                    // Both negative: the larger magnitude is the smaller value.
                    var bothNegative = Expression.AndAlso(negativeA, CompareKeys(Mirror(op), keyA, key));
                    return below ? bothNegative : Expression.OrElse(positiveA, bothNegative);
                }
                var bothPositive = Expression.AndAlso(positiveA, CompareKeys(op, keyA, key));
                return below ? Expression.OrElse(negativeA, bothPositive) : bothPositive;
            }

            var textB = Text(Stored(b));
            var negativeB = CompareKeys(ExpressionType.LessThan, textB, Expression.Constant("0"));
            var positiveB = CompareKeys(ExpressionType.GreaterThanOrEqual, textB, Expression.Constant("0"));
            var keyB = TimeSpanKey(textB);
            var signsDiffer = below ? Expression.AndAlso(negativeA, positiveB) : Expression.AndAlso(positiveA, negativeB);
            return Expression.OrElse(
                Expression.OrElse(signsDiffer, Expression.AndAlso(Expression.AndAlso(positiveA, positiveB), CompareKeys(op, keyA, keyB))),
                Expression.AndAlso(Expression.AndAlso(negativeA, negativeB), CompareKeys(Mirror(op), keyA, keyB)));
        }

        /// <summary>
        /// The stored TimeSpan <paramref name="operand"/> reads: a member, or a member read as its default (TASK-899). Anything
        /// else was refused by <see cref="TimeSpanShapes{T}"/>, or is refused here.
        /// </summary>
        private static Expression Stored(Expression operand)
        {
            var unwrapped = Birko.Data.Expressions.SpanContains.UnwrapConvert(operand) ?? operand;
            if (unwrapped is MemberExpression or ConditionalExpression) return unwrapped;
            throw TimeSpanRefusal("a TimeSpan computed from a document member (" + unwrapped.NodeType + ")",
                "only a member's stored text can be compared as its value");
        }
    }
}
