using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Aggregation;
using Birko.Data.Stores;
using Birko.Data.Views;
using Nest;

namespace Birko.Data.ElasticSearch.Views;

/// <summary>
/// ElasticSearch implementation of <see cref="IViewStore{TView}"/>.
/// Translates <see cref="ViewDefinition"/> into NEST search requests with optional aggregations.
/// </summary>
/// <remarks>
/// ElasticSearch does not support joins natively. If the view definition contains joins,
/// only primary source fields are queried and join clauses are ignored.
/// For aggregate queries, NEST TermsAggregation + metric sub-aggregations are used.
/// For non-aggregate queries, standard SearchRequest with _source filtering is used.
/// </remarks>
public class ElasticSearchViewStore<TView> : IViewStore<TView> where TView : class, new()
{
    private readonly ElasticClient _connector;
    private readonly ViewDefinition _definition;
    private readonly string _indexName;
    private readonly string[] _sourceFields;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticSearchViewStore{TView}"/> class.
    /// </summary>
    /// <param name="connector">The NEST ElasticClient instance.</param>
    /// <param name="definition">The view definition describing fields, aggregates, and query mode.</param>
    public ElasticSearchViewStore(ElasticClient connector, ViewDefinition definition)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _indexName = ResolveIndexName();
        _sourceFields = ResolveSourceFields();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (_definition.HasAggregates)
        {
            return await ExecuteAggregateQueryAsync(filter, limit, ct).ConfigureAwait(false);
        }

        return await ExecuteSimpleQueryAsync(filter, orderBy, limit, offset, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        var results = await QueryAsync(filter, null, 1, null, ct).ConfigureAwait(false);
        return results.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var filterQuery = BuildFilterQuery(filter);
        var countRequest = new CountRequest(_indexName)
        {
            Query = filterQuery
        };

        var response = await _connector.CountAsync(countRequest, ct).ConfigureAwait(false);

        if (!response.IsValid || response.OriginalException != null)
        {
            throw new InvalidOperationException(
                $"ElasticSearch count failed. Index: {_indexName}. DebugInfo: {response.DebugInformation}",
                response.OriginalException);
        }

        return response.Count;
    }

    #region Simple Query (no aggregates)

    private async Task<IEnumerable<TView>> ExecuteSimpleQueryAsync(
        Expression<Func<TView, bool>>? filter,
        OrderBy<TView>? orderBy,
        int? limit,
        int? offset,
        CancellationToken ct)
    {
        var filterQuery = BuildFilterQuery(filter);

        var from = offset ?? 0;
        var searchRequest = new SearchRequest(_indexName)
        {
            // CR-L118: clamp Size so From + Size stays within the ES default max_result_window (10000);
            // otherwise a non-zero offset with the default size makes the request exceed the window and
            // ES rejects it. For result sets deeper than the window, use search_after / scroll instead.
            Size = ClampWindowSize(from, limit),
            From = from,
            Query = filterQuery
        };

        // Apply _source filtering to return only mapped fields
        if (_sourceFields.Length > 0)
        {
            searchRequest.Source = new SourceFilter
            {
                Includes = _sourceFields
            };
        }

        // Apply sorting
        if (orderBy != null && orderBy.Fields.Count > 0)
        {
            var sorts = new List<ISort>();
            foreach (var field in orderBy.Fields)
            {
                sorts.Add(new FieldSort
                {
                    Field = field.PropertyName,
                    Order = field.Descending ? SortOrder.Descending : SortOrder.Ascending
                });
            }
            searchRequest.Sort = sorts;
        }

        var response = await _connector.SearchAsync<TView>(searchRequest, ct).ConfigureAwait(false);

        if (!response.IsValid || response.OriginalException != null)
        {
            throw new InvalidOperationException(
                $"ElasticSearch view query failed. Index: {_indexName}. DebugInfo: {response.DebugInformation}",
                response.OriginalException);
        }

        return response.Documents;
    }

    #endregion

    #region Aggregate Query

    private async Task<IEnumerable<TView>> ExecuteAggregateQueryAsync(
        Expression<Func<TView, bool>>? filter,
        int? limit,
        CancellationToken ct)
    {
        var filterQuery = BuildFilterQuery(filter);

        // Build metric sub-aggregations from the definition
        var metricAggregations = new AggregationDictionary();
        foreach (var aggregate in _definition.Aggregates)
        {
            var aggName = BuildAggregationName(aggregate);
            var fieldName = aggregate.SourceProperty != null
                ? aggregate.SourceProperty
                : null;

            var agg = StoreAggregationHelper.BuildSingleMetricAggregation(aggregate.Function, aggName, fieldName);

            metricAggregations.Add(aggName, agg);
        }

        AggregationDictionary topLevelAggregations;

        if (_definition.HasGroupBy)
        {
            // Use shared helper for GROUP BY aggregation
            var groupByFieldNames = _definition.GroupBy.Select(g => g.PropertyName).ToList();
            topLevelAggregations = new AggregationDictionary
            {
                { "group_by", StoreAggregationHelper.BuildGroupByAggregation(groupByFieldNames, metricAggregations, limit ?? 10000) }
            };
        }
        else
        {
            // No GROUP BY — metric aggregations at the top level
            topLevelAggregations = metricAggregations;
        }

        var searchRequest = new SearchRequest(_indexName)
        {
            Size = 0,
            Query = filterQuery,
            Aggregations = topLevelAggregations
        };

        var response = await _connector.SearchAsync<TView>(searchRequest, ct).ConfigureAwait(false);

        if (!response.IsValid || response.OriginalException != null)
        {
            throw new InvalidOperationException(
                $"ElasticSearch aggregate view query failed. Index: {_indexName}. DebugInfo: {response.DebugInformation}",
                response.OriginalException);
        }

        if (_definition.HasGroupBy)
        {
            return ParseGroupedAggregateResponse(response);
        }

        return ParseUngroupedAggregateResponse(response);
    }

    private IEnumerable<TView> ParseGroupedAggregateResponse(ISearchResponse<TView> response)
    {
        var results = new List<TView>();
        var viewType = typeof(TView);

        // Try composite aggregation first, then terms
        var compositeBuckets = response.Aggregations.Composite("group_by");
        if (compositeBuckets?.Buckets != null)
        {
            foreach (var bucket in compositeBuckets.Buckets)
            {
                var item = new TView();

                // Set group-by field values from composite key
                foreach (var groupBy in _definition.GroupBy)
                {
                    var gbFieldName = groupBy.PropertyName;
                    if (bucket.Key.TryGetValue(gbFieldName, out string keyValue))
                    {
                        SetPropertyValue(item, viewType, groupBy.PropertyName, keyValue);
                    }
                }

                // Set aggregate values
                SetAggregateValues(item, viewType, bucket);
                results.Add(item);
            }

            return results;
        }

        var termsBuckets = response.Aggregations.Terms("group_by");
        if (termsBuckets?.Buckets == null)
        {
            return results;
        }

        foreach (var bucket in termsBuckets.Buckets)
        {
            var item = new TView();

            // Set the group-by field value
            var groupByProperty = _definition.GroupBy[0].PropertyName;
            SetPropertyValue(item, viewType, groupByProperty, bucket.Key);

            // Set aggregate values
            SetAggregateValues(item, viewType, bucket);
            results.Add(item);
        }

        return results;
    }

    private IEnumerable<TView> ParseUngroupedAggregateResponse(ISearchResponse<TView> response)
    {
        var item = new TView();
        var viewType = typeof(TView);

        // Reuse the shared metric extraction logic via a response-level adapter.
        // response.Aggregations is a BucketBase-compatible aggregate dictionary.
        var aggNames = _definition.Aggregates.Select(a => (BuildAggregationName(a), a.Function));
        var values = StoreAggregationHelper.ExtractMetricValues(response.Aggregations, aggNames);

        foreach (var aggregate in _definition.Aggregates)
        {
            var aggName = BuildAggregationName(aggregate);
            if (values.TryGetValue(aggName, out var value) && value.HasValue)
            {
                SetPropertyValue(item, viewType, aggregate.ViewProperty, value.Value);
            }
        }

        return new[] { item };
    }

    private void SetAggregateValues(TView item, Type viewType, BucketBase bucket)
    {
        var aggNames = _definition.Aggregates.Select(a => (BuildAggregationName(a), a.Function));
        var values = StoreAggregationHelper.ExtractMetricValues(bucket, aggNames);

        for (int i = 0; i < _definition.Aggregates.Count; i++)
        {
            var aggregate = _definition.Aggregates[i];
            var aggName = BuildAggregationName(aggregate);
            if (values.TryGetValue(aggName, out var value) && value.HasValue)
            {
                SetPropertyValue(item, viewType, aggregate.ViewProperty, value.Value);
            }
        }
    }

    #endregion

    #region Filter Translation

    private QueryContainer BuildFilterQuery(Expression<Func<TView, bool>>? filter)
    {
        if (filter == null)
        {
            return new MatchAllQuery();
        }

        // CR-H047 — a supplied filter that can't be translated must NOT silently widen to match-all: that
        // turns a filtered/existence/permission query into a false full-result set. This used to be the
        // ONLY place the invariant was enforced; the main entity stores assigned the parser's result
        // straight to their requests, so the same null widened reads and, worse, reached
        // _delete_by_query / _update_by_query unguarded (TASK-268). The logic now lives in one shared
        // helper that every filter->query conversion routes through, so a new call site cannot forget it.
        var query = Data.ElasticSearch.ElasticSearch.ParseFilterQuery(filter);

        // Unreachable for a non-null filter (the helper throws instead of returning null); kept as a
        // belt-and-braces assertion of this method's own non-null contract.
        return query ?? throw new NotSupportedException(
            $"The filter expression could not be translated to an ElasticSearch query: {filter}.");
    }

    #endregion

    #region Helpers

    private string ResolveIndexName() => ElasticSearchViewIndexResolver.Resolve(_definition);

    private string[] ResolveSourceFields()
    {
        if (_definition.Fields.Count == 0)
        {
            return Array.Empty<string>();
        }

        return _definition.Fields
            .Select(f => f.SourceProperty)
            .ToArray();
    }

    private static string BuildAggregationName(AggregateClause aggregate)
    {
        var functionPrefix = aggregate.Function.ToString().ToLowerInvariant();
        var fieldSuffix = aggregate.SourceProperty ?? "all";
        return $"{functionPrefix}_{fieldSuffix}";
    }

    private static void SetPropertyValue(TView instance, Type viewType, string propertyName, object? value)
    {
        var property = viewType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanWrite || value == null)
        {
            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        // CR-L119: Convert.ChangeType does not handle enum or Guid targets, so those group-by/aggregate
        // columns were always silently dropped; ConvertValue handles them explicitly. Non-convertible
        // values are still left at their default (a mismatch between the property type and the value ES
        // returns), but enum/Guid now round-trip.
        if (ConvertValue(targetType, value, out var convertedValue))
        {
            property.SetValue(instance, convertedValue);
        }
    }

    /// <summary>
    /// Converts an ElasticSearch-returned value to <paramref name="targetType"/>, handling enum and Guid
    /// targets that <see cref="Convert.ChangeType(object, Type)"/> cannot. Returns <c>false</c> (and leaves
    /// <paramref name="converted"/> null) when the value cannot be converted.
    /// </summary>
    internal static bool ConvertValue(Type targetType, object value, out object? converted)
    {
        converted = null;
        try
        {
            if (targetType.IsEnum)
            {
                converted = value is string enumName
                    ? Enum.Parse(targetType, enumName, ignoreCase: true)
                    : Enum.ToObject(targetType, Convert.ChangeType(value, Enum.GetUnderlyingType(targetType)));
                return true;
            }

            if (targetType == typeof(Guid))
            {
                converted = value is Guid g ? g : Guid.Parse(value.ToString()!);
                return true;
            }

            converted = Convert.ChangeType(value, targetType);
            return true;
        }
        catch
        {
            // Value shape does not match the property type — leave the property at its default.
            return false;
        }
    }

    /// <summary>
    /// Clamps the effective page size so that <c>From + Size</c> stays within the ElasticSearch default
    /// <c>max_result_window</c> (10000). Returns 0 once the offset is at or past the window (CR-L118).
    /// </summary>
    internal static int ClampWindowSize(int from, int? limit)
    {
        const int maxResultWindow = 10000;
        var size = limit ?? maxResultWindow;
        if (from < 0)
        {
            from = 0;
        }

        if (from + size > maxResultWindow)
        {
            size = Math.Max(0, maxResultWindow - from);
        }

        return size;
    }

    #endregion
}
