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

            // No aggregation requested — return raw rows as results
            if (query.Aggregates.Count == 0 && query.GroupByFields.Count == 0)
            {
                var rawResults = list.Select(item => new AggregateResult(AggregateMath.ToValueDictionary(item))).ToList().AsReadOnly();
                return Task.FromResult<IReadOnlyList<AggregateResult>>(rawResults);
            }

            // Build group key
            var keyProperties = query.GroupByFields
                .Select(f => typeof(T).GetProperty(f, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase))
                .Where(p => p != null)
                .ToArray();

            // Apply time bucketing to Timestamp property if requested
            PropertyInfo? timeProp = null;
            long bucketTicks = 0;
            if (!string.IsNullOrEmpty(query.TimeBucketInterval) && !string.IsNullOrEmpty(query.TimeColumn))
            {
                bucketTicks = TimeIntervalParser.Parse(query.TimeBucketInterval).Ticks;
                timeProp = typeof(T).GetProperty(query.TimeColumn, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (timeProp != null && bucketTicks > 0)
                {
                    list = ApplyTimeBucket(list, timeProp, bucketTicks).ToList();
                }
            }

            // Group by composite key
            var grouped = list.GroupBy(item =>
            {
                var key = new List<object?>();
                foreach (var prop in keyProperties)
                {
                    key.Add(prop!.GetValue(item));
                }
                if (timeProp != null && bucketTicks > 0)
                {
                    key.Add(timeProp.GetValue(item));
                }
                return string.Join("|", key.Select(k => k?.ToString() ?? ""));
            });

            // Compute aggregates per group
            var results = new List<AggregateResult>();
            foreach (var group in grouped)
            {
                var row = new Dictionary<string, object?>();

                var first = group.First();
                for (int i = 0; i < keyProperties.Length; i++)
                {
                    row[query.GroupByFields[i]] = keyProperties[i]!.GetValue(first);
                }

                if (timeProp != null && bucketTicks > 0)
                {
                    row["bucket_time"] = timeProp.GetValue(first);
                }

                // Aggregate functions
                foreach (var agg in query.Aggregates)
                {
                    if (agg.Function == AggregateFunction.Count)
                    {
                        row[agg.ResolvedAlias] = group.Count();
                        continue;
                    }

                    var prop = typeof(T).GetProperty(agg.SourcePropertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null) continue;

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

        private static IEnumerable<T> ApplyTimeBucket<T>(IEnumerable<T> items, PropertyInfo timeProp, long bucketTicks) where T : Models.AbstractModel
        {
            foreach (var item in items)
            {
                var ts = timeProp.GetValue(item);
                if (ts is DateTime dt)
                {
                    var bucketed = AggregateMath.TruncateToBucket(dt, bucketTicks);
                    timeProp.SetValue(item, bucketed);
                }
                yield return item;
            }
        }
    }
}
