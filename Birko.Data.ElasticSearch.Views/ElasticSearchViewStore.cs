using System;
using System.Collections.Generic;
using System.Globalization;
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
/// <para>
/// A view reads documents of one type: the primary source's, or, for a Persistent view, view-shaped documents in the
/// view's own index. Every name the caller uses is a VIEW property, and is mapped to the stored field of the
/// property it selects (TASK-151, SH-M382): filters, sorts, group keys and projections alike.
/// </para>
/// <para>
/// ElasticSearch has no joins, so a definition with joins is refused (SH-M411); it used to be read as an unjoined
/// query whose joined properties were all default.
/// </para>
/// <para>
/// A grouping view is a composite aggregation read page by page to the end (SH-M390), so every group is returned;
/// its rows are then ordered, paged and counted as the rows they are (SH-M380, SH-M381).
/// </para>
/// </remarks>
public class ElasticSearchViewStore<TView> : IViewStore<TView> where TView : class, new()
{
    private readonly ElasticClient _connector;
    private readonly ViewDefinition _definition;
    private readonly string _indexName;
    private readonly Type _documentType;

    /// <summary>Groups read per composite-aggregation page.</summary>
    internal int GroupPageSize { get; init; } = 1000;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticSearchViewStore{TView}"/> class.
    /// </summary>
    /// <param name="connector">The NEST ElasticClient instance.</param>
    /// <param name="definition">The view definition describing fields, aggregates, and query mode.</param>
    /// <exception cref="NotSupportedException">The definition joins another source.</exception>
    public ElasticSearchViewStore(ElasticClient connector, ViewDefinition definition)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (definition.HasJoins)
        {
            throw new NotSupportedException(
                $"An ElasticSearch view reads one index and cannot join '{definition.Joins[0].RightType.Name}'. Denormalise the joined fields into the source documents.");
        }
        _indexName = ElasticSearchViewIndexResolver.Resolve(_definition);
        _documentType = _definition.QueryMode == ViewQueryMode.Persistent && !string.IsNullOrEmpty(_definition.Name)
            ? typeof(TView)
            : _definition.PrimarySource;
    }

    private bool Grouped => _definition.HasAggregates || _definition.HasGroupBy;

    /// <inheritdoc />
    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (Grouped)
        {
            IEnumerable<TView> rows = OrderByHelper.ApplyTo(await ReadGroupsAsync(filter, ct).ConfigureAwait(false), orderBy);
            if (offset.HasValue && offset.Value > 0) rows = rows.Skip(offset.Value);
            if (limit.HasValue) rows = rows.Take(limit.Value);
            return rows.ToList();
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

        // SH-M380: a grouping view counts its rows, not the source documents.
        if (Grouped)
        {
            return (await ReadGroupsAsync(filter, ct).ConfigureAwait(false)).Count;
        }

        var countRequest = new CountRequest(_indexName)
        {
            Query = BuildFilterQuery(filter)
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
        var from = offset ?? 0;
        var searchRequest = new SearchRequest(_indexName)
        {
            // CR-L118: clamp Size so From + Size stays within the ES default max_result_window (10000);
            // otherwise a non-zero offset with the default size makes the request exceed the window and
            // ES rejects it. For result sets deeper than the window, use search_after / scroll instead.
            Size = ClampWindowSize(from, limit),
            From = from,
            Query = BuildFilterQuery(filter)
        };

        if (_definition.Fields.Count > 0)
        {
            searchRequest.Source = new SourceFilter
            {
                Includes = _definition.Fields.Select(f => StoredField(SourceOf(f), exact: false)).ToArray()
            };
        }

        if (orderBy != null && orderBy.Fields.Count > 0)
        {
            searchRequest.Sort = orderBy.Fields
                .Select(f => (ISort)new FieldSort
                {
                    Field = StoredField(SourceOfViewProperty(f.PropertyName), exact: true),
                    Order = f.Descending ? SortOrder.Descending : SortOrder.Ascending
                })
                .ToList();
        }

        var documents = await (Task<IReadOnlyCollection<object>>)typeof(ElasticSearchViewStore<TView>)
            .GetMethod(nameof(SearchDocumentsAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(_documentType)
            .Invoke(this, new object[] { searchRequest, ct })!;

        if (_documentType == typeof(TView) && _definition.Fields.Count == 0)
        {
            return documents.Cast<TView>().ToList();
        }

        return documents.Select(Project).ToList();
    }

    private async Task<IReadOnlyCollection<object>> SearchDocumentsAsync<TDocument>(SearchRequest request, CancellationToken ct)
        where TDocument : class
    {
        var response = await _connector.SearchAsync<TDocument>(request, ct).ConfigureAwait(false);

        if (!response.IsValid || response.OriginalException != null)
        {
            throw new InvalidOperationException(
                $"ElasticSearch view query failed. Index: {_indexName}. DebugInfo: {response.DebugInformation}",
                response.OriginalException);
        }

        return response.Documents.Cast<object>().ToList();
    }

    /// <summary>A view row from a document: each field read from the property it selects (SH-M382).</summary>
    private TView Project(object document)
    {
        var row = new TView();
        foreach (var field in _definition.Fields)
        {
            var source = _documentType.GetProperty(SourceOf(field), BindingFlags.Public | BindingFlags.Instance)
                ?? throw new NotSupportedException($"'{_documentType.Name}' has no property '{SourceOf(field)}' for view field '{field.ViewProperty}'.");
            SetPropertyValue(row, field.ViewProperty, source.GetValue(document));
        }
        return row;
    }

    #endregion

    #region Aggregate Query

    /// <summary>
    /// Every row of a grouping view: one per group, read through a composite aggregation page by page, or the one
    /// row of an aggregate with no grouping.
    /// </summary>
    private async Task<List<TView>> ReadGroupsAsync(Expression<Func<TView, bool>>? filter, CancellationToken ct)
    {
        var filterQuery = BuildFilterQuery(filter);

        var metricAggregations = new AggregationDictionary();
        var metricNames = new List<(string Name, AggregateFunction Function)>();
        for (int i = 0; i < _definition.Aggregates.Count; i++)
        {
            var aggregate = _definition.Aggregates[i];
            // Named by position: two aggregates of one function over one field collided under "{fn}_{field}".
            var name = "m" + i.ToString(CultureInfo.InvariantCulture);
            var field = aggregate.SourceProperty != null ? StoredField(aggregate.SourceProperty, exact: true) : null;
            metricAggregations.Add(name, StoreAggregationHelper.BuildSingleMetricAggregation(aggregate.Function, name, field));
            metricNames.Add((name, aggregate.Function));
        }

        if (!_definition.HasGroupBy)
        {
            var response = await SearchAsync(new SearchRequest(_indexName) { Size = 0, Query = filterQuery, Aggregations = metricAggregations }, ct).ConfigureAwait(false);
            var row = new TView();
            SetMetrics(row, StoreAggregationHelper.ExtractMetricValues(response.Aggregations, metricNames));
            return new List<TView> { row };
        }

        // SH-M387, SH-M389: one composite source per group key, named by position, whatever the number of keys; a
        // key is written to the VIEW property that selects it. SH-M390: read to the last page, not the first 10000.
        var sources = _definition.GroupBy
            .Select((g, i) => (ICompositeAggregationSource)new TermsCompositeAggregationSource("k" + i.ToString(CultureInfo.InvariantCulture))
            {
                Field = StoredField(g.PropertyName, exact: true),
                MissingBucket = true
            })
            .ToList();
        var keyProperties = _definition.GroupBy.Select(FindViewPropertyForGroupBy).ToList();

        var rows = new List<TView>();
        CompositeKey? after = null;
        while (true)
        {
            var composite = new CompositeAggregation("group_by")
            {
                Size = GroupPageSize,
                Sources = sources,
                Aggregations = metricAggregations,
                After = after
            };
            var response = await SearchAsync(new SearchRequest(_indexName)
            {
                Size = 0,
                Query = filterQuery,
                Aggregations = new AggregationDictionary { { "group_by", composite } }
            }, ct).ConfigureAwait(false);

            var page = response.Aggregations.Composite("group_by")
                ?? throw new InvalidOperationException($"ElasticSearch returned no group_by aggregation for view '{typeof(TView).Name}'.");
            var buckets = page.Buckets.ToList();
            foreach (var bucket in buckets)
            {
                var row = new TView();
                IReadOnlyDictionary<string, object> key = bucket.Key;
                for (int i = 0; i < keyProperties.Count; i++)
                {
                    if (key.TryGetValue("k" + i.ToString(CultureInfo.InvariantCulture), out var value))
                    {
                        SetPropertyValue(row, keyProperties[i], value);
                    }
                }
                SetMetrics(row, StoreAggregationHelper.ExtractMetricValues(bucket, metricNames));
                rows.Add(row);
            }

            if (buckets.Count < GroupPageSize || page.AfterKey == null)
            {
                return rows;
            }
            after = page.AfterKey;
        }
    }

    private async Task<ISearchResponse<object>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var response = await _connector.SearchAsync<object>(request, ct).ConfigureAwait(false);
        if (!response.IsValid || response.OriginalException != null)
        {
            throw new InvalidOperationException(
                $"ElasticSearch aggregate view query failed. Index: {_indexName}. DebugInfo: {response.DebugInformation}",
                response.OriginalException);
        }
        return response;
    }

    private void SetMetrics(TView row, Dictionary<string, double?> values)
    {
        for (int i = 0; i < _definition.Aggregates.Count; i++)
        {
            if (values.TryGetValue("m" + i.ToString(CultureInfo.InvariantCulture), out var value) && value.HasValue)
            {
                SetPropertyValue(row, _definition.Aggregates[i].ViewProperty, value.Value);
            }
        }
    }

    private string FindViewPropertyForGroupBy(GroupByClause groupBy)
    {
        var field = _definition.Fields.FirstOrDefault(f =>
            f.SourceType == groupBy.SourceType &&
            f.SourceProperty == groupBy.PropertyName);

        return field?.ViewProperty ?? groupBy.PropertyName;
    }

    #endregion

    #region Names

    /// <summary>The document property a view field reads: its source's, or its own in a Persistent view's index.</summary>
    private string SourceOf(FieldSelector field) => _documentType == typeof(TView) ? field.ViewProperty : field.SourceProperty;

    /// <summary>The document property behind a view property a caller named (in a filter or a sort).</summary>
    private string SourceOfViewProperty(string viewProperty)
    {
        // A WHERE runs on the documents: an aggregate exists only after grouping (HAVING).
        if (_definition.Aggregates.Any(a => a.ViewProperty == viewProperty))
        {
            throw new NotSupportedException(
                $"A filter on aggregate view property '{typeof(TView).Name}.{viewProperty}' would need HAVING, which view filters do not emit.");
        }

        var field = _definition.Fields.FirstOrDefault(f => f.ViewProperty == viewProperty);
        if (field != null) return SourceOf(field);
        if (_documentType.GetProperty(viewProperty, BindingFlags.Public | BindingFlags.Instance) != null) return viewProperty;
        throw new NotSupportedException($"View property '{typeof(TView).Name}.{viewProperty}' selects no field of '{_documentType.Name}'.");
    }

    /// <summary>The stored field of a document property, through the one producer the entity store uses.</summary>
    private string StoredField(string property, bool exact)
    {
        var member = _documentType.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new NotSupportedException($"'{_documentType.Name}' has no property '{property}'.");
        var name = exact
            ? Data.ElasticSearch.ElasticSearch.ExactFieldName(member, member.PropertyType, null)
            : Data.ElasticSearch.ElasticSearch.StoredFieldName(member, null);
        return name ?? throw new NotSupportedException($"'{_documentType.Name}.{property}' has no stored field.");
    }

    #endregion

    #region Filter Translation

    private QueryContainer BuildFilterQuery(Expression<Func<TView, bool>>? filter)
    {
        if (filter == null)
        {
            return new MatchAllQuery();
        }

        // SH-M382: the filter names VIEW properties; it is rewritten onto the document type, so it tests the
        // stored field each one selects. Translated through the same parser as the entity store.
        LambdaExpression documentFilter = filter;
        if (_documentType != typeof(TView) || _definition.Fields.Count > 0)
        {
            var parameter = Expression.Parameter(_documentType, filter.Parameters[0].Name);
            var body = new ViewToDocument(filter.Parameters[0], parameter, this).Visit(filter.Body);
            documentFilter = Expression.Lambda(body, parameter);
        }

        // CR-H047 — a supplied filter that can't be translated must NOT silently widen to match-all: that
        // turns a filtered/existence/permission query into a false full-result set (TASK-268).
        QueryBase? query;
        try
        {
            query = Data.ElasticSearch.ElasticSearch.ParseExpression(documentFilter);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw new NotSupportedException($"The filter expression could not be translated to an ElasticSearch query: {filter}.", ex);
        }

        return query ?? throw new NotSupportedException(
            $"The filter expression could not be translated to an ElasticSearch query: {filter}.");
    }

    private sealed class ViewToDocument : ExpressionVisitor
    {
        private readonly ParameterExpression _view;
        private readonly ParameterExpression _document;
        private readonly ElasticSearchViewStore<TView> _store;

        public ViewToDocument(ParameterExpression view, ParameterExpression document, ElasticSearchViewStore<TView> store)
        {
            _view = view;
            _document = document;
            _store = store;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != _view)
            {
                return base.VisitMember(node);
            }

            var source = Expression.Property(_document, _store.SourceOfViewProperty(node.Member.Name));
            return source.Type == node.Type ? source : Expression.Convert(source, node.Type);
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _view
                ? throw new NotSupportedException($"A view filter may only read view properties, not the view row itself.")
                : base.VisitParameter(node);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Sets a view property, converting what ElasticSearch returned. A value that cannot be converted throws: it
    /// was left at the property's default, so a date minimum read as epoch milliseconds came back as
    /// <c>DateTime.MinValue</c> (SH-M388).
    /// </summary>
    private static void SetPropertyValue(TView instance, string propertyName, object? value)
    {
        var property = typeof(TView).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanWrite)
        {
            throw new NotSupportedException($"View '{typeof(TView).Name}' has no writable property '{propertyName}'.");
        }
        if (value == null)
        {
            return;
        }

        if (property.PropertyType.IsInstanceOfType(value))
        {
            property.SetValue(instance, value);
            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (!ConvertValue(targetType, value, out var convertedValue))
        {
            throw new InvalidOperationException(
                $"ElasticSearch returned {value.GetType().Name} '{value}' for view property '{typeof(TView).Name}.{propertyName}', which is {targetType.Name}.");
        }
        property.SetValue(instance, convertedValue);
    }

    /// <summary>
    /// Converts an ElasticSearch-returned value to <paramref name="targetType"/>, handling enum and Guid
    /// targets that <see cref="Convert.ChangeType(object, Type)"/> cannot, and dates, which ElasticSearch returns
    /// from an aggregation as epoch milliseconds. Returns <c>false</c> (and leaves <paramref name="converted"/>
    /// null) when the value cannot be converted.
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
                    : Enum.ToObject(targetType, Convert.ChangeType(value, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture));
                return true;
            }

            if (targetType == typeof(Guid))
            {
                converted = value is Guid g ? g : Guid.Parse(value.ToString()!);
                return true;
            }

            if (targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset))
            {
                DateTimeOffset instant = value switch
                {
                    DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, dt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : dt.Kind)),
                    DateTimeOffset dto => dto,
                    string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                    _ => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                };
                converted = targetType == typeof(DateTime) ? (object)instant.UtcDateTime : instant;
                return true;
            }

            converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException || ex is ArgumentException)
        {
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
