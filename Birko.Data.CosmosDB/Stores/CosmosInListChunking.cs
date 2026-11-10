using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// TASK-834: splits a filter whose <c>list.Contains(x.Col)</c> is longer than the chunk size into one filter per
/// chunk of the list, so a long list stays under Cosmos DB's query-text ceiling. Since TASK-842 a chunk is cut by the
/// length of its query text as well as by count, so a list of long strings is split smaller instead of refused.
/// </summary>
/// <remarks>
/// <para>The Cosmos LINQ provider writes the list into the query TEXT as <c>IN (...)</c>, not as a parameter, and the
/// server refuses a query text over 524,288 characters (SC3020, 400 Bad Request). Measured on the emulator
/// (vnext-preview, 2026-11-07): a filtered read accepts 13,105 Guids and refuses 13,106, 40 characters per Guid.</para>
/// <para>The rules are TASK-833's (the user's decision): a list is split only when its IN is a plain AND conjunct of the
/// filter, its values are scalar value types or strings, and the read has no sort, limit or offset. Then <c>IN(L) AND R</c>
/// is the union over the chunks of <c>IN(Lk) AND R</c>, and with the values deduplicated the chunks are disjoint, so
/// counts add up.</para>
/// <para>Strings are split here and refused by the SQL planner (<c>InListChunkPlanner</c>), and the difference is the
/// provider's comparison. Under a case- or accent-insensitive SQL collation two distinct C# strings can be one stored
/// value, so a row could match two chunks (TASK-833). Cosmos DB has no collation: <c>IN</c> and <c>ARRAY_CONTAINS</c>
/// match only the ordinally equal string. Measured on the emulator (vnext-preview, 2026-11-07, TASK-841): "a" / "A",
/// "a" / "a ", U+00E9 / "e"+U+0301 and "Straße" / "STRASSE" each match only themselves, through both. So strings
/// are deduplicated ordinally, like the default <see cref="string.Equals(string)"/>, and their chunks are disjoint.
/// A shape that cannot be split still runs as one query while its text fits, so nothing that worked before refuses;
/// past the ceiling it is refused with <see cref="NotSupportedException"/> before any request, naming the operation,
/// the count, the limit and the reason, never the values. The caller's expression is never modified, and an empty
/// list is never chunked.</para>
/// </remarks>
internal static class CosmosInListChunking
{
    /// <summary>
    /// The most values per chunk: 40 KB of query text for Guids, a thirteenth of the measured ceiling. A ceiling, not the
    /// only rule: a chunk also ends where its query text would pass the budget (TASK-842).
    /// </summary>
    public const int DefaultChunkSize = 1000;

    /// <summary>Cosmos DB's query-text limit in characters (the server's SC3020 message names it).</summary>
    public const int DefaultMaxQueryTextLength = 524288;

    /// <summary>
    /// TASK-842: a chunk's query text is kept this far under the limit, a sixteenth of it (32,768 characters by
    /// default). Every chunk is measured with the store's own rendering before any request, and on the emulator that
    /// rendering is exactly what is counted: characters, not UTF-8 bytes (300,000 U+00E9 pass), with 524,253 accepted
    /// and 524,453 refused (vnext-preview, 2026-11-07). The margin is for what that did not measure, the production
    /// service; it costs at most one chunk in sixteen, and nothing for Guid lists, whose 1,000-value chunks are 40 KB.
    /// </summary>
    public static int MarginFor(int maxQueryTextLength) => maxQueryTextLength / 16;

    /// <summary>
    /// Plans the filter. Returns <c>null</c> when it runs as one query, or the chunk filters in list order.
    /// </summary>
    /// <remarks>
    /// TASK-842: a list is split when it has more than <paramref name="chunkSize"/> distinct values, or when the query
    /// would be over the budget (<paramref name="maxQueryTextLength"/> less <see cref="MarginFor"/>). Chunks are cut by
    /// serialized length: values are added while the chunk's estimated query text stays within the budget and its count
    /// within <paramref name="chunkSize"/>; each chunk is then measured with <paramref name="queryTextLength"/>, and one
    /// over the budget is halved until it fits. A single value whose query is still over the limit is refused by name.
    /// </remarks>
    /// <param name="queryTextLength">The length of the query text the store would send for a filter; local, no request.</param>
    /// <param name="unsplittableReason">Why the operation cannot run in chunks when <paramref name="sortedOrPaged"/> is
    /// set; the refusal names it. Defaults to the sort, limit or offset of a read.</param>
    /// <exception cref="NotSupportedException">The list is past the ceiling and the filter cannot be split.</exception>
    public static IReadOnlyList<Expression<Func<T, bool>>>? Plan<T>(
        Expression<Func<T, bool>>? filter,
        string operation,
        int chunkSize,
        int maxQueryTextLength,
        bool sortedOrPaged,
        Func<Expression<Func<T, bool>>, int> queryTextLength,
        string? unsplittableReason = null)
    {
        if (filter == null) return null;
        if (chunkSize < 1)
            throw new InvalidOperationException($"The IN-list chunk size must be at least 1; it is {chunkSize}.");

        var lists = InListFinder.Find(filter);
        if (lists.Count == 0) return null;
        var budget = maxQueryTextLength - MarginFor(maxQueryTextLength);

        var big = lists.Where(l => l.Values.Count > chunkSize).ToList();
        int? whole = null;
        if (big.Count == 0)
        {
            // Within the count ceiling, a list is split only when its values cannot fit; the estimate spares a short
            // list the rendering.
            if (lists.Sum(l => l.EstimatedLength) <= budget) return null;
            whole = queryTextLength(filter);
            if (whole <= budget) return null;
            big = lists.Where(l => l.Values.Count > 0).ToList();
            if (big.Count == 0) return null;
        }

        var blocker = big.Where(l => l.Reason != null).OrderByDescending(l => l.Values.Count).FirstOrDefault();
        if (blocker != null || sortedOrPaged)
        {
            var reason = blocker?.Reason ?? unsplittableReason ?? "the read has a sort, limit or offset, which chunks cannot reproduce";
            var count = (blocker ?? big.OrderByDescending(l => l.Values.Count).First()).Values.Count;
            var length = whole ?? queryTextLength(filter);
            if (length <= maxQueryTextLength) return null;
            throw Refusal<T>(operation, count, length, maxQueryTextLength, chunkSize, reason);
        }

        var target = big.OrderByDescending(l => l.EstimatedLength).ThenByDescending(l => l.Values.Count).First();
        var leadingChunk = true;
        Expression<Func<T, bool>> Build(IEnumerable<object?> values)
            => Expression.Lambda<Func<T, bool>>(
                new NodeReplacer(target.Replaced, target.WithValues(values, leadingChunk)).Visit(filter.Body)!, filter.Parameters);

        // The rest of the query, estimated from one value's rendering; the measurement of each chunk is what decides.
        var first = target.Values[0];
        var overhead = Math.Max(0, queryTextLength(Build(new[] { first })) - InList.Cost(first));

        var chunks = new List<Expression<Func<T, bool>>>();
        var slice = new List<object?>();
        var sliceLength = overhead;
        foreach (var value in target.Values)
        {
            var cost = InList.Cost(value);
            if (slice.Count > 0 && (slice.Count >= chunkSize || sliceLength + cost > budget))
            {
                AddMeasured(slice);
                slice = new List<object?>();
                sliceLength = overhead;
            }
            slice.Add(value);
            sliceLength += cost;
        }
        AddMeasured(slice);

        // A list split only for its length that still fits one chunk is the original query.
        return chunks.Count == 1 ? null : chunks;

        void AddMeasured(List<object?> values)
        {
            var chunk = Build(values);
            var length = queryTextLength(chunk);
            if (length <= budget || (values.Count == 1 && length <= maxQueryTextLength))
            {
                chunks.Add(chunk);
                leadingChunk = false;
                return;
            }
            if (values.Count > 1)
            {
                var half = values.Count / 2;
                AddMeasured(values.GetRange(0, half));
                AddMeasured(values.GetRange(half, values.Count - half));
                return;
            }

            var valueLength = InList.Cost(values[0]);
            if (length - valueLength > maxQueryTextLength)
            {
                throw Refusal<T>(operation, target.Values.Count, length, maxQueryTextLength, chunkSize,
                    "even one chunk is over the limit: the rest of the filter (another long IN list, for one) is too long");
            }
            throw new NotSupportedException(
                $"Cosmos DB {operation} of {typeof(T).Name} refused: one value of its IN list (of {target.Values.Count} "
                + $"distinct values) serializes to about {valueLength} characters, and a query holding that value alone "
                + $"would be {length} characters, over Cosmos DB's limit of {maxQueryTextLength}. No chunk can hold it. "
                + "Narrow the filter, or match that value another way. (TASK-842)");
        }
    }

    private static NotSupportedException Refusal<T>(string operation, int count, int length, int max, int chunkSize, string reason)
        => new($"Cosmos DB {operation} of {typeof(T).Name} refused: its IN list has {count} distinct values and the query "
             + $"text would be {length} characters, over Cosmos DB's limit of {max}. It cannot be split into chunks of "
             + $"{chunkSize} because {reason}. Split the list in the caller, or narrow the filter.");

    /// <summary>
    /// Runs a write once per chunk, in order. Cosmos DB has no transaction across partitions, so a chunk that fails
    /// does not undo the chunks before it (the user's decision): from the second chunk on, the failure is reported as
    /// a <see cref="CosmosChunkedWriteException"/> naming how many chunks applied. A failure in the first chunk, and a
    /// cancellation, propagate as themselves, since nothing beyond what an unchunked write would leave has changed.
    /// </summary>
    public static async Task RunWritesAsync<T>(IReadOnlyList<Expression<Func<T, bool>>> chunks, string operation, Func<Expression<Func<T, bool>>, Task> write)
    {
        for (var i = 0; i < chunks.Count; i++)
        {
            try
            {
                await write(chunks[i]).ConfigureAwait(false);
            }
            catch (Exception ex) when (i > 0 && ex is not OperationCanceledException)
            {
                throw new CosmosChunkedWriteException(operation, typeof(T), i, chunks.Count, ex);
            }
        }
    }

    /// <inheritdoc cref="RunWritesAsync{T}"/>
    public static void RunWrites<T>(IReadOnlyList<Expression<Func<T, bool>>> chunks, string operation, Action<Expression<Func<T, bool>>> write)
    {
        for (var i = 0; i < chunks.Count; i++)
        {
            try
            {
                write(chunks[i]);
            }
            catch (Exception ex) when (i > 0 && ex is not OperationCanceledException)
            {
                throw new CosmosChunkedWriteException(operation, typeof(T), i, chunks.Count, ex);
            }
        }
    }

    /// <summary>One <c>list.Contains(item)</c> in the filter, with its distinct values and why it cannot be split.</summary>
    private sealed class InList
    {
        public InList(MethodCallExpression node, Expression item, Type elementType, List<object?> values, string? reason,
            BinaryExpression? absentOrIn = null)
        {
            Node = node;
            Item = item;
            ElementType = elementType;
            Values = values;
            Reason = reason;
            AbsentOrIn = absentOrIn;
        }

        public MethodCallExpression Node { get; }

        /// <summary>
        /// TASK-899: the <c>absent OR IN</c> the null semantics made of this list (<c>CosmosFilterRewrite.TryAbsentOrIn</c>),
        /// or <c>null</c>. Its absent leg goes with the leading chunk only: no stored value of the list matches a null or
        /// missing member, so the chunks stay disjoint.
        /// </summary>
        public BinaryExpression? AbsentOrIn { get; }

        /// <summary>The node a chunk replaces: the <c>absent OR IN</c>, or the IN itself.</summary>
        public Expression Replaced => AbsentOrIn ?? (Expression)Node;
        public Expression Item { get; }
        public Type ElementType { get; }
        public List<object?> Values { get; }
        /// <summary><c>null</c> when the list can be split.</summary>
        public string? Reason { get; }

        private int? _estimatedLength;

        /// <summary>The values' estimated length in a query, separators included (TASK-842).</summary>
        public int EstimatedLength => _estimatedLength ??= Values.Sum(Cost);

        /// <summary>
        /// One value's estimated length in a query: its JSON plus a separator. JSON is how the view store measures a
        /// parameter, and it escapes at least as much as the LINQ provider's literals; each chunk is measured anyway.
        /// </summary>
        public static int Cost(object? value)
            => (value == null ? 4 : System.Text.Json.JsonSerializer.Serialize(value, value.GetType()).Length) + 2;

        /// <summary>
        /// <c>new List&lt;E&gt;(values).Contains(item)</c>: the List spelling the Cosmos provider renders as IN; for the
        /// leading chunk of an <c>absent OR IN</c>, with the absent leg in front.
        /// </summary>
        public Expression WithValues(IEnumerable<object?> values, bool leadingChunk)
        {
            var listType = typeof(List<>).MakeGenericType(ElementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var v in values) list.Add(v);
            var contains = listType.GetMethod(nameof(List<object>.Contains), new[] { ElementType })!;
            Expression chunk = Expression.Call(Expression.Constant(list, listType), contains, Item);
            return AbsentOrIn != null && leadingChunk ? Expression.OrElse(AbsentOrIn.Left, chunk) : chunk;
        }
    }

    /// <summary>Finds every IN in the filter and records whether the path from the root reaches it through AND alone.</summary>
    private sealed class InListFinder : ExpressionVisitor
    {
        private static readonly MethodInfo EnumerableContains = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

        private readonly Stack<Expression> _ancestors = new();
        private readonly HashSet<Expression> _conjuncts = new(ReferenceEqualityComparer.Instance);
        private readonly List<InList> _found = new();

        public static List<InList> Find(LambdaExpression filter)
        {
            var finder = new InListFinder();
            finder.CollectConjuncts(filter.Body);
            finder.Visit(filter.Body);
            return finder._found;
        }

        private void CollectConjuncts(Expression node)
        {
            if (node.NodeType == ExpressionType.AndAlso && node is BinaryExpression and)
            {
                CollectConjuncts(and.Left);
                CollectConjuncts(and.Right);
                return;
            }
            _conjuncts.Add(node);
        }

        public override Expression? Visit(Expression? node)
        {
            if (node == null) return null;
            _ancestors.Push(node);
            try { return base.Visit(node); }
            finally { _ancestors.Pop(); }
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            // TASK-899: `absent OR IN(list)`, a plain AND conjunct, splits like the IN itself; its absent leg goes with the
            // leading chunk. A stored default must be in that chunk: re-preparing a chunk (a chunked write does) adds the
            // leg to the chunk holding the default, so it is moved to the front.
            if (_conjuncts.Contains(node)
                && CosmosFilterRewrite.TryAbsentOrIn(node, out var contains, out var defaultValue)
                && TryMatch(contains, out var source, out var item, out var elementType))
            {
                var values = DistinctValues(source);
                if (defaultValue != null)
                {
                    var at = values.FindIndex(v => Equals(v, defaultValue));
                    if (at > 0)
                    {
                        values.RemoveAt(at);
                        values.Insert(0, defaultValue);
                    }
                }
                _found.Add(new InList(contains, item, elementType, values, ReasonFor(contains, elementType, absentOrIn: true), node));
                return node;
            }
            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (TryMatch(node, out var source, out var item, out var elementType))
            {
                _found.Add(new InList(node, item, elementType, DistinctValues(source), ReasonFor(node, elementType)));
            }
            return base.VisitMethodCall(node);
        }

        private static bool TryMatch(MethodCallExpression node, out Expression source, out Expression item, out Type elementType)
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
            // An IN is a caller's list tested against the document: the list must not depend on the document, the item must.
            return !ParameterFinder.Uses(source) && ParameterFinder.Uses(item);
        }

        private string? ReasonFor(MethodCallExpression node, Type elementType, bool absentOrIn = false)
        {
            if (elementType != typeof(string) && !elementType.IsValueType)
                return $"its values are of type {elementType.Name}, not a scalar value type or a string";
            if (absentOrIn || _conjuncts.Contains(node))
                return null;

            // _ancestors holds the node itself on top; the next one is its parent.
            var parent = _ancestors.Skip(1).FirstOrDefault();
            if (parent?.NodeType == ExpressionType.Not)
                return "it is a NOT IN, and deleting \"not in chunk 1\" would delete chunk 2's rows";
            foreach (var a in _ancestors.Skip(1))
            {
                if (a.NodeType is ExpressionType.OrElse or ExpressionType.Or)
                    return "it is under an OR";
                if (a.NodeType == ExpressionType.Not)
                    return "it is under a NOT";
                if (_conjuncts.Contains(a)) break;
            }
            return "it is inside a nested expression, not a plain AND conjunct";
        }

        private static List<object?> DistinctValues(Expression source)
        {
            var value = Evaluate(source);
            var result = new List<object?>();
            if (value is not IEnumerable sequence) return result;
            // Default equality: ordinal for strings, which is how Cosmos DB compares them (TASK-841).
            var seen = new HashSet<object?>();
            var sawNull = false;
            foreach (var v in sequence)
            {
                if (v == null)
                {
                    if (!sawNull) result.Add(null);
                    sawNull = true;
                    continue;
                }
                if (seen.Add(v)) result.Add(v);
            }
            return result;
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
                case UnaryExpression u when u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked && u.Method == null:
                    return Evaluate(u.Operand);
                default:
                    return Expression.Lambda<Func<object?>>(Expression.Convert(expr, typeof(object))).Compile()();
            }
        }
    }

    private sealed class ParameterFinder : ExpressionVisitor
    {
        private bool _found;

        public static bool Uses(Expression expr)
        {
            var finder = new ParameterFinder();
            finder.Visit(expr);
            return finder._found;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _found = true;
            return node;
        }
    }

    private sealed class NodeReplacer : ExpressionVisitor
    {
        private readonly Expression _target;
        private readonly Expression _replacement;

        public NodeReplacer(Expression target, Expression replacement)
        {
            _target = target;
            _replacement = replacement;
        }

        public override Expression? Visit(Expression? node)
            => ReferenceEquals(node, _target) ? _replacement : base.Visit(node);
    }
}
