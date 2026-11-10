using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Stores;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// TASK-902: a read sorted by a <see cref="DateTime"/> member, made to order as C# orders it, for both stores and the view
/// store.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Cosmos DB sorts a DateTime by its stored text (and only by a document path: an <c>ORDER BY</c> of the
/// wall-clock key is a 400, 2206, measured at TASK-899). The first 19 characters of every stored form are the wall clock to
/// the second, so the server's order is C#'s at the second; within one second it is not: <c>"…00"</c>, <c>"…00+01:00"</c>,
/// <c>"…00.0000001Z"</c>, <c>"…00.5Z"</c>, <c>"…00Z"</c> (measured on the emulator, vnext-preview, 2026-11-10).</para>
/// <para><b>The correction.</b> Rows that agree on every sort key before the first DateTime one, and on that one's second,
/// form a run; runs are contiguous and in the same place in both orders, so only the rows inside a run move. Each run is
/// sorted in memory (the DateTime by its ticks, as C#; a later string key ordinally, as Cosmos DB). On a paged read a run
/// can straddle the page's edge: it is read whole (the filter AND the run's keys, a second query), and the page takes the
/// run's rows at the positions the page covered. Where a single run covers the whole page, the rows after the page are read
/// (at most the run's size) to find where in the run the page begins. A run is one second of one key, so the extra reads
/// are small unless many rows share a second.</para>
/// <para>A sort by a <see cref="DateTimeOffset"/> member is refused by name: C# orders the instant, the stored text holds
/// the writer's offset, so no second-prefix of it orders as the instant does.</para>
/// </remarks>
internal static class CosmosDateTimeOrder
{
    internal sealed record Field(PropertyInfo Property, bool Descending);

    /// <summary>
    /// The sort's fields when one of them is a <see cref="DateTime"/>, or <c>null</c> when the server's order is C#'s
    /// already (or a field does not resolve, which the read reports as before).
    /// </summary>
    /// <exception cref="NotSupportedException">The sort has a <see cref="DateTimeOffset"/> field.</exception>
    internal static IReadOnlyList<Field>? Plan<T>(OrderBy<T>? orderBy, string owner)
    {
        if (orderBy == null || orderBy.Fields.Count == 0) return null;
        var fields = new List<Field>();
        foreach (var field in orderBy.Fields)
        {
            var property = typeof(T).GetProperty(field.PropertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null) return null;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type == typeof(DateTimeOffset))
            {
                throw new NotSupportedException(
                    $"Cosmos DB cannot sort {owner} by {property.Name}, a DateTimeOffset, as C# sorts it: C# orders the instant, "
                    + "Cosmos DB the stored text, which holds the writer's offset. Sort by a DateTime stored in UTC, or sort in the "
                    + "caller. (TASK-902)");
            }
            fields.Add(new Field(property, field.Descending));
        }
        return fields.Any(f => IsDateTime(f.Property.PropertyType)) ? fields : null;
    }

    /// <summary>
    /// The page <paramref name="read"/> answers for <paramref name="filter"/>, sorted as C# sorts it.
    /// </summary>
    /// <param name="filter">The filter as given (it is prepared again for a run read).</param>
    /// <param name="read">The server read with the caller's sort: a filter (already prepared), a limit, an offset.</param>
    internal static async ValueTask<List<T>> ReadAsync<T>(
        Expression<Func<T, bool>>? filter,
        IReadOnlyList<Field> fields,
        int? limit,
        int? offset,
        Func<Expression<Func<T, bool>>?, int?, int?, ValueTask<List<T>>> read)
    {
        var rows = await read(filter, limit, offset).ConfigureAwait(false);
        if (rows.Count == 0) return rows;

        var at = Enumerable.Range(0, fields.Count).First(i => IsDateTime(fields[i].Property.PropertyType));
        var comparer = new RunComparer<T>(fields, at);
        var runs = Runs(rows, fields, at);
        var paged = limit.HasValue || offset.HasValue;
        var start = offset ?? 0;
        var full = limit.HasValue && rows.Count == limit.Value;

        var result = new List<T>(rows.Count);
        for (var r = 0; r < runs.Count; r++)
        {
            var (first, count) = runs[r];
            var slice = rows.GetRange(first, count);
            var mayStartBefore = paged && r == 0 && start > 0;
            var mayEndAfter = paged && r == runs.Count - 1 && full;
            if (!mayStartBefore && !mayEndAfter)
            {
                result.AddRange(slice.OrderBy(x => x, comparer));
                continue;
            }

            var whole = (await read(CosmosFilterRewrite.Prepare(And(filter, RunFilter<T>(slice[0], fields, at))), null, null).ConfigureAwait(false))
                .OrderBy(x => x, comparer).ToList();
            var before = 0;
            if (mayStartBefore && mayEndAfter)
            {
                // The run covers the page: the rows after it that are still in the run say where the page begins.
                var next = await read(filter, whole.Count, start + rows.Count).ConfigureAwait(false);
                var key = RunKey(slice[0], fields, at);
                var after = next.TakeWhile(x => SameRun(RunKey(x, fields, at), key)).Count();
                before = whole.Count - count - after;
            }
            else if (mayStartBefore)
            {
                before = whole.Count - count;
            }

            // The documents changed between the reads: the page's own rows, sorted, are the best answer left.
            if (before < 0 || before + count > whole.Count)
            {
                result.AddRange(slice.OrderBy(x => x, comparer));
                continue;
            }
            result.AddRange(whole.GetRange(before, count));
        }
        return result;
    }

    private static bool IsDateTime(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(DateTime);

    /// <summary>The keys before the first DateTime one, and that one's second (null for a null value).</summary>
    private static object?[] RunKey<T>(T row, IReadOnlyList<Field> fields, int at)
    {
        var key = new object?[at + 1];
        for (var i = 0; i < at; i++) key[i] = fields[i].Property.GetValue(row);
        key[at] = fields[at].Property.GetValue(row) is DateTime d ? d.Ticks - d.Ticks % TimeSpan.TicksPerSecond : null;
        return key;
    }

    private static bool SameRun(object?[] a, object?[] b)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if (!Equals(a[i], b[i])) return false;
        }
        return true;
    }

    private static List<(int First, int Count)> Runs<T>(List<T> rows, IReadOnlyList<Field> fields, int at)
    {
        var runs = new List<(int, int)>();
        var first = 0;
        var key = RunKey(rows[0], fields, at);
        for (var i = 1; i < rows.Count; i++)
        {
            var next = RunKey(rows[i], fields, at);
            if (SameRun(key, next)) continue;
            runs.Add((first, i - first));
            first = i;
            key = next;
        }
        runs.Add((first, rows.Count - first));
        return runs;
    }

    /// <summary>The rows of <paramref name="row"/>'s run: equal earlier keys, and the DateTime in the same second (or null).</summary>
    private static Expression<Func<T, bool>> RunFilter<T>(T row, IReadOnlyList<Field> fields, int at)
    {
        var x = Expression.Parameter(typeof(T), "x");
        Expression? body = null;
        void Add(Expression test) => body = body == null ? test : Expression.AndAlso(body, test);

        for (var i = 0; i < at; i++)
        {
            var property = fields[i].Property;
            Add(Expression.Equal(Expression.Property(x, property), Expression.Constant(property.GetValue(row), property.PropertyType)));
        }

        var dateTime = fields[at].Property;
        var member = Expression.Property(x, dateTime);
        if (dateTime.GetValue(row) is not DateTime value)
        {
            Add(Expression.Equal(member, Expression.Constant(null, dateTime.PropertyType)));
        }
        else
        {
            var second = new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond);
            Add(Expression.GreaterThanOrEqual(member, Expression.Constant(second, dateTime.PropertyType)));
            if (second < DateTime.MaxValue.AddSeconds(-1))
            {
                Add(Expression.LessThan(member, Expression.Constant(second.AddSeconds(1), dateTime.PropertyType)));
            }
        }
        return Expression.Lambda<Func<T, bool>>(body!, x);
    }

    private static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>>? filter, Expression<Func<T, bool>> run)
    {
        if (filter == null) return run;
        var body = new ParameterSwap(run.Parameters[0], filter.Parameters[0]).Visit(run.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(filter.Body, body), filter.Parameters);
    }

    private sealed class ParameterSwap : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterSwap(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : node;
    }

    /// <summary>
    /// The order inside a run, from the first DateTime key on: a DateTime by its ticks (null first), a string ordinally (as
    /// Cosmos DB orders it), anything else by its default comparer (null first); a descending key reversed.
    /// </summary>
    private sealed class RunComparer<T> : IComparer<T>
    {
        private readonly IReadOnlyList<Field> _fields;
        private readonly int _at;

        public RunComparer(IReadOnlyList<Field> fields, int at)
        {
            _fields = fields;
            _at = at;
        }

        public int Compare(T? a, T? b)
        {
            for (var i = _at; i < _fields.Count; i++)
            {
                var field = _fields[i];
                var c = CompareValues(a == null ? null : field.Property.GetValue(a), b == null ? null : field.Property.GetValue(b));
                if (c != 0) return field.Descending ? -c : c;
            }
            return 0;
        }

        private static int CompareValues(object? a, object? b)
        {
            if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
            if (a is DateTime da && b is DateTime db) return da.Ticks.CompareTo(db.Ticks);
            if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
            return Comparer.Default.Compare(a, b);
        }
    }
}
