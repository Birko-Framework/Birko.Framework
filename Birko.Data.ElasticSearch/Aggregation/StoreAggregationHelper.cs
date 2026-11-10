using Birko.Data.Stores;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.ElasticSearch.Aggregation
{
    /// <summary>
    /// Metric aggregation building and reading shared by the view store. The entity stores' aggregate is
    /// <see cref="StoreAggregatePlan"/> (TASK-878: it replaced this class's terms/composite builder and its single-response
    /// parser, which sent property names verbatim and read one page).
    /// </summary>
    public static class StoreAggregationHelper
    {
        /// <summary>
        /// Builds a single metric aggregation container for the given function and field.
        /// Shared by store-level and view-level aggregation. A Count of a field counts its non-null values (SQL's
        /// <c>COUNT(col)</c>); a Count of the rows is the document count, never an aggregation here (TASK-886: it was a
        /// <c>value_count</c> on <c>_id</c>, which Elasticsearch 8 refuses by default), so a Count with no field is refused.
        /// </summary>
        /// <exception cref="ArgumentException">A Count with no field.</exception>
        public static AggregationContainer BuildSingleMetricAggregation(
            AggregateFunction function, string name, string? fieldName)
        {
            if (function == AggregateFunction.Count && fieldName == null)
                throw new ArgumentException(
                    "A Count of the rows is the document count (a bucket's doc_count, or the hit total), not a metric aggregation. (TASK-886)",
                    nameof(fieldName));
            return function switch
            {
                AggregateFunction.Sum => new AggregationContainer { Sum = new SumAggregation(name, fieldName) },
                AggregateFunction.Avg => new AggregationContainer { Average = new AverageAggregation(name, fieldName) },
                AggregateFunction.Min => new AggregationContainer { Min = new MinAggregation(name, fieldName) },
                AggregateFunction.Max => new AggregationContainer { Max = new MaxAggregation(name, fieldName) },
                AggregateFunction.Count => new AggregationContainer { ValueCount = new ValueCountAggregation(name, fieldName) },
                _ => throw new NotSupportedException($"Aggregate function {function} is not supported")
            };
        }

        /// <summary>
        /// Extracts metric values from a bucket into a dictionary.
        /// Shared by store-level and view-level result parsing.
        /// </summary>
        /// <param name="bucket">The bucket to extract metrics from.</param>
        /// <param name="aggregateNames">Map of aggregate name → function type.</param>
        /// <returns>Dictionary of alias → value.</returns>
        public static Dictionary<string, double?> ExtractMetricValues(
            BucketBase bucket,
            IEnumerable<(string Name, AggregateFunction Function)> aggregateNames)
        {
            var values = new Dictionary<string, double?>();
            foreach (var (name, function) in aggregateNames)
            {
                values[name] = function switch
                {
                    AggregateFunction.Sum => bucket.Sum(name)?.Value,
                    AggregateFunction.Avg => bucket.Average(name)?.Value,
                    AggregateFunction.Min => bucket.Min(name)?.Value,
                    AggregateFunction.Max => bucket.Max(name)?.Value,
                    AggregateFunction.Count => bucket.ValueCount(name)?.Value,
                    _ => null
                };
            }
            return values;
        }

        /// <summary>
        /// Extracts metric values from a response-level aggregate dictionary.
        /// Used for ungrouped (global) aggregation responses.
        /// </summary>
        public static Dictionary<string, double?> ExtractMetricValues(
            AggregateDictionary aggregations,
            IEnumerable<(string Name, AggregateFunction Function)> aggregateNames)
        {
            var values = new Dictionary<string, double?>();
            foreach (var (name, function) in aggregateNames)
            {
                values[name] = function switch
                {
                    AggregateFunction.Sum => aggregations.Sum(name)?.Value,
                    AggregateFunction.Avg => aggregations.Average(name)?.Value,
                    AggregateFunction.Min => aggregations.Min(name)?.Value,
                    AggregateFunction.Max => aggregations.Max(name)?.Value,
                    AggregateFunction.Count => aggregations.ValueCount(name)?.Value,
                    _ => null
                };
            }
            return values;
        }
    }
}
