using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Aggregate function types. Shared across stores and views.
    /// </summary>
    public enum AggregateFunction
    {
        Sum,
        Avg,
        Min,
        Max,
        Count
    }

    /// <summary>
    /// Describes a single aggregate computation over a source property.
    /// </summary>
    public sealed record AggregateField(
        AggregateFunction Function,
        string SourcePropertyName,
        string? Alias = null)
    {
        /// <summary>
        /// Resolved alias — defaults to "{function}_{source}" when not explicitly set.
        /// </summary>
        public string ResolvedAlias => Alias ?? $"{Function.ToString().ToLowerInvariant()}_{SourcePropertyName}";
    }

    /// <summary>
    /// Store-agnostic aggregation query specification.
    /// Follows the same expression-based pattern as <see cref="OrderBy{T}"/>.
    /// </summary>
    public sealed class AggregateQuery<T> where T : Models.AbstractModel
    {
        /// <summary>Optional filter applied before aggregation (SQL WHERE / MongoDB $match).</summary>
        public Expression<Func<T, bool>>? Filter { get; init; }

        /// <summary>Property names to group by.</summary>
        public IReadOnlyList<string> GroupByFields { get; init; } = [];

        /// <summary>Aggregate computations to apply per group.</summary>
        public IReadOnlyList<AggregateField> Aggregates { get; init; } = [];

        /// <summary>
        /// Optional time-bucket interval (e.g. "1 hour", "5 minutes", "00:15:00"); needs <see cref="TimeColumn"/>, and an interval
        /// that does not parse is refused. On SQL stores only TimescaleDB buckets (time_bucket); other SQL providers refuse
        /// it (TASK-706). The in-memory aggregation buckets any store's rows.
        /// </summary>
        public string? TimeBucketInterval { get; init; }

        /// <summary>Property name of the time column for time-bucketing.</summary>
        public string? TimeColumn { get; init; }

        /// <summary>Optional ordering on the result set.</summary>
        public IDictionary<string, bool>? OrderBy { get; init; }

        /// <summary>Maximum number of results.</summary>
        public int? Limit { get; init; }

        /// <summary>Number of results to skip.</summary>
        public int? Offset { get; init; }

        /// <summary>
        /// TASK-907 (the user's decision, 2026-11-10): the order a <see cref="AggregateFunction.Min"/> or
        /// <see cref="AggregateFunction.Max"/> of a <see cref="string"/> member is taken in. <see cref="System.StringComparison.Ordinal"/>
        /// by default, so <c>"Banana"</c> is below <c>"apple"</c> on every machine and in every culture, as TASK-873's exact
        /// string equality is. The in-memory aggregation honours every comparison; a provider that cannot take a Min or Max
        /// of a string in this order refuses the query by name (<see cref="NotSupportedException"/>), never answering in the
        /// database's own order. It orders nothing else: not a non-string member, not the result rows.
        /// </summary>
        public StringComparison StringComparison { get; init; } = StringComparison.Ordinal;

        /// <summary>
        /// Whether the rows are grouped: by a group-by field or by a time bucket. An ungrouped query answers ONE row, also
        /// over no rows (TASK-886); a grouped one answers a row per group, so none over no rows.
        /// </summary>
        public bool IsGrouped
            => GroupByFields.Count > 0 || !string.IsNullOrEmpty(TimeBucketInterval) || !string.IsNullOrEmpty(TimeColumn);

        /// <summary>
        /// TASK-886: refuses a query with nothing to aggregate (no aggregate, no group-by field, no time bucket), before
        /// anything runs. Measured before: the in-memory aggregation answered the raw rows, MongoDB and Elasticsearch one
        /// empty row, PostgreSQL a row with no columns per row, and SQLite, MySQL, SQL Server and Cosmos DB a syntax error.
        /// Every provider calls this where it reads the query.
        /// </summary>
        /// <exception cref="ArgumentException">The query names no aggregate, no group-by field and no time bucket.</exception>
        public void ThrowIfNothingToAggregate()
        {
            if (Aggregates.Count > 0 || IsGrouped) return;
            throw new ArgumentException(
                $"The aggregate query of {typeof(T).Name} has nothing to aggregate: it names no aggregate, no group-by field and "
                + "no time bucket. Read the rows with Read instead. (TASK-886)", "query");
        }

        /// <summary>
        /// TASK-907: refuses, before anything is read, a query whose string Min / Max asks for another order than
        /// <see cref="System.StringComparison.Ordinal"/>, on a provider that takes only Ordinal. Called first by every native
        /// provider, so the refusal does not depend on the data (an empty store, a missing collection).
        /// </summary>
        /// <param name="provider">What refuses, for the message: e.g. <c>"MongoDB aggregate of Order"</c>.</param>
        /// <exception cref="NotSupportedException">A string Min / Max in another order than Ordinal.</exception>
        public void ThrowIfStringOrderNotOrdinal(string provider)
        {
            if (StringComparison == StringComparison.Ordinal) return;
            foreach (var aggregate in Aggregates)
            {
                if (aggregate.Function is not (AggregateFunction.Min or AggregateFunction.Max)) continue;
                var member = typeof(T).GetProperty(aggregate.SourcePropertyName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (member?.PropertyType != typeof(string)) continue;
                throw AggregateMath.StringOrderNotSupported(provider, aggregate.Function, $"{typeof(T).Name}.{member.Name}", StringComparison);
            }
        }

        /// <summary>This query with <paramref name="filter"/> in place of <see cref="Filter"/> (a scoping decorator's, TASK-631).</summary>
        public AggregateQuery<T> WithFilter(Expression<Func<T, bool>>? filter) => new()
        {
            Filter = filter,
            GroupByFields = GroupByFields,
            Aggregates = Aggregates,
            TimeBucketInterval = TimeBucketInterval,
            TimeColumn = TimeColumn,
            OrderBy = OrderBy,
            Limit = Limit,
            Offset = Offset,
            StringComparison = StringComparison,
        };
    }
}
