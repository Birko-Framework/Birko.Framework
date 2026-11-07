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
/// chunk of the list, so a long list stays under Cosmos DB's query-text ceiling.
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
    /// <summary>Values per chunk: 40 KB of query text for Guids, a thirteenth of the measured ceiling.</summary>
    public const int DefaultChunkSize = 1000;

    /// <summary>Cosmos DB's query-text limit in characters (the server's SC3020 message names it).</summary>
    public const int DefaultMaxQueryTextLength = 524288;

    /// <summary>
    /// Plans the filter. Returns <c>null</c> when it runs as one query, or the chunk filters in list order.
    /// </summary>
    /// <param name="queryTextLength">The length of the query text the store would send for a filter; local, no request.</param>
    /// <exception cref="NotSupportedException">The list is past the ceiling and the filter cannot be split.</exception>
    public static IReadOnlyList<Expression<Func<T, bool>>>? Plan<T>(
        Expression<Func<T, bool>>? filter,
        string operation,
        int chunkSize,
        int maxQueryTextLength,
        bool sortedOrPaged,
        Func<Expression<Func<T, bool>>, int> queryTextLength)
    {
        if (filter == null) return null;
        if (chunkSize < 1)
            throw new InvalidOperationException($"The IN-list chunk size must be at least 1; it is {chunkSize}.");

        var lists = InListFinder.Find(filter);
        var big = lists.Where(l => l.Values.Count > chunkSize).ToList();
        if (big.Count == 0) return null;

        var blocker = big.Where(l => l.Reason != null).OrderByDescending(l => l.Values.Count).FirstOrDefault();
        if (blocker != null || sortedOrPaged)
        {
            var reason = blocker?.Reason ?? "the read has a sort, limit or offset, which chunks cannot reproduce";
            var count = (blocker ?? big.OrderByDescending(l => l.Values.Count).First()).Values.Count;
            var length = queryTextLength(filter);
            if (length <= maxQueryTextLength) return null;
            throw Refusal<T>(operation, count, length, maxQueryTextLength, chunkSize, reason);
        }

        var target = big.OrderByDescending(l => l.Values.Count).First();
        var chunks = new List<Expression<Func<T, bool>>>();
        for (var start = 0; start < target.Values.Count; start += chunkSize)
        {
            var slice = target.Values.Skip(start).Take(chunkSize);
            var body = new NodeReplacer(target.Node, target.WithValues(slice)).Visit(filter.Body)!;
            chunks.Add(Expression.Lambda<Func<T, bool>>(body, filter.Parameters));
        }

        var first = queryTextLength(chunks[0]);
        if (first > maxQueryTextLength)
        {
            throw Refusal<T>(operation, target.Values.Count, first, maxQueryTextLength, chunkSize,
                "even one chunk is over the limit: the rest of the filter (another long IN list, for one) is too long");
        }
        return chunks;
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
        public InList(MethodCallExpression node, Expression item, Type elementType, List<object?> values, string? reason)
        {
            Node = node;
            Item = item;
            ElementType = elementType;
            Values = values;
            Reason = reason;
        }

        public MethodCallExpression Node { get; }
        public Expression Item { get; }
        public Type ElementType { get; }
        public List<object?> Values { get; }
        /// <summary><c>null</c> when the list can be split.</summary>
        public string? Reason { get; }

        /// <summary><c>new List&lt;E&gt;(values).Contains(item)</c>: the List spelling the Cosmos provider renders as IN.</summary>
        public Expression WithValues(IEnumerable<object?> values)
        {
            var listType = typeof(List<>).MakeGenericType(ElementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var v in values) list.Add(v);
            var contains = listType.GetMethod(nameof(List<object>.Contains), new[] { ElementType })!;
            return Expression.Call(Expression.Constant(list, listType), contains, Item);
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

        private string? ReasonFor(MethodCallExpression node, Type elementType)
        {
            if (elementType != typeof(string) && !elementType.IsValueType)
                return $"its values are of type {elementType.Name}, not a scalar value type or a string";
            if (_conjuncts.Contains(node))
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
