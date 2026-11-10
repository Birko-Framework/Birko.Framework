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
        };
    }
}
