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
        /// TASK-907: adds the Min / Max of a string to <paramref name="aggregations"/>: the first <c>terms</c> bucket of its
        /// <paramref name="keyword"/>, ordered by key ascending for a Min, descending for a Max. A document without a value
        /// has no bucket, so a null is skipped as LINQ skips it. A keyword orders by its UTF-8 bytes, code-point order: C#'s
        /// Ordinal but for a group holding both a character above U+FFFF and one in U+E000–U+FFFF, which two <c>regexp</c>
        /// filters find and <see cref="ReadStringExtreme"/> refuses by name. Where the keyword is the
        /// <c>.keyword</c> sub-field of <paramref name="textField"/>, a filter counts the documents it skipped (past
        /// <c>ignore_above</c>), which <see cref="ReadStringExtreme"/> refuses. The one producer for the store and the view.
        /// </summary>
        public static void AddStringExtreme(AggregationDictionary aggregations, AggregateFunction function, string name, string keyword, string? textField)
        {
            aggregations.Add(name, new TermsAggregation(name)
            {
                Field = keyword,
                Size = 1,
                Order = new List<TermsOrder> { function == AggregateFunction.Min ? TermsOrder.KeyAscending : TermsOrder.KeyDescending },
            });
            foreach (var (check, pattern) in new[] { (HighBmpCheck(name), AggregateMath.HighBmpCharacters), (AstralCheck(name), AggregateMath.AstralCharacters) })
            {
                aggregations.Add(check, new FilterAggregation(check)
                {
                    Filter = new RegexpQuery { Field = keyword, Value = ".*" + pattern + ".*" },
                });
            }
            if (textField == null) return;
            aggregations.Add(SkippedCheck(name), new FilterAggregation(SkippedCheck(name))
            {
                Filter = new BoolQuery
                {
                    Must = new QueryContainer[] { new ExistsQuery { Field = textField } },
                    MustNot = new QueryContainer[] { new ExistsQuery { Field = keyword } },
                },
            });
        }

        /// <summary>
        /// TASK-907: the Min / Max <see cref="AddStringExtreme"/> asked for, or null over no value.
        /// </summary>
        /// <exception cref="InvalidOperationException">A value has no keyword (it is past <c>ignore_above</c>), so it would be
        /// left out, as a group key over such a value is refused (TASK-878).</exception>
        /// <exception cref="NotSupportedException">The group holds both a character above U+FFFF and one in U+E000–U+FFFF.</exception>
        public static string? ReadStringExtreme(AggregateDictionary source, string name, bool checksSkipped, string describe,
            AggregateFunction function, string member)
        {
            if ((source.Filter(HighBmpCheck(name))?.DocCount ?? 0) > 0 && (source.Filter(AstralCheck(name))?.DocCount ?? 0) > 0)
            {
                throw AggregateMath.MixedAstralNotSupported($"Elasticsearch: {describe}", function, member);
            }
            if (checksSkipped && (source.Filter(SkippedCheck(name))?.DocCount ?? 0) > 0)
            {
                throw new InvalidOperationException(
                    $"{describe} has values longer than its keyword sub-field indexes (ignore_above), which Elasticsearch would "
                    + "leave out. Map it with [Keyword] to take its Min / Max. (TASK-907)");
            }
            return source.Terms(name)?.Buckets.FirstOrDefault()?.Key;
        }

        private static string SkippedCheck(string name) => name + "s";

        private static string HighBmpCheck(string name) => name + "b";

        private static string AstralCheck(string name) => name + "a";

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
