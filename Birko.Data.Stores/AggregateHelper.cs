using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// LINQ-based aggregation fallback.
    /// Used by in-memory stores and any provider without native aggregation support (e.g. RavenDB).
    /// Takes a materialized sequence and applies grouping + aggregate functions.
    /// Delegates pure computation to <see cref="AggregateMath"/>.
    /// </summary>
    public static class AggregateHelper
    {
        /// <summary>
        /// Aggregates an in-memory sequence synchronously using the given query specification.
        /// </summary>
        public static IReadOnlyList<AggregateResult> LinqAggregate<T>(
            IEnumerable<T> source,
            AggregateQuery<T> query)
            where T : Models.AbstractModel
        {
            return LinqAggregateAsync(source, query, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Aggregates an in-memory sequence using the given query specification.
        /// </summary>
        public static Task<IReadOnlyList<AggregateResult>> LinqAggregateAsync<T>(
            IEnumerable<T> source,
            AggregateQuery<T> query,
            CancellationToken ct = default)
            where T : Models.AbstractModel
        {
            query.ThrowIfNothingToAggregate();

            var filtered = source;

            // Apply filter
            if (query.Filter != null)
            {
                filtered = filtered.Where(query.Filter.Compile());
            }

            var list = filtered.ToList();

            // TASK-152 (SH-M377): a name that resolves to no property is refused. It was dropped from the key array,
            // so the remaining values were labelled with the wrong GroupByFields names; an aggregate over one was
            // skipped, so its column was simply absent.
            var keyProperties = query.GroupByFields.Select(f => RequireProperty<T>(f, "group-by field")).ToArray();

            // Apply time bucketing to Timestamp property if requested
            PropertyInfo? timeProp = null;
            long bucketTicks = 0;
            if (!string.IsNullOrEmpty(query.TimeBucketInterval) || !string.IsNullOrEmpty(query.TimeColumn))
            {
                // SH-M386: an interval that does not parse, or a time column that does not resolve, used to disable
                // bucketing silently (the unparsed interval read as TimeSpan.Zero).
                if (string.IsNullOrEmpty(query.TimeBucketInterval) || string.IsNullOrEmpty(query.TimeColumn))
                    throw new ArgumentException("Time bucketing needs both TimeBucketInterval and TimeColumn.", nameof(query));
                bucketTicks = TimeIntervalParser.Parse(query.TimeBucketInterval).Ticks;
                if (bucketTicks <= 0)
                    throw new ArgumentException($"Time bucket interval '{query.TimeBucketInterval}' is not a positive interval.", nameof(query));
                timeProp = RequireProperty<T>(query.TimeColumn, "time column");
                // Do NOT mutate the source entities here (CR-H097): the list holds the caller's own
                // instances (e.g. an InMemory store's live values). The bucket time is computed
                // on-the-fly at group/result time from the untouched property below.
            }

            // TASK-886 (the user's decision): an ungrouped query is ONE group of every row, so over no rows it still answers
            // one row (Count 0, Sum 0, Avg / Min / Max null), as SQL does. It used to group no rows into no groups.
            // A query with nothing to aggregate is refused above; it used to answer the raw rows (SH-M383).
            // Grouped: by the key tuple itself. SH-M384: the key was the values joined with "|", so ("a|b", "c") and
            // ("a", "b|c"), or null and "", fell into one group.
            var groups = keyProperties.Length == 0 && timeProp == null
                ? new List<List<T>> { list }
                : list.GroupBy(item =>
                {
                    var key = new object?[keyProperties.Length + (timeProp != null ? 1 : 0)];
                    for (var i = 0; i < keyProperties.Length; i++)
                    {
                        key[i] = keyProperties[i].GetValue(item);
                    }
                    if (timeProp != null)
                    {
                        key[keyProperties.Length] = BucketTimeOf(item, timeProp, bucketTicks);
                    }
                    return key;
                }, KeyTupleComparer.Instance).Select(g => g.ToList()).ToList();

            // Compute aggregates per group
            var results = new List<AggregateResult>();
            foreach (var group in groups)
            {
                var row = new Dictionary<string, object?>();

                // A grouped group always holds a row; only the ungrouped one can be empty, and it has no keys.
                if (group.Count > 0)
                {
                    var first = group[0];
                    for (int i = 0; i < keyProperties.Length; i++)
                    {
                        row[query.GroupByFields[i]] = keyProperties[i].GetValue(first);
                    }

                    if (timeProp != null)
                    {
                        row["bucket_time"] = BucketTimeOf(first, timeProp, bucketTicks);
                    }
                }

                // Aggregate functions
                foreach (var agg in query.Aggregates)
                {
                    if (agg.Function == AggregateFunction.Count)
                    {
                        row[agg.ResolvedAlias] = group.Count;
                        continue;
                    }

                    var prop = RequireProperty<T>(agg.SourcePropertyName, "aggregate source");

                    var values = group.Select(item => prop.GetValue(item)).ToList();

                    row[agg.ResolvedAlias] = AggregateMath.ComputeAggregate(agg.Function, values, prop.PropertyType, group.Count, query.StringComparison);
                }

                results.Add(new AggregateResult(row));
            }

            results = ApplyOrderingAndPaging(results, query.OrderBy, query.Offset, query.Limit, query.StringComparison);

            return Task.FromResult<IReadOnlyList<AggregateResult>>(results.AsReadOnly());
        }

        /// <summary>
        /// Applies ordering, offset, and limit to a list of aggregate results.
        /// Shared by all store providers to avoid duplicating this logic.
        /// </summary>
        /// <remarks>
        /// TASK-909 (the user's decision, 2026-11-11): two strings are ordered in <paramref name="stringComparison"/>'s order
        /// (<see cref="AggregateQuery{T}.StringComparison"/>, Ordinal by default), as a string Min / Max is taken
        /// (<see cref="AggregateMath.ValueComparer"/>); any other value by <see cref="Comparer{T}.Default"/>, null below every
        /// value. Measured before: <see cref="Comparer{T}.Default"/> ordered strings in the current culture, so the in-memory
        /// stores, MongoDB, Elasticsearch and Cosmos DB put <c>"apple"</c> before <c>"Banana"</c>, and a page held other rows
        /// on a machine with another culture. The order is stable: rows tied on every key keep the order they came in (the
        /// in-memory aggregation's is the order the groups first occur in).
        /// </remarks>
        public static List<AggregateResult> ApplyOrderingAndPaging(
            List<AggregateResult> results,
            IDictionary<string, bool>? orderBy,
            int? offset,
            int? limit,
            StringComparison stringComparison = StringComparison.Ordinal)
        {
            if (orderBy != null && orderBy.Count > 0)
            {
                var comparer = AggregateMath.ValueComparer(stringComparison);
                IOrderedEnumerable<AggregateResult>? ordered = null;
                foreach (var (field, desc) in orderBy)
                {
                    if (ordered == null)
                        ordered = desc ? results.OrderByDescending(r => r.GetValue<object>(field), comparer)
                                       : results.OrderBy(r => r.GetValue<object>(field), comparer);
                    else
                        ordered = desc ? ordered.ThenByDescending(r => r.GetValue<object>(field), comparer)
                                       : ordered.ThenBy(r => r.GetValue<object>(field), comparer);
                }
                if (ordered != null)
                    results = ordered.ToList();
            }

            var skip = offset ?? 0;
            if (skip > 0) results = results.Skip(skip).ToList();
            if (limit.HasValue) results = results.Take(limit.Value).ToList();

            return results;
        }

        private static PropertyInfo RequireProperty<T>(string name, string role)
            => typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
               ?? throw new ArgumentException($"Aggregate {role} '{name}' is not a property of {typeof(T).Name}.", nameof(name));

        /// <summary>Equality of a group-key tuple by its values, element by element.</summary>
        private sealed class KeyTupleComparer : IEqualityComparer<object?[]>
        {
            public static readonly KeyTupleComparer Instance = new();

            public bool Equals(object?[]? x, object?[]? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null || x.Length != y.Length) return false;
                for (var i = 0; i < x.Length; i++)
                {
                    if (!object.Equals(x[i], y[i])) return false;
                }
                return true;
            }

            public int GetHashCode(object?[] key)
            {
                var hash = new HashCode();
                foreach (var part in key) hash.Add(part);
                return hash.ToHashCode();
            }
        }

        /// <summary>
        /// Computes the time-bucket for an item's time column WITHOUT mutating the item (CR-H097).
        /// Returns the truncated bucket value for a DateTime, otherwise the raw property value.
        /// </summary>
        private static object? BucketTimeOf(object item, PropertyInfo timeProp, long bucketTicks)
        {
            var ts = timeProp.GetValue(item);
            if (ts is DateTime dt)
            {
                return AggregateMath.TruncateToBucket(dt, bucketTicks);
            }
            return ts;
        }
    }
}
