using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Azure.Cosmos.Linq;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// The one pre-pass every Cosmos DB filter takes before it is rendered, by the store's LINQ provider and by the view
/// store's translator alike: <c>SpanContains</c>, <c>DateTruncation</c>, <c>NullOrderingFold</c>, then
/// <see cref="NullSemantics{T}"/>, which makes null and missing members answer what C# answers.
/// </summary>
/// <remarks>
/// <para><b>Why (TASK-896).</b> Measured on the emulator (vnext-preview, 2026-11-10), 93 filter shapes over nullable
/// members against the compiled expression, on reads, counts, aggregates and the reads behind <c>Update</c> /
/// <c>Delete</c>: 53 silently answered other rows and 14 threw, on every path. Cosmos DB's semantics, measured:</para>
/// <list type="bullet">
/// <item>An ordering with a null operand (<c>c.Score &gt; 20</c>) is undefined, and so is its <c>NOT</c>, so
/// <c>!(x.Score &gt; 20)</c> missed the null rows C# includes.</item>
/// <item>A property MISSING from the document (written before the member existed, or by a serializer that omits nulls)
/// is undefined under every operator: <c>c.S = null</c>, <c>c.S != "a"</c>, <c>c.S IN ("a", null)</c> all skip it, while
/// C# reads it as null.</item>
/// <item>The LINQ provider renders <c>HasValue</c> as <c>IS_DEFINED</c>, which is true of a stored null (8 rows
/// where C# has 5), and Cosmos DB's <c>??</c> replaces only undefined, never null.</item>
/// <item><c>IS_DEFINED(x)</c> and <c>IS_NULL(x)</c> are always true or false, so "null or missing" is
/// <c>(NOT IS_DEFINED(x) OR IS_NULL(x))</c> and "has a value" its negation; both are what this rewrite emits.</item>
/// </list>
/// <para><b>Polarity.</b> A comparison that is undefined where C# says false is harmless where it decides the result
/// directly (an undefined row is not returned), so a comparison reached through <c>&amp;&amp;</c>, <c>||</c> and an even
/// number of <c>!</c> keeps its rendering. Under an odd number of <c>!</c>, or inside anything else (a conditional's test,
/// a comparison of booleans), it is guarded with "has a value", so it is never undefined. <c>== null</c>,
/// <c>HasValue</c>, <c>!=</c>, a list holding <c>null</c>, two nullable operands compared for equality, <c>??</c> and
/// <c>GetValueOrDefault</c> are rewritten wherever they occur.</para>
/// <para><b>Strings.</b> <c>string.IsNullOrEmpty</c> is translated. <c>string.IsNullOrWhiteSpace</c> is refused: Cosmos DB's
/// <c>TRIM</c> keeps U+00A0, U+0085 and U+001C, which C# counts as white space (measured). A culture-sensitive comparison
/// (<c>string.Compare(a, b)</c>, <c>CompareTo</c>, an ignore-case or culture overload) is refused: the provider rendered it
/// as Cosmos DB's ordinal comparison, so <c>"C7"</c> vs <c>"c2"</c> came out the other way round. An ordinal one
/// (<c>string.CompareOrdinal</c>, <c>string.Compare(a, b, StringComparison.Ordinal)</c>) of a member against a value,
/// compared with 0, is translated, null ordered first as in C#. Cosmos DB orders strings by code point and .NET by UTF-16
/// unit, which differ only where a surrogate pair meets U+E000-U+FFFF (measured: U+1F600 sorts after U+FFFF), so a value
/// holding a character at or above U+D800 is refused; below it the two orders agree for every stored string.</para>
/// <para>Every refusal is a <see cref="NotSupportedException"/> before any request. The rewritten tree is for the
/// renderers; it is never compiled. A second pass over its output (a chunked write prepares each chunk again) leaves the
/// ordinal comparisons it produced alone and only repeats guards, which changes no row.</para>
/// <para><b>A non-nullable member a document omits (TASK-899)</b> is the type's default to C#, undefined to Cosmos DB:
/// measured, 34 of 44 shapes skipped such documents (<c>x.A == 0</c>, <c>!x.Del</c>, <c>x.E == New</c>, a list holding 0).
/// Against a value, a test its default passes is <c>NOT IS_DEFINED(m) OR test</c>, one it fails keeps its rendering
/// (guarded under <c>!</c>); in a computed operand the member is <c>IS_DEFINED(m) ? m : default</c>.</para>
/// <para><b>Nested predicates (TASK-899)</b> take the same rewrite, positive inside whatever the polarity outside;
/// <c>All(p)</c>, which the provider cannot render, is <c>!Any(!p)</c>.</para>
/// </remarks>
public static class CosmosFilterRewrite
{
    /// <summary>
    /// Every rewrite a Cosmos DB filter takes before rendering, in order. Returns <paramref name="filter"/> by reference
    /// when none applies.
    /// </summary>
    /// <exception cref="NotSupportedException">The filter holds a shape no Cosmos DB rendering answers as C# does.</exception>
    public static Expression<Func<T, bool>>? Prepare<T>(Expression<Func<T, bool>>? filter)
    {
        filter = Birko.Data.Expressions.SpanContains.Rewrite(filter);
        filter = Birko.Data.Expressions.DateTruncation.Rewrite(filter);   // TASK-224
        filter = Birko.Data.Expressions.NullOrderingFold.Rewrite(filter);   // TASK-867
        return NullSemantics(filter);   // TASK-896
    }

    /// <summary>
    /// Rewrites <paramref name="filter"/> so null and missing members answer what C# answers (see the remarks on
    /// <see cref="CosmosFilterRewrite"/>). Returns <paramref name="filter"/> by reference when nothing changes.
    /// </summary>
    /// <exception cref="NotSupportedException">A shape Cosmos DB cannot answer as C# does.</exception>
    public static Expression<Func<T, bool>>? NullSemantics<T>(Expression<Func<T, bool>>? filter)
    {
        if (filter == null) return null;
        var body = new Rewriter().Visit(filter.Body)!;
        return ReferenceEquals(body, filter.Body) ? filter : Expression.Lambda<Func<T, bool>>(body, filter.Parameters);
    }

    /// <summary>True when <paramref name="call"/> is Cosmos DB's <c>IS_DEFINED</c> marker this rewrite emits.</summary>
    internal static bool IsDefinedCall(MethodCallExpression call) => call.Method == IsDefinedMethod;

    /// <summary>True when <paramref name="call"/> is Cosmos DB's <c>IS_NULL</c> marker this rewrite emits.</summary>
    internal static bool IsNullCall(MethodCallExpression call) => call.Method == IsNullMethod;

    /// <summary>
    /// TASK-899: <paramref name="node"/> is the <c>absent OR list.Contains(x)</c> this rewrite makes of a list holding null
    /// (<c>NOT IS_DEFINED(x) OR IS_NULL(x)</c>, the list without its null) or of a list holding a non-nullable member's
    /// default (<c>NOT IS_DEFINED(x)</c>, the list whole). No stored value in the list matches a document whose member
    /// is null or missing, so the IN can be split with the absent leg in one chunk only (<c>CosmosInListChunking</c>).
    /// </summary>
    /// <param name="node">The node to test.</param>
    /// <param name="contains">The <c>Contains</c> on the right.</param>
    /// <param name="defaultValue">For the non-nullable form, the list's element default (the chunk holding it carries the
    /// leg, since preparing that chunk again adds the leg to it); otherwise <c>null</c>.</param>
    internal static bool TryAbsentOrIn(Expression node, out MethodCallExpression contains, out object? defaultValue)
    {
        contains = null!;
        defaultValue = null;
        if (node is not BinaryExpression { NodeType: ExpressionType.OrElse } or || or.Right is not MethodCallExpression call) return false;
        if (!TryContains(call, out _, out var item, out var elementType)) return false;
        var target = Birko.Data.Expressions.SpanContains.UnwrapConvert(item);

        if (IsNotDefinedOf(or.Left, target))
        {
            if (!elementType.IsValueType || Nullable.GetUnderlyingType(elementType) != null) return false;
            contains = call;
            defaultValue = Activator.CreateInstance(elementType);
            return true;
        }
        if (or.Left is BinaryExpression { NodeType: ExpressionType.OrElse } absent
            && IsNotDefinedOf(absent.Left, target)
            && absent.Right is MethodCallExpression isNull && IsNullCall(isNull)
            && ReferenceEquals(Birko.Data.Expressions.SpanContains.UnwrapConvert(isNull.Arguments[0]), target))
        {
            contains = call;
            return true;
        }
        return false;
    }

    private static bool IsNotDefinedOf(Expression test, Expression? target)
        => test is UnaryExpression { NodeType: ExpressionType.Not, Operand: MethodCallExpression isDefined }
           && IsDefinedCall(isDefined)
           && ReferenceEquals(Birko.Data.Expressions.SpanContains.UnwrapConvert(isDefined.Arguments[0]), target);

    private static readonly MethodInfo IsDefinedMethod =
        typeof(CosmosLinqExtensions).GetMethod(nameof(CosmosLinqExtensions.IsDefined), new[] { typeof(object) })!;

    private static readonly MethodInfo IsNullMethod =
        typeof(CosmosLinqExtensions).GetMethod(nameof(CosmosLinqExtensions.IsNull), new[] { typeof(object) })!;

    private static readonly MethodInfo EnumerableContains = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    // The ordinal string orderings this rewrite emits: the renderers see a plain `<`, `>`, ... between a member and a
    // value; a second pass recognises them by their declaring type. Only reached if the tree were ever compiled.
    private static bool OrdinalLessThan(string? a, string? b) => string.CompareOrdinal(a, b) < 0;
    private static bool OrdinalLessThanOrEqual(string? a, string? b) => string.CompareOrdinal(a, b) <= 0;
    private static bool OrdinalGreaterThan(string? a, string? b) => string.CompareOrdinal(a, b) > 0;
    private static bool OrdinalGreaterThanOrEqual(string? a, string? b) => string.CompareOrdinal(a, b) >= 0;

    private static MethodInfo OrdinalMethod(ExpressionType op) => typeof(CosmosFilterRewrite).GetMethod(op switch
    {
        ExpressionType.LessThan => nameof(OrdinalLessThan),
        ExpressionType.LessThanOrEqual => nameof(OrdinalLessThanOrEqual),
        ExpressionType.GreaterThan => nameof(OrdinalGreaterThan),
        _ => nameof(OrdinalGreaterThanOrEqual),
    }, BindingFlags.NonPublic | BindingFlags.Static)!;

    private enum Polarity { Positive, Negative, Exact }

    private sealed class Rewriter : ExpressionVisitor
    {
        private Polarity _polarity = Polarity.Positive;

        /// <summary>An undefined comparison here would change the answer, so it must be guarded.</summary>
        private bool MustGuard => _polarity != Polarity.Positive;

        private Expression In(Polarity polarity, Expression node)
        {
            var saved = _polarity;
            _polarity = polarity;
            try { return Visit(node)!; }
            finally { _polarity = saved; }
        }

        private Expression Exact(Expression node) => In(Polarity.Exact, node);

        private static Polarity Flip(Polarity p) => p switch
        {
            Polarity.Positive => Polarity.Negative,
            Polarity.Negative => Polarity.Positive,
            _ => Polarity.Exact,
        };

        public override Expression? Visit(Expression? node)
        {
            if (node == null) return null;
            switch (node)
            {
                case LambdaExpression lambda:
                    // TASK-899: a nested lambda is rewritten like the filter. A predicate is handled in VisitMethodCall;
                    // any other lambda (a selector) is an operand.
                    return Body(lambda, Exact(lambda.Body));
                case BinaryExpression:
                case UnaryExpression:
                case MethodCallExpression:
                case ConditionalExpression:
                case MemberExpression:
                case ConstantExpression:
                case ParameterExpression:
                    return base.Visit(node);
                default:
                    var saved = _polarity;
                    _polarity = Polarity.Exact;
                    try { return base.Visit(node); }
                    finally { _polarity = saved; }
            }
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not && node.Type == typeof(bool))
            {
                return node.Update(In(Flip(_polarity), node.Operand));
            }
            return node.Update(Exact(node.Operand));
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            // TASK-899: a missing member read as its default, produced by an earlier pass.
            if (node.Test is MethodCallExpression test && IsDefinedCall(test)) return node;
            return node.Update(Exact(node.Test), Visit(node.IfTrue)!, Visit(node.IfFalse)!);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == nameof(Nullable<int>.HasValue) && node.Expression != null
                && Nullable.GetUnderlyingType(node.Expression.Type) != null && Reads(node.Expression))
            {
                return Present(Exact(node.Expression));
            }

            // TASK-899: a non-nullable member missing from the document is its default to C#, undefined to Cosmos DB. A
            // comparison or a Contains of one is handled where it is made; here it is a bool predicate or an operand.
            if (IsDefaultable(node, out var member))
            {
                if (node.Type == typeof(bool) && _polarity == Polarity.Positive) return node;   // missing: C#'s false
                if (node.Type == typeof(bool) && _polarity == Polarity.Negative) return Expression.AndAlso(Defined(member), node);
                return Defaulted(node);
            }
            return node.Expression == null ? node : node.Update(Exact(node.Expression));
        }

        /// <summary><paramref name="lambda"/> with <paramref name="body"/>, or itself when the body did not change.</summary>
        private static LambdaExpression Body(LambdaExpression lambda, Expression body)
            => ReferenceEquals(body, lambda.Body) ? lambda : Expression.Lambda(lambda.Type, body, lambda.Name, lambda.TailCall, lambda.Parameters);

        protected override Expression VisitBinary(BinaryExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.AndAlso:
                case ExpressionType.OrElse:
                case ExpressionType.And when node.Type == typeof(bool):
                case ExpressionType.Or when node.Type == typeof(bool):
                    return node.Update(Visit(node.Left)!, node.Conversion, Visit(node.Right)!);
                case ExpressionType.Coalesce:
                    return Coalesce(node);
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    return Comparison(node);
                default:
                    return node.Update(Exact(node.Left), node.Conversion, Exact(node.Right));
            }
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var method = node.Method;

            // A marker an earlier pass produced (a chunked write prepares each chunk again): its operand is the member.
            if (IsDefinedCall(node) || IsNullCall(node)) return node;

            // TASK-899: nested predicates. The provider has no All (it threw DocumentQueryException), and C#'s All(p) is
            // !Any(!p), so it is rewritten to that and the negated predicate is guarded like any other negation. A
            // predicate (Any, Where, Count, First, ...) keeps the elements it is true of, so its body is positive whatever
            // the polarity outside: an element whose comparison is undefined is not kept, which is C#'s false.
            if (method.DeclaringType == typeof(Enumerable) && node.Arguments.Count == 2
                && node.Arguments[1] is LambdaExpression predicate && predicate.ReturnType == typeof(bool))
            {
                if (method.Name == nameof(Enumerable.All))
                {
                    var any = EnumerableAny.MakeGenericMethod(method.GetGenericArguments()[0]);
                    var negated = Expression.Lambda(predicate.Type, Expression.Not(predicate.Body), predicate.Parameters);
                    return Visit(Expression.Not(Expression.Call(any, node.Arguments[0], negated)))!;
                }
                if (PredicateMethods.Contains(method.Name))
                {
                    return node.Update(null, new[] { Exact(node.Arguments[0]), Body(predicate, In(Polarity.Positive, predicate.Body)) });
                }
            }

            if (method.Name == nameof(Nullable<int>.GetValueOrDefault) && node.Object != null
                && Nullable.GetUnderlyingType(node.Object.Type) != null && Reads(node.Object))
            {
                var operand = Exact(node.Object);
                var fallback = node.Arguments.Count == 1
                    ? Exact(node.Arguments[0])
                    : Expression.Constant(Activator.CreateInstance(node.Type), node.Type);
                return Expression.Condition(Present(operand), Expression.Property(operand, "Value"), fallback, node.Type);
            }

            if (method.DeclaringType == typeof(string) && method.IsStatic && node.Arguments.Count == 1 && Reads(node.Arguments[0]))
            {
                if (method.Name == nameof(string.IsNullOrEmpty))
                {
                    var s = Exact(node.Arguments[0]);
                    return Expression.OrElse(NullOrMissing(s), Expression.Equal(s, Expression.Constant(string.Empty)));
                }
                if (method.Name == nameof(string.IsNullOrWhiteSpace))
                {
                    throw Refusal("string.IsNullOrWhiteSpace",
                        "Cosmos DB's TRIM keeps U+00A0, U+0085 and U+001C, which C# counts as white space, so no rendering "
                        + "matches it; compare with string.IsNullOrEmpty or explicit values instead");
                }
            }

            if (IsStringCompare(node) && Reads(node))
            {
                throw Refusal(Describe(node), "a string comparison is supported only as an ordinal one "
                    + "(string.CompareOrdinal or string.Compare(a, b, StringComparison.Ordinal)) of a member against a value, "
                    + "compared with 0");
            }

            if (TryContains(node, out var source, out var item, out var elementType) && MayBeNull(item))
            {
                return Contains(node, source, item, elementType);
            }

            // TASK-899: a list tested against a non-nullable member a document may omit: C# tests its default.
            if (TryContains(node, out _, out var tested, out _) && IsDefaultable(tested, out var testedMember))
            {
                return MissingAsDefault(node, testedMember, node);
            }

            return node.Update(node.Object == null ? null : Exact(node.Object), node.Arguments.Select(Exact));
        }

        private Expression Coalesce(BinaryExpression node)
        {
            if (node.Conversion != null || !Reads(node.Left))
            {
                return node.Update(Exact(node.Left), node.Conversion, Exact(node.Right));
            }

            var left = Exact(node.Left);
            var right = Exact(node.Right);
            Expression value = left;
            if (value.Type != node.Type && Nullable.GetUnderlyingType(value.Type) != null)
            {
                value = Expression.Property(value, "Value");
            }
            if (value.Type != node.Type) value = Expression.Convert(value, node.Type);
            if (right.Type != node.Type) right = Expression.Convert(right, node.Type);
            return Expression.Condition(Present(left), value, right, node.Type);
        }

        private Expression Comparison(BinaryExpression node)
        {
            // Produced by an earlier pass: an ordinal string ordering.
            if (node.Method?.DeclaringType == typeof(CosmosFilterRewrite)) return node;

            if (TryStringCompare(node, out var compared)) return compared;

            // TASK-899: a non-nullable member is kept as it is, so a missing one can be answered below.
            var a = IsDefaultable(node.Left, out _) ? node.Left : Exact(node.Left);
            var b = IsDefaultable(node.Right, out _) ? node.Right : Exact(node.Right);
            var op = node.NodeType;

            // The document side on the left: the view translator renders `field op value` only.
            if (!Reads(a) && Reads(b) && TryMake(Mirror(op), b, a, node.IsLiftedToNull, out _))
            {
                (a, b) = (b, a);
                op = Mirror(op);
            }
            if (!Reads(a))
            {
                return Rebuild(node, op, a, b);
            }

            // TASK-899: against a value, a missing member answers what its default answers; against another member,
            // each side is read as its default where it is missing.
            if (Reads(b))
            {
                a = Defaulted(a);
                b = Defaulted(b);
            }
            else if (IsDefaultable(a, out var missing))
            {
                return MissingAsDefault(Rebuild(node, op, a, b), missing, CompareValues(node, op, a, b));
            }
            return CompareValues(node, op, a, b);
        }

        /// <summary>A comparison of a document operand <paramref name="a"/> with <paramref name="b"/>, null as C# reads it.</summary>
        private Expression CompareValues(BinaryExpression node, ExpressionType op, Expression a, Expression b)
        {
            var aNullable = MayBeNull(a);
            var bNullable = MayBeNull(b);

            if (op is ExpressionType.Equal or ExpressionType.NotEqual && IsNullValue(b))
            {
                var isNull = aNullable ? NullOrMissing(a) : Expression.Constant(false);
                if (op == ExpressionType.Equal) return isNull;
                return aNullable ? Present(a) : Expression.Constant(true);
            }

            if (!aNullable && !bNullable)
            {
                return Rebuild(node, op, a, b);
            }

            switch (op)
            {
                case ExpressionType.NotEqual:
                    // C#'s null != 5 is true and a missing member is undefined: the equality, guarded, negated.
                    return Expression.Not(Equality(node, a, b, aNullable, bNullable, Flip(_polarity)));
                case ExpressionType.Equal:
                    return Equality(node, a, b, aNullable, bNullable, _polarity);
                default:
                    var ordering = Rebuild(node, op, a, b);
                    return MustGuard ? Guard(ordering, a, aNullable, b, bNullable) : ordering;
            }
        }

        private static Expression Equality(BinaryExpression node, Expression a, Expression b, bool aNullable, bool bNullable, Polarity polarity)
        {
            var equal = Rebuild(node, ExpressionType.Equal, a, b);
            if (aNullable && bNullable)
            {
                // C#: two nulls are equal. Cosmos DB: two missing members are undefined.
                return Expression.OrElse(
                    Expression.AndAlso(NullOrMissing(a), NullOrMissing(b)),
                    Guard(equal, a, true, b, true));
            }
            return polarity == Polarity.Positive ? equal : Guard(equal, a, aNullable, b, bNullable);
        }

        /// <summary>
        /// TASK-899: <paramref name="rendered"/>, the test of <paramref name="member"/> against a value, answered for a
        /// document that omits the member as <paramref name="condition"/> answers for the member's default: where the
        /// default passes, <c>NOT IS_DEFINED(m) OR test</c>; where it fails, the test, guarded with <c>IS_DEFINED(m)</c>
        /// where an undefined test would change the answer. A plain test keeps its rendering, and its index.
        /// </summary>
        private Expression MissingAsDefault(Expression condition, MemberExpression member, Expression rendered)
        {
            if (rendered is ConstantExpression) return rendered;
            var atDefault = new Replacer(member, DefaultOf(member.Type)).Visit(condition)!;
            if (Evaluate(atDefault) is true) return Expression.OrElse(Expression.Not(Defined(member)), rendered);
            return MustGuard ? Expression.AndAlso(Defined(member), rendered) : rendered;
        }

        private static Expression Guard(Expression comparison, Expression a, bool aNullable, Expression b, bool bNullable)
        {
            var guarded = comparison;
            if (bNullable && Reads(b)) guarded = Expression.AndAlso(Present(b), guarded);
            if (aNullable) guarded = Expression.AndAlso(Present(a), guarded);
            return guarded;
        }

        private Expression Contains(MethodCallExpression node, Expression source, Expression item, Type elementType)
        {
            var visitedItem = Exact(item);
            var values = Evaluate(source) as IEnumerable;
            var nonNull = new List<object>();
            var hasNull = false;
            if (values != null)
            {
                foreach (var v in values)
                {
                    if (v == null) hasNull = true;
                    else nonNull.Add(v);
                }
            }

            if (!hasNull)
            {
                var plain = node.Object != null
                    ? node.Update(node.Object, new[] { visitedItem })
                    : node.Update(null, new[] { node.Arguments[0], visitedItem });
                return MustGuard ? Expression.AndAlso(Present(visitedItem), plain) : plain;
            }

            // `IN (.., null)` matches a stored null but not a missing member: null-or-missing, or one of the values.
            if (nonNull.Count == 0) return NullOrMissing(visitedItem);
            var array = Array.CreateInstance(elementType, nonNull.Count);
            for (var i = 0; i < nonNull.Count; i++) array.SetValue(nonNull[i], i);
            var inList = Expression.Call(EnumerableContains.MakeGenericMethod(elementType),
                Expression.Constant(array, elementType.MakeArrayType()), visitedItem);
            return Expression.OrElse(NullOrMissing(visitedItem), inList);
        }

        /// <summary>
        /// <c>Compare(m, s) op k</c> (or <c>k op Compare(..)</c>) for the string comparison family: an ordinal one of a
        /// member against a value, with k = 0, translated with C#'s null ordering; anything else refused by name.
        /// </summary>
        private bool TryStringCompare(BinaryExpression node, out Expression result)
        {
            result = node;
            var op = node.NodeType;
            var leftCall = Birko.Data.Expressions.SpanContains.UnwrapConvert(node.Left) as MethodCallExpression;
            var rightCall = Birko.Data.Expressions.SpanContains.UnwrapConvert(node.Right) as MethodCallExpression;
            MethodCallExpression call;
            Expression other;
            if (leftCall != null && IsStringCompare(leftCall) && Reads(leftCall))
            {
                call = leftCall;
                other = node.Right;
            }
            else if (rightCall != null && IsStringCompare(rightCall) && Reads(rightCall))
            {
                call = rightCall;
                other = node.Left;
                op = Mirror(op);
            }
            else
            {
                return false;
            }

            var what = Describe(call);
            if (!IsOrdinal(call))
            {
                throw Refusal(what, "it compares by culture (or ignoring case), and Cosmos DB compares strings ordinally by "
                    + "code point; use string.CompareOrdinal or string.Compare(a, b, StringComparison.Ordinal)");
            }
            if (Reads(other) || Evaluate(other) is not int k || k != 0)
            {
                throw Refusal(what, "an ordinal comparison's result is translated only when compared with the constant 0");
            }

            var first = call.Arguments[0];
            var second = call.Arguments[1];
            Expression member;
            Expression valueSide;
            if (Reads(first) && !Reads(second))
            {
                member = first;
                valueSide = second;
            }
            else if (Reads(second) && !Reads(first))
            {
                member = second;
                valueSide = first;
                op = Mirror(op);
            }
            else
            {
                throw Refusal(what, "it compares two document members; Cosmos DB orders by code point and .NET by UTF-16 "
                    + "unit, which differ where a surrogate pair meets U+E000-U+FFFF");
            }

            if (Birko.Data.Expressions.SpanContains.UnwrapConvert(member) is not MemberExpression)
            {
                throw Refusal(what, "the document side is a computed expression, not a member");
            }
            var value = Evaluate(valueSide) as string;
            if (value != null && value.Any(ch => ch >= '\uD800'))
            {
                throw Refusal(what, "the value holds a character at or above U+D800, where Cosmos DB's code-point order and "
                    + ".NET's UTF-16 order can disagree");
            }

            // C#: null sorts before every string and equals null.
            var whenNull = Holds(op, value == null ? 0 : -1);
            Expression whenPresent;
            if (value == null)
            {
                whenPresent = Expression.Constant(Holds(op, 1));
            }
            else
            {
                var constant = Expression.Constant(value, typeof(string));
                whenPresent = op switch
                {
                    ExpressionType.Equal => Expression.Equal(member, constant),
                    ExpressionType.NotEqual => Expression.Not(Expression.Equal(member, constant)),
                    _ => Expression.MakeBinary(op, member, constant, false, OrdinalMethod(op)),
                };
            }

            result = (whenNull, whenPresent) switch
            {
                (true, ConstantExpression { Value: true }) => Expression.Constant(true),
                (false, ConstantExpression { Value: false }) => Expression.Constant(false),
                (true, ConstantExpression) => NullOrMissing(member),
                (false, ConstantExpression) => Present(member),
                (true, _) => Expression.OrElse(NullOrMissing(member), whenPresent),
                _ => Expression.AndAlso(Present(member), whenPresent),
            };
            return true;
        }

        private static bool Holds(ExpressionType op, int comparison) => op switch
        {
            ExpressionType.Equal => comparison == 0,
            ExpressionType.NotEqual => comparison != 0,
            ExpressionType.LessThan => comparison < 0,
            ExpressionType.LessThanOrEqual => comparison <= 0,
            ExpressionType.GreaterThan => comparison > 0,
            _ => comparison >= 0,
        };

        private static bool IsStringCompare(MethodCallExpression call)
        {
            var m = call.Method;
            if (m.DeclaringType != typeof(string)) return false;
            return m.Name is nameof(string.Compare) or nameof(string.CompareOrdinal) or nameof(string.CompareTo);
        }

        private static bool IsOrdinal(MethodCallExpression call)
        {
            var parameters = call.Method.GetParameters();
            if (call.Method.Name == nameof(string.CompareOrdinal)) return parameters.Length == 2;
            if (call.Method.Name != nameof(string.Compare) || parameters.Length != 3) return false;
            return parameters[2].ParameterType == typeof(StringComparison)
                   && !Reads(call.Arguments[2])
                   && Evaluate(call.Arguments[2]) is StringComparison.Ordinal;
        }

        private static string Describe(MethodCallExpression call)
            => (call.Method.IsStatic ? "string." : "string instance ") + call.Method.Name
               + "(" + string.Join(", ", call.Method.GetParameters().Select(p => p.ParameterType.Name)) + ")";
    }

    // ----- shared helpers -----

    private static Expression Rebuild(BinaryExpression node, ExpressionType op, Expression a, Expression b)
    {
        if (op == node.NodeType && ReferenceEquals(a, node.Left) && ReferenceEquals(b, node.Right)) return node;
        if (op == node.NodeType && a.Type == node.Left.Type && b.Type == node.Right.Type)
        {
            return Expression.MakeBinary(op, a, b, node.IsLiftedToNull, node.Method);
        }
        return Expression.MakeBinary(op, a, b, node.IsLiftedToNull, null);
    }

    private static bool TryMake(ExpressionType op, Expression a, Expression b, bool liftToNull, out Expression? made)
    {
        try
        {
            made = Expression.MakeBinary(op, a, b, liftToNull, null);
            return true;
        }
        catch (InvalidOperationException)
        {
            made = null;
            return false;
        }
    }

    private static ExpressionType Mirror(ExpressionType op) => op switch
    {
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        _ => op,
    };

    /// <summary>"Has a value": <c>IS_DEFINED(x) AND NOT IS_NULL(x)</c>, never undefined.</summary>
    private static Expression Present(Expression operand)
    {
        var target = Box(operand);
        return Expression.AndAlso(Expression.Call(IsDefinedMethod, target), Expression.Not(Expression.Call(IsNullMethod, target)));
    }

    /// <summary>"Null or missing": <c>NOT IS_DEFINED(x) OR IS_NULL(x)</c>, never undefined.</summary>
    private static Expression NullOrMissing(Expression operand)
    {
        var target = Box(operand);
        return Expression.OrElse(Expression.Not(Expression.Call(IsDefinedMethod, target)), Expression.Call(IsNullMethod, target));
    }

    /// <summary>TASK-899: <c>IS_DEFINED(m)</c>: the document holds the member (a non-nullable one is never stored null).</summary>
    private static Expression Defined(MemberExpression member) => Expression.Call(IsDefinedMethod, Box(member));

    private static ConstantExpression DefaultOf(Type type) => Expression.Constant(Activator.CreateInstance(type), type);

    /// <summary>
    /// TASK-899: <paramref name="operand"/> with its non-nullable member read as <c>IS_DEFINED(m) ? m : default</c>, the
    /// value C# reads for a member the document omits; any other operand unchanged.
    /// </summary>
    private static Expression Defaulted(Expression operand)
    {
        if (!IsDefaultable(operand, out var member)) return operand;
        var read = Expression.Condition(Defined(member), member, DefaultOf(member.Type), member.Type);
        return new Replacer(member, read).Visit(operand)!;
    }

    /// <summary>
    /// TASK-899: <paramref name="operand"/> (through a conversion) is a member of a non-nullable value type, read off a
    /// document object along a chain of members from a lambda parameter: one a stored document can omit, which C# then reads
    /// as the type's default. <c>Nullable&lt;T&gt;.Value</c>, a struct's member (<c>DateTime.Year</c>) and a string's or a
    /// collection's member are not document fields; a captured variable does not read the document.
    /// </summary>
    private static bool IsDefaultable(Expression operand, out MemberExpression member)
    {
        member = null!;
        if (Birko.Data.Expressions.SpanContains.UnwrapConvert(operand) is not MemberExpression m || m.Expression == null) return false;
        if (!m.Type.IsValueType || Nullable.GetUnderlyingType(m.Type) != null) return false;
        Expression? current = m;
        while (current is MemberExpression link)
        {
            var owner = link.Expression?.Type;
            if (owner == null || !owner.IsClass || owner == typeof(string) || typeof(IEnumerable).IsAssignableFrom(owner)) return false;
            current = link.Expression;
        }
        if (current is not ParameterExpression) return false;
        member = m;
        return true;
    }

    /// <summary>The Enumerable methods whose lambda is a predicate that keeps the elements it is true of (TASK-899).</summary>
    private static readonly HashSet<string> PredicateMethods = new(StringComparer.Ordinal)
    {
        nameof(Enumerable.Any), nameof(Enumerable.Where), nameof(Enumerable.Count), nameof(Enumerable.LongCount),
        nameof(Enumerable.First), nameof(Enumerable.FirstOrDefault), nameof(Enumerable.Single),
        nameof(Enumerable.SingleOrDefault), nameof(Enumerable.Last), nameof(Enumerable.LastOrDefault),
    };

    private static readonly MethodInfo EnumerableAny = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2);

    /// <summary>Replaces one node, by reference.</summary>
    private sealed class Replacer : ExpressionVisitor
    {
        private readonly Expression _target;
        private readonly Expression _replacement;

        public Replacer(Expression target, Expression replacement)
        {
            _target = target;
            _replacement = replacement;
        }

        public override Expression? Visit(Expression? node)
            => ReferenceEquals(node, _target) ? _replacement : base.Visit(node);
    }

    private static Expression Box(Expression operand)
    {
        var unwrapped = Birko.Data.Expressions.SpanContains.UnwrapConvert(operand) ?? operand;
        return unwrapped.Type.IsValueType ? Expression.Convert(unwrapped, typeof(object)) : unwrapped;
    }

    /// <summary>The operand reads the document and its value can be null (a nullable value type or a reference type).</summary>
    private static bool MayBeNull(Expression operand)
    {
        if (!Reads(operand)) return false;
        var unwrapped = Birko.Data.Expressions.SpanContains.UnwrapConvert(operand) ?? operand;
        var type = unwrapped.Type;
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }

    /// <summary>The operand does not read the document and evaluates to null.</summary>
    private static bool IsNullValue(Expression operand)
    {
        if (Reads(operand)) return false;
        if (operand.Type.IsValueType && Nullable.GetUnderlyingType(operand.Type) == null) return false;
        try
        {
            return Evaluate(operand) == null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static bool TryContains(MethodCallExpression node, out Expression source, out Expression item, out Type elementType)
    {
        source = null!;
        item = null!;
        elementType = null!;
        if (node.Method.IsGenericMethod && node.Method.GetGenericMethodDefinition() == EnumerableContains)
        {
            source = node.Arguments[0];
            item = node.Arguments[1];
            elementType = node.Method.GetGenericArguments()[0];
        }
        else if (node.Object != null && node.Method.Name == nameof(List<object>.Contains) && node.Arguments.Count == 1
                 && node.Object.Type != typeof(string)
                 && typeof(IEnumerable<>).MakeGenericType(node.Method.GetParameters()[0].ParameterType).IsAssignableFrom(node.Object.Type))
        {
            source = node.Object;
            item = node.Arguments[0];
            elementType = node.Method.GetParameters()[0].ParameterType;
        }
        else
        {
            return false;
        }
        return !Reads(source) && Reads(item);
    }

    private static object? Evaluate(Expression expr)
    {
        switch (expr)
        {
            case ConstantExpression c:
                return c.Value;
            case MemberExpression m when m.Member is FieldInfo f:
                return f.GetValue(m.Expression == null ? null : Evaluate(m.Expression));
            case MemberExpression m when m.Member is PropertyInfo p:
                return p.GetValue(m.Expression == null ? null : Evaluate(m.Expression));
            case UnaryExpression u when u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked && u.Method == null
                                        && (Nullable.GetUnderlyingType(u.Type) == u.Operand.Type || u.Type == typeof(object)):
                return Evaluate(u.Operand);
            default:
                return Expression.Lambda<Func<object?>>(Expression.Convert(expr, typeof(object))).Compile()();
        }
    }

    private static bool Reads(Expression expr)
    {
        var finder = new ParameterFinder();
        finder.Visit(expr);
        return finder.Found;
    }

    private static NotSupportedException Refusal(string what, string why)
        => new($"A Cosmos DB filter cannot translate {what} as C# evaluates it: {why}. (TASK-896)");

    private sealed class ParameterFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node) => Found || node == null ? node : base.Visit(node);

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found = true;
            return node;
        }
    }
}
