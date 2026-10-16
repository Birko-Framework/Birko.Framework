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

            // No aggregation requested — raw rows, still ordered and paged (SH-M383: they were returned whole).
            if (query.Aggregates.Count == 0 && query.GroupByFields.Count == 0 && timeProp == null)
            {
                var rawResults = list.Select(item => new AggregateResult(AggregateMath.ToValueDictionary(item))).ToList();
                return Task.FromResult<IReadOnlyList<AggregateResult>>(
                    ApplyOrderingAndPaging(rawResults, query.OrderBy, query.Offset, query.Limit).AsReadOnly());
            }

            // Group by the key tuple itself. SH-M384: the key was the values joined with "|", so ("a|b", "c") and
            // ("a", "b|c"), or null and "", fell into one group.
            var grouped = list.GroupBy(item =>
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
            }, KeyTupleComparer.Instance);

            // Compute aggregates per group
            var results = new List<AggregateResult>();
            foreach (var group in grouped)
            {
                var row = new Dictionary<string, object?>();

                var first = group.First();
                for (int i = 0; i < keyProperties.Length; i++)
                {
                    row[query.GroupByFields[i]] = keyProperties[i].GetValue(first);
                }

                if (timeProp != null)
                {
                    row["bucket_time"] = BucketTimeOf(first, timeProp, bucketTicks);
                }

                // Aggregate functions
                foreach (var agg in query.Aggregates)
                {
                    if (agg.Function == AggregateFunction.Count)
                    {
                        row[agg.ResolvedAlias] = group.Count();
                        continue;
                    }

                    var prop = RequireProperty<T>(agg.SourcePropertyName, "aggregate source");

                    var values = group.Select(item => prop.GetValue(item)).ToList();

                    row[agg.ResolvedAlias] = AggregateMath.ComputeAggregate(agg.Function, values, prop.PropertyType, group.Count());
                }

                results.Add(new AggregateResult(row));
            }

            results = ApplyOrderingAndPaging(results, query.OrderBy, query.Offset, query.Limit);

            return Task.FromResult<IReadOnlyList<AggregateResult>>(results.AsReadOnly());
        }

        /// <summary>
        /// Applies ordering, offset, and limit to a list of aggregate results.
        /// Shared by all store providers to avoid duplicating this logic.
        /// </summary>
        public static List<AggregateResult> ApplyOrderingAndPaging(
            List<AggregateResult> results,
            IDictionary<string, bool>? orderBy,
            int? offset,
            int? limit)
        {
            if (orderBy != null && orderBy.Count > 0)
            {
                IOrderedEnumerable<AggregateResult>? ordered = null;
                foreach (var (field, desc) in orderBy)
                {
                    if (ordered == null)
                        ordered = desc ? results.OrderByDescending(r => r.GetValue<object>(field))
                                       : results.OrderBy(r => r.GetValue<object>(field));
                    else
                        ordered = desc ? ordered.ThenByDescending(r => r.GetValue<object>(field))
                                       : ordered.ThenBy(r => r.GetValue<object>(field));
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
