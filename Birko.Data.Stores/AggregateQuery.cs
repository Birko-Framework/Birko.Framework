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

        /// <summary>Optional time-bucket interval (e.g. "1 hour", "5 minutes"). Used by TimescaleDB time_bucket / PostgreSQL date_trunc.</summary>
        public string? TimeBucketInterval { get; init; }

        /// <summary>Property name of the time column for time-bucketing.</summary>
        public string? TimeColumn { get; init; }

        /// <summary>Optional ordering on the result set.</summary>
        public IDictionary<string, bool>? OrderBy { get; init; }

        /// <summary>Maximum number of results.</summary>
        public int? Limit { get; init; }

        /// <summary>Number of results to skip.</summary>
        public int? Offset { get; init; }
    }
}
