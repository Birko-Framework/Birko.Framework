using Birko.Data.Models;
using Birko.Data.Stores;
using Raven.Client.Documents.Queries.Facets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.RavenDB.Aggregation;

/// <summary>
/// Shared helper for building RavenDB faceted aggregation queries and mapping results.
/// Eliminates duplication between sync and async store implementations.
/// </summary>
public static class FacetAggregationHelper
{
    /// <summary>
    /// Builds an <see cref="Action{T}"/> that configures a RavenDB facet builder
    /// from the aggregates defined in an <see cref="AggregateQuery{T}"/>.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="query">The aggregate query containing group-by field and aggregate definitions.</param>
    /// <returns>An action that configures the facet builder.</returns>
    public static Action<IFacetBuilder<T>> BuildFacetBuilder<T>(AggregateQuery<T> query)
        where T : AbstractModel
    {
        var groupByField = query.GroupByFields[0];

        return builder =>
        {
            var ops = builder.ByField(groupByField);

            foreach (var agg in query.Aggregates)
            {
                if (agg.Function == AggregateFunction.Count)
                {
                    continue; // Count is always included in facet results
                }

                var param = Expression.Parameter(typeof(T), "x");
                var prop = Expression.Property(param, agg.SourcePropertyName);
                var converted = Expression.Convert(prop, typeof(object));
                var lambda = Expression.Lambda<Func<T, object>>(converted, param);

                switch (agg.Function)
                {
                    case AggregateFunction.Sum: ops = ops.SumOn(lambda); break;
                    case AggregateFunction.Avg: ops = ops.AverageOn(lambda); break;
                    case AggregateFunction.Min: ops = ops.MinOn(lambda); break;
                    case AggregateFunction.Max: ops = ops.MaxOn(lambda); break;
                }
            }
        };
    }

    /// <summary>
    /// Maps a RavenDB facet result dictionary to a list of <see cref="AggregateResult"/> objects.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="facetResults">The facet results from RavenDB.</param>
    /// <param name="query">The aggregate query containing group-by field and aggregate definitions.</param>
    /// <returns>A list of aggregate results mapped from the facet values.</returns>
    public static List<AggregateResult> MapFacetResults<T>(
        Dictionary<string, FacetResult> facetResults,
        AggregateQuery<T> query)
        where T : AbstractModel
    {
        var groupByField = query.GroupByFields[0];
        var results = new List<AggregateResult>();

        if (!facetResults.TryGetValue(groupByField, out var facetResult))
        {
            return results;
        }

        // Group FacetValues by Range (group key) since each aggregate field produces a separate entry
        var byRange = facetResult.Values.GroupBy(v => v.Range);

        foreach (var group in byRange)
        {
            var row = new Dictionary<string, object?> { [groupByField] = group.Key };

            foreach (var agg in query.Aggregates)
            {
                if (agg.Function == AggregateFunction.Count)
                {
                    row[agg.ResolvedAlias] = (double?)group.First().Count;
                    continue;
                }

                var matching = group.FirstOrDefault(v =>
                    v.Name != null && v.Name.Equals(agg.SourcePropertyName, StringComparison.OrdinalIgnoreCase));

                if (matching != null)
                {
                    row[agg.ResolvedAlias] = agg.Function switch
                    {
                        AggregateFunction.Sum => matching.Sum,
                        AggregateFunction.Avg => matching.Average,
                        AggregateFunction.Min => matching.Min,
                        AggregateFunction.Max => matching.Max,
                        _ => null
                    };
                }
            }

            results.Add(new AggregateResult(row));
        }

        return results;
    }
}
