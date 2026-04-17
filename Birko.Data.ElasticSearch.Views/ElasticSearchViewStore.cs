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

        var searchRequest = new SearchRequest(_indexName)
        {
            Size = limit ?? 10000,
            From = offset ?? 0,
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

        // Attempt to translate the expression using the shared ElasticSearch expression parser.
        // The parser handles common binary expressions (==, !=, <, >, <=, >=) and logical
        // operators (&&, ||). For expressions it cannot translate, fall back to MatchAll.
        try
        {
            return Data.ElasticSearch.ElasticSearch.ParseExpression(filter);
        }
        catch
        {
            // If expression parsing fails, fall back to match-all.
            // This is a safety net; callers should prefer simple filter expressions.
            return new MatchAllQuery();
        }
    }

    #endregion

    #region Helpers

    private string ResolveIndexName()
    {
        // For Persistent mode, use the view name as the index (transform destination).
        // For OnTheFly/Auto, use the primary source type name as the index.
        if (_definition.QueryMode == ViewQueryMode.Persistent && !string.IsNullOrEmpty(_definition.Name))
        {
            return _definition.Name!.ToLowerInvariant();
        }

        return _definition.PrimarySource.Name.ToLowerInvariant();
    }

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

        try
        {
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var convertedValue = Convert.ChangeType(value, targetType);
            property.SetValue(instance, convertedValue);
        }
        catch
        {
            // Silently ignore type conversion failures for individual properties.
            // This can happen when ES returns unexpected types (e.g., string for a numeric field).
        }
    }

    #endregion
}
