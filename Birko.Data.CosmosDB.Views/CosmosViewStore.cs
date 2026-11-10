using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using Birko.Data.Views;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Birko.Data.CosmosDB.Views;

/// <summary>
/// Azure Cosmos DB (NoSQL API) implementation of <see cref="IViewStore{TView}"/>.
/// Builds Cosmos SQL itself, with filter values bound as parameters by <c>CosmosFilterTranslator</c>: a projection
/// for non-aggregate views, GROUP BY for aggregate ones, both run through <c>GetItemQueryIterator</c>.
/// Joins are not supported (Cosmos DB does not support cross-container joins).
/// </summary>
/// <remarks>
/// <para>A collection <c>Contains</c> in a filter (TASK-839) is <c>ARRAY_CONTAINS</c>, an exact comparison, in two
/// shapes: <c>ids.Contains(v.Key)</c>, the list bound as one array parameter, and <c>v.Tags.Contains(x)</c> on a view
/// array property. Guid, int, string and enum values; an empty list matches nothing; <c>!</c> and <c>||</c> compose.
/// Other shapes (a comparer, a computed operand, a nested collection) are refused by name before any request.
/// Before TASK-837 a collection Contains rendered <c>CONTAINS()</c> and matched nothing.</para>
/// <para>A member path under a view property (<c>v.Inner.X</c>, also in a <c>Contains</c>) renders in full,
/// <c>c["Src"]["X"]</c>, only its first segment mapped to the source field (TASK-840; it used to render <c>c.X</c> and read the
/// wrong field). A member that is not a document field (<c>Length</c>, <c>Count</c>, a struct's member) or a
/// captured variable is refused by name before any request.</para>
/// <para>A filter takes the store's own pre-pass, <c>CosmosFilterRewrite.Prepare</c> (TASK-896): <c>HasValue</c>, <c>== null</c>,
/// <c>!=</c>, a negated comparison and a list holding null answer a stored null and a missing member as C# does, and
/// <c>string.IsNullOrEmpty</c> and an ordinal string comparison translate; culture comparisons and
/// <c>IsNullOrWhiteSpace</c> are refused by name.</para>
/// <para>The server counts the serialized parameter values toward its 524,288-character query limit (measured on the
/// emulator, 2026-11-07: 13,795 Guids pass, 25,000 fail with SC3020), so a list longer than
/// <see cref="InListChunkSize"/> is split by TASK-834's planner (<c>CosmosInListChunking</c>): a read or count whose
/// list is a plain AND conjunct runs once per chunk of distinct values (strings included: Cosmos DB compares them
/// exactly, TASK-841), rows concatenated and counts summed. Past <see cref="MaxQueryTextLength"/> a list that cannot
/// be split (under NOT or OR, a sort, limit or offset, <see cref="QueryFirstAsync"/>, or a grouped view) is refused with
/// <see cref="NotSupportedException"/> before any request; below it, it runs as one query.</para>
/// </remarks>
public class CosmosViewStore<TView> : IViewStore<TView> where TView : class, new()
{
    private readonly Container _container;
    private readonly ViewDefinition _definition;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosViewStore{TView}"/> class.
    /// </summary>
    /// <param name="container">The Cosmos DB container to query.</param>
    /// <param name="definition">The view definition describing fields, aggregates, and grouping.</param>
    public CosmosViewStore(Container container, ViewDefinition definition)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));

        // TASK-151 (SH-M411): joins were accepted and never read, so every joined view property stayed default. A
        // Cosmos query reads one container, with no cross-document join.
        if (definition.HasJoins)
        {
            throw new NotSupportedException(
                $"A Cosmos DB view reads one container and cannot join '{definition.Joins[0].RightType.Name}'. Denormalise the joined fields into the source documents.");
        }
    }

    /// <summary>
    /// TASK-151 (SH-M407): Cosmos DB has no stored views, so a Persistent definition (which promises one) is refused
    /// rather than silently computed from the source container; OnTheFly and Auto compute on the fly.
    /// </summary>
    private void RequireComputable()
    {
        if (_definition.QueryMode == ViewQueryMode.Persistent)
        {
            throw new NotSupportedException(
                $"Cosmos DB has no stored views: view '{_definition.Name ?? typeof(TView).Name}' cannot be Persistent. Use OnTheFly or Auto.");
        }
    }

    private bool Grouped => _definition.HasAggregates || _definition.HasGroupBy;

    /// <summary>Distinct list values per query when a filter's <c>Contains</c> list is split (TASK-839).</summary>
    protected virtual int InListChunkSize => CosmosInListChunking.DefaultChunkSize;

    /// <summary>
    /// The server's query limit in characters, counted over the query text plus the serialized parameter values
    /// (TASK-839, measured on the emulator).
    /// </summary>
    protected virtual int MaxQueryTextLength => CosmosInListChunking.DefaultMaxQueryTextLength;

    /// <summary>
    /// Plans a non-grouped query's filter through TASK-834's planner: <c>null</c> runs it as one query, otherwise the
    /// chunk filters. Refuses before any request what cannot be split past the limit.
    /// </summary>
    private IReadOnlyList<Expression<Func<TView, bool>>>? PlanChunks(
        Expression<Func<TView, bool>>? filter,
        string operation,
        bool sortedOrPaged,
        Func<Expression<Func<TView, bool>>, QueryDefinition> build)
        => CosmosInListChunking.Plan(filter, operation, InListChunkSize, MaxQueryTextLength, sortedOrPaged, f => Length(build(f)));

    /// <summary>
    /// A grouped view's query is never split: a group can span chunks, so its rows and its count cannot be combined.
    /// Past the limit it is refused before any request.
    /// </summary>
    private void RequireUnsplitGrouped(QueryDefinition query, string operation)
    {
        var length = Length(query);
        if (length <= MaxQueryTextLength) return;
        throw new NotSupportedException(
            $"Cosmos DB view {operation} of {typeof(TView).Name} refused: its query text and parameter values would be "
            + $"{length} characters, over Cosmos DB's limit of {MaxQueryTextLength}. A grouped view cannot be split into "
            + "chunks, because a group can span them. Split the list in the caller, or narrow the filter. (TASK-839)");
    }

    /// <summary>The query text plus each parameter value as JSON: what the server counts against its limit.</summary>
    private static int Length(QueryDefinition query)
        => query.QueryText.Length
           + query.GetQueryParameters().Sum(p => System.Text.Json.JsonSerializer.Serialize(p.Value).Length);

    /// <inheritdoc />
    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        RequireComputable();
        filter = Birko.Data.Expressions.SpanContains.Rewrite(filter);

        // Take the SQL path for group-by-only (distinct/grouping) views too, not just aggregate views —
        // otherwise a HasGroupBy && !HasAggregates view would return raw ungrouped docs via LINQ (CR-L110).
        if (Grouped)
        {
            RequireUnsplitGrouped(BuildAggregateSql(filter, orderBy, limit, offset), "read");
            return await QueryAggregateAsync(filter, orderBy, limit, offset, ct).ConfigureAwait(false);
        }

        var sortedOrPaged = orderBy?.Fields.Count > 0 || limit.HasValue || offset.HasValue;
        var dateTimeOrder = CosmosDateTimeOrder.Plan(orderBy, "view " + typeof(TView).Name);   // TASK-902
        var chunks = PlanChunks(filter, "view read", sortedOrPaged, f => BuildProjectionSql(f, orderBy, limit, offset));
        if (chunks == null && dateTimeOrder != null)
        {
            return await CosmosDateTimeOrder.ReadAsync(filter, dateTimeOrder, limit, offset,
                async (f, l, o) => await RunAsync<TView>(BuildProjectionSql(f, orderBy, l, o), ct).ConfigureAwait(false)).ConfigureAwait(false);
        }
        if (chunks == null)
        {
            return await RunAsync<TView>(BuildProjectionSql(filter, orderBy, limit, offset), ct).ConfigureAwait(false);
        }

        // Disjoint chunks of distinct values, no sort or paging: the union is the concatenation.
        var rows = new List<TView>();
        foreach (var chunk in chunks)
        {
            rows.AddRange(await RunAsync<TView>(BuildProjectionSql(chunk, null, null, null), ct).ConfigureAwait(false));
        }
        return rows;
    }

    /// <inheritdoc />
    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        RequireComputable();
        filter = Birko.Data.Expressions.SpanContains.Rewrite(filter);

        // TASK-151 (SH-M379): grouped on the same condition as QueryAsync; a group-by-only view answered a raw document.
        if (Grouped)
        {
            RequireUnsplitGrouped(BuildAggregateSql(filter, null, 1, null), "first read");
            var results = await QueryAggregateAsync(filter, null, 1, null, ct).ConfigureAwait(false);
            return results.FirstOrDefault();
        }

        // LIMIT 1 is paging: a list past the limit is refused, never split (the planner returns null or throws).
        PlanChunks(filter, "view first read", true, f => BuildProjectionSql(f, null, 1, null));
        return (await RunAsync<TView>(BuildProjectionSql(filter, null, 1, null), ct).ConfigureAwait(false)).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        RequireComputable();
        filter = Birko.Data.Expressions.SpanContains.Rewrite(filter);

        if (Grouped)
        {
            RequireUnsplitGrouped(BuildAggregateSql(filter, null, null, null), "count");
            return await CountAggregateAsync(filter, ct).ConfigureAwait(false);
        }

        var chunks = PlanChunks(filter, "view count", false, f => BuildCountSql(f));
        if (chunks == null)
        {
            return (await RunAsync<long>(BuildCountSql(filter), ct).ConfigureAwait(false)).Sum();
        }

        // Disjoint chunks of distinct values: the counts add up.
        long total = 0;
        foreach (var chunk in chunks)
        {
            total += (await RunAsync<long>(BuildCountSql(chunk), ct).ConfigureAwait(false)).Sum();
        }
        return total;
    }

    private QueryDefinition BuildCountSql(Expression<Func<TView, bool>>? filter)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        return Bind("SELECT VALUE COUNT(1) FROM c" + Where(filter, parameters), parameters);
    }

    #region Projection (non-aggregate)

    /// <summary>
    /// SELECT each view field from its source field, under the view's name.
    /// </summary>
    /// <remarks>
    /// TASK-151 (SH-M410): the LINQ path deserialised the raw documents into the view, so a renamed field stayed
    /// default, and a filter or order on a view name tested a document field that does not exist.
    /// TASK-843: a nested source is selected by its whole path, <c>c["Sub"]["X"]</c>; it used to be <c>c["X"]</c>, the
    /// document's top-level field (measured on the emulator).
    /// TASK-844: the alias is a quoted string, <c>AS "Inner"</c>; bare, a view property named like a keyword failed
    /// every query with a 400.
    /// </remarks>
    private QueryDefinition BuildProjectionSql(Expression<Func<TView, bool>>? filter, OrderBy<TView>? orderBy, int? limit, int? offset)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        var sb = new StringBuilder("SELECT ");
        sb.Append(_definition.Fields.Count == 0
            ? "VALUE c"
            : string.Join(", ", _definition.Fields.Select(f => $"{CosmosAggregationHelper.PathRef(f.SourcePath)} AS {CosmosAggregationHelper.Alias(f.ViewProperty)}")));
        sb.Append(" FROM c").Append(Where(filter, parameters));
        AppendOrderAndPage(sb, orderBy, limit, offset);
        return Bind(sb.ToString(), parameters);
    }

    private string Where(Expression<Func<TView, bool>>? filter, List<KeyValuePair<string, object?>> parameters)
    {
        if (filter == null) return string.Empty;
        var whereClause = CosmosFilterTranslator.Translate(filter, parameters, MapViewPropertyToSource);
        return string.IsNullOrEmpty(whereClause) ? string.Empty : " WHERE " + whereClause;
    }

    private void AppendOrderAndPage(StringBuilder sb, OrderBy<TView>? orderBy, int? limit, int? offset)
    {
        // ORDER BY clause — order keys are view property names; map group keys back to their source
        // field (CR-H044). An aggregate key is refused (TASK-848).
        if (orderBy?.Fields.Count > 0)
        {
            var orderParts = orderBy.Fields
                .Select(f => $"{ResolveOrderKey(f.PropertyName)}{(f.Descending ? " DESC" : " ASC")}");
            sb.Append(" ORDER BY ").Append(string.Join(", ", orderParts));
        }

        // OFFSET ... LIMIT
        if (offset.HasValue || limit.HasValue)
        {
            // Not parameterised, deliberately: both are `int?` from this store's own API, so no
            // caller text reaches the statement and there is nothing to contain.
            sb.Append($" OFFSET {offset ?? 0} LIMIT {limit ?? int.MaxValue}");
        }
    }

    private async Task<List<TResult>> RunAsync<TResult>(QueryDefinition query, CancellationToken ct)
    {
        var results = new List<TResult>();
        using var iterator = _container.GetItemQueryIterator<TResult>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct).ConfigureAwait(false);
            results.AddRange(response);
        }
        return results;
    }

    #endregion

    #region SQL-based queries (aggregate with GROUP BY)

    private async Task<IEnumerable<TView>> QueryAggregateAsync(
        Expression<Func<TView, bool>>? filter,
        OrderBy<TView>? orderBy,
        int? limit,
        int? offset,
        CancellationToken ct)
    {
        var queryDef = BuildAggregateSql(filter, orderBy, limit, offset);

        var results = new List<TView>();
        using var iterator = _container.GetItemQueryIterator<TView>(queryDef);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct).ConfigureAwait(false);
            results.AddRange(response);
        }

        RoundDecimalSums(results);
        return results;
    }

    /// <summary>
    /// TASK-899: a decimal SUM as the store aggregate answers it (TASK-897): Cosmos DB adds in double, and the container's
    /// serializer read that double into the view property, so 0.1 + 0.2 read 0.30000000000000004 (measured on the
    /// emulator, vnext-preview, 2026-11-10). It is now the double to 15 significant digits, <c>(decimal)(double)x</c>, the
    /// conversion the store applies, so 0.1 + 0.2 is 0.3. Exact while the sum and its addends hold at most 15 significant
    /// digits; past that it is Cosmos DB's double, rounded. Sorting by an aggregate is refused (TASK-848), so rounding after
    /// the read changes no order and no page.
    /// </summary>
    private void RoundDecimalSums(List<TView> rows)
    {
        foreach (var aggregate in _definition.Aggregates)
        {
            if (aggregate.Function != AggregateFunction.Sum || aggregate.SourceProperty == null) continue;
            var source = aggregate.SourceType.GetProperty(aggregate.SourceProperty);
            if (source == null || (Nullable.GetUnderlyingType(source.PropertyType) ?? source.PropertyType) != typeof(decimal)) continue;
            var target = typeof(TView).GetProperty(aggregate.ViewProperty);
            if (target == null || !target.CanWrite
                || (Nullable.GetUnderlyingType(target.PropertyType) ?? target.PropertyType) != typeof(decimal)) continue;
            foreach (var row in rows)
            {
                if (target.GetValue(row) is decimal sum) target.SetValue(row, (decimal)(double)sum);
            }
        }
    }

    private async Task<long> CountAggregateAsync(
        Expression<Func<TView, bool>>? filter,
        CancellationToken ct)
    {
        // TASK-151 (SH-M378): the count is the number of groups the view's own query returns. The old statement
        // nested a GROUP BY under FROM and read a bare COUNT VALUE into an object's Count member, so it answered 0
        // or the document count. A view's groups are what it returns, so they are read and counted.
        var groups = await RunAsync<object>(BuildAggregateSql(filter, null, null, null), ct).ConfigureAwait(false);
        return groups.Count;
    }

    private QueryDefinition BuildAggregateSql(
        Expression<Func<TView, bool>>? filter,
        OrderBy<TView>? orderBy,
        int? limit,
        int? offset)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        // Build SELECT ... FROM c and GROUP BY parts separately via shared helper
        // TASK-899: a DateTime group key is grouped on its wall clock, as the store aggregate groups it.
        var groupByFields = _definition.GroupBy
            .Where(g => !IsDateTimeGroup(g))
            .Select(g => (g.PropertyName, FindViewPropertyForGroupBy(g)));
        var dateTimeGroups = _definition.GroupBy
            .Where(IsDateTimeGroup)
            .Select(g => (g.PropertyName, FindViewPropertyForGroupBy(g)))
            .ToList();
        var aggregateDefs = _definition.Aggregates
            .Select(a => (a.Function, a.SourceProperty, a.ViewProperty, AggregateMemberType(a)))
            .ToList();
        var (selectFromSql, groupBySql) = CosmosAggregationHelper.BuildAggregateSqlParts(groupByFields, aggregateDefs, dateTimeGroups);

        var sb = new StringBuilder(selectFromSql);

        // WHERE clause (must come before GROUP BY). Translate view property names to source field
        // names — the query runs against the raw documents (FROM c) (CR-H045).
        sb.Append(Where(filter, parameters));

        // GROUP BY clause
        if (groupBySql != null)
        {
            sb.Append(groupBySql);
        }

        AppendOrderAndPage(sb, orderBy, limit, offset);

        return Bind(sb.ToString(), parameters);
    }

    /// <summary>
    /// TASK-897: the source member's type of an aggregate, so a MIN/MAX of a DateTime compares its wall-clock key
    /// (<c>CosmosAggregationHelper.WallClockKey</c>), or a refusal before any request, the store aggregate's own
    /// (<c>CosmosAggregationHelper.ResultType</c>): a SUM/AVG of a non-numeric member with LINQ's exception and message, a
    /// MIN/MAX of a string (Cosmos DB orders by code point, C# by culture) or of another type whose text does not order as
    /// its value, by name.
    /// </summary>
    /// <remarks>
    /// Measured before (vnext-preview, 2026-11-10): MIN/MAX compared the stored text, so a group holding <c>…10:00:00Z</c> and
    /// <c>…10:00:00.5Z</c> answered the half second as its MIN; a SUM of a string read the view property's default. The
    /// view's result is read by the container's serializer into the view property's type, so the store's type conversion
    /// does not apply here.
    /// TASK-899: a decimal AVG is refused by name. Cosmos DB averages in double (1, 0, 0 read 0.3333333333333333 where C# has
    /// 28 digits, measured), and the exact answer, a decimal SUM divided by the count as the store aggregate divides it,
    /// needs both read from the response, where a view reads only its own properties through the container's serializer.
    /// </remarks>
    private static Type? AggregateMemberType(AggregateClause aggregate)
    {
        if (aggregate.Function == AggregateFunction.Count || aggregate.SourceProperty == null) return null;
        var member = aggregate.SourceType.GetProperty(aggregate.SourceProperty);
        if (member == null) return null;
        var resultType = CosmosAggregationHelper.ResultType(aggregate.Function, member, typeof(TView).Name);
        if (aggregate.Function == AggregateFunction.Avg && resultType == typeof(decimal))
        {
            throw new NotSupportedException(
                $"Cosmos DB view {typeof(TView).Name} refused: Avg of {member.DeclaringType?.Name}.{member.Name}, a Decimal. "
                + "Cosmos DB averages in double, so the answer would not be C#'s decimal average. Select its Sum and a Count "
                + "and divide them in the caller. (TASK-899)");
        }
        return member.PropertyType;
    }

    /// <summary>TASK-899: the group-by member is a <see cref="DateTime"/>, grouped on its wall clock.</summary>
    private static bool IsDateTimeGroup(GroupByClause groupBy)
    {
        var member = groupBy.SourceType.GetProperty(groupBy.PropertyName);
        return member != null && (Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType) == typeof(DateTime);
    }

    private static QueryDefinition Bind(string sql, List<KeyValuePair<string, object?>> parameters)
    {
        var queryDef = new QueryDefinition(sql);
        foreach (var p in parameters)
        {
            queryDef = queryDef.WithParameter(p.Key, p.Value);
        }

        return queryDef;
    }

    /// <summary>
    /// Finds the view property name that a GroupBy clause maps to, using the Fields collection.
    /// Falls back to the GroupBy property name if no matching field selector is found.
    /// </summary>
    private string FindViewPropertyForGroupBy(GroupByClause groupBy)
    {
        var field = _definition.Fields.FirstOrDefault(f =>
            f.SourceType == groupBy.SourceType &&
            f.SourceProperty == groupBy.PropertyName);

        return field?.ViewProperty ?? groupBy.PropertyName;
    }

    /// <summary>
    /// Maps a TView property name to its raw source-document field name (CR-H045). Falls back to
    /// the name unchanged when no renamed field matches (identity fields, id, etc.).
    /// </summary>
    private string MapViewPropertyToSource(string viewProperty)
    {
        // A WHERE runs before grouping, against the documents: an aggregate exists only after it (HAVING).
        if (_definition.Aggregates.Any(a => a.ViewProperty == viewProperty))
        {
            throw new NotSupportedException(
                $"A filter on aggregate view property '{typeof(TView).Name}.{viewProperty}' would need HAVING, which view filters do not emit.");
        }

        // A nested source (TASK-843) maps to its dotted path, Sub.X, which DocumentPath and ResolveOrderKey render as
        // c["Sub"]["X"], the same path the projection selects.
        var field = _definition.Fields.FirstOrDefault(f => f.ViewProperty == viewProperty);
        return field?.SourceProperty ?? viewProperty;
    }

    /// <summary>
    /// Resolves an ORDER BY key expressed as a TView property name (CR-H044): a group key maps back
    /// to its source field (<c>c["Source"]</c>, bracket-quoted, TASK-844). An aggregate view property is refused.
    /// </summary>
    /// <remarks>
    /// TASK-848: Cosmos DB cannot sort a grouped query by an aggregate in any form. Measured on the emulator
    /// (vnext-preview, 2026-11-07): a bare alias (<c>ORDER BY Total</c>) is SC2001, a quoted alias or the aggregate
    /// itself (<c>ORDER BY SUM(c["Amount"])</c>) is 2206, also as a second key after a group key; a group key sorts and
    /// pages correctly. The store used to send the statement and get a 400. Not sorted in memory instead: a grouped read
    /// pages on the server (<c>OFFSET … LIMIT</c>, measured), so a correct page would need every group read first, an
    /// unbounded read behind a paged call. Refused by name before any request, as every statement this store builds is.
    /// </remarks>
    private string ResolveOrderKey(string viewProperty)
    {
        if (_definition.Aggregates.Any(a => a.ViewProperty == viewProperty))
        {
            throw new NotSupportedException(
                $"Cosmos DB cannot sort a grouped view by an aggregate: '{typeof(TView).Name}.{viewProperty}' is one. "
                + "Sort by a group key, or read the groups and sort them in the caller. (TASK-848)");
        }

        // TASK-899: a DateTime group is grouped on an expression (its wall clock), and Cosmos DB orders only by a document
        // path: measured on the emulator (vnext-preview, 2026-11-10), ORDER BY the expression is 2206 ("could not be mapped
        // to a document path"), ORDER BY the field SC2103 (not a group key). Ordering by the stored text, as before, put
        // ...10:00:00.5Z before ...10:00:00Z and grouped a UTC, a local and an unspecified time apart.
        if (Grouped && _definition.GroupBy.Any(g => IsDateTimeGroup(g) && FindViewPropertyForGroupBy(g) == viewProperty))
        {
            throw new NotSupportedException(
                $"Cosmos DB cannot sort a grouped view by a DateTime group key: '{typeof(TView).Name}.{viewProperty}' is grouped "
                + "on its wall clock, an expression, and Cosmos DB sorts only by a document path. Sort by another group key, "
                + "or read the groups and sort them in the caller. (TASK-899)");
        }

        // Group key / passthrough field — order by the source field the GROUP BY uses, bracket-quoted (TASK-844: a
        // dotted c.Value is a 400 syntax error).
        return CosmosAggregationHelper.PathRef(MapViewPropertyToSource(viewProperty).Split('.'));
    }

    #endregion


}

/// <summary>
/// Translates simple <see cref="Expression"/> filters into Cosmos SQL WHERE clauses.
/// Supports basic binary comparisons (==, !=, &lt;, &gt;, &lt;=, &gt;=) and logical AND/OR.
/// Complex expressions should use LINQ-based queries instead.
/// </summary>
internal static class CosmosFilterTranslator
{
    /// <summary>
    /// Translates a filter expression into a Cosmos SQL WHERE clause string.
    /// </summary>
    /// <returns>
    /// The WHERE clause, or an empty string when the predicate is the explicit constant
    /// <c>true</c> -- the one predicate that constrains nothing. An empty return therefore means
    /// exactly that, and never "translation failed".
    /// </returns>
    /// <param name="parameters">
    /// Collects every value the filter compares against, as <c>@pN</c> bindings the caller attaches
    /// to the <c>QueryDefinition</c>. TASK-447: values used to be rendered into the statement as
    /// quoted literals with only <c>'</c> escaped, so a value containing a backslash broke out of its
    /// own literal and the remainder was parsed as SQL. A bound parameter has no grammar to break out
    /// of, which is why this is parameterised rather than escaped more carefully.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// The predicate cannot be expressed as Cosmos SQL. SH-H055: this used to be swallowed and
    /// returned as an empty string, which both callers read as "no filter" -- so the aggregate ran
    /// over every document and a tenant-scoped query returned other tenants' rows. Callers must let
    /// this propagate rather than widening to match-all; the ElasticSearch view store settled the
    /// same invariant under CR-H047 / TASK-268.
    /// </exception>
    /// <param name="mapMember">
    /// Optional map from a TView property name to the raw source-document field name. The aggregate
    /// SQL path runs the WHERE against the source documents (FROM c), so a renamed view field must
    /// be translated back to its source name or the predicate targets a non-existent field and
    /// silently matches nothing (CR-H045). Null (LINQ path) leaves names unchanged.
    /// </param>
    public static string Translate<T>(
        Expression<Func<T, bool>> filter,
        IList<KeyValuePair<string, object?>> parameters,
        Func<string, string>? mapMember = null)
    {
        // SH-H055: an explicit `x => true` is the ONE case where an empty clause is the right answer,
        // and it is answered here rather than inside the recursion. Nested, a boolean constant has to
        // render as a real SQL literal or `(c.A = 1 AND )` is a syntax error -- CLAUDE.md § TASK-137,
        // "a strategy asked to render the unrenderable throws; an empty string is silently joined
        // between its neighbours' separators". Kept deliberately narrow: a single ConstantExpression
        // node, never a whitelist of shapes that happen to reduce to true.
        // TASK-867: an ordering against null is false in C#, but `c.Score > null` is undefined in Cosmos DB and so is its
        // NOT, so `!(v.Score > nul) && v.A > 4` missed rows (measured). Folded before the constant check below, so a
        // filter that folds to true is the empty clause too.
        // TASK-896: the store's own pre-pass, CosmosFilterRewrite.Prepare (rule 47's SpanContains, DateTruncation, the
        // fold, and the null semantics that guard a comparison under NOT and read a null or missing member as C#'s null),
        // so a view and a store answer one filter alike. Before it, `!(v.Score > 20)` missed the null rows here too.
        filter = CosmosFilterRewrite.Prepare(filter)!;
        if (filter.Body is ConstantExpression { Value: true })
        {
            return string.Empty;
        }

        return TranslateExpression(filter.Body, parameters, mapMember);
    }

    private static string TranslateExpression(
        Expression expression,
        IList<KeyValuePair<string, object?>> parameters,
        Func<string, string>? mapMember)
    {
        return expression switch
        {
            BinaryExpression binary => TranslateBinary(binary, parameters, mapMember),
            UnaryExpression { NodeType: ExpressionType.Not } unary => $"NOT ({TranslateExpression(unary.Operand, parameters, mapMember)})",
            MethodCallExpression method => TranslateMethodCall(method, parameters, mapMember),
            // SH-H055: `x => false` used to take the unsupported-node throw below, get swallowed, and
            // emit no WHERE -- so it matched EVERY document instead of none. Rendered as a literal it
            // is correct both at the top level and nested inside AND/OR.
            ConstantExpression { Value: bool b } => b ? "true" : "false",
            _ => throw new NotSupportedException($"Expression type {expression.NodeType} is not supported for SQL translation.")
        };
    }

    private static string TranslateBinary(
        BinaryExpression binary,
        IList<KeyValuePair<string, object?>> parameters,
        Func<string, string>? mapMember)
    {
        if (binary.NodeType == ExpressionType.AndAlso)
        {
            return $"({TranslateExpression(binary.Left, parameters, mapMember)} AND {TranslateExpression(binary.Right, parameters, mapMember)})";
        }

        if (binary.NodeType == ExpressionType.OrElse)
        {
            return $"({TranslateExpression(binary.Left, parameters, mapMember)} OR {TranslateExpression(binary.Right, parameters, mapMember)})";
        }

        // TASK-896: a predicate compared with a boolean constant (`v.N.HasValue == false`, `(v.A > 1) == true`).
        if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
            && binary.Left.Type == typeof(bool)
            && Birko.Data.Expressions.SpanContains.UnwrapConvert(binary.Left) is not MemberExpression
            && binary.Right is ConstantExpression { Value: bool expected })
        {
            var predicate = TranslateExpression(binary.Left, parameters, mapMember);
            return expected == (binary.NodeType == ExpressionType.Equal) ? predicate : $"NOT ({predicate})";
        }

        // TASK-902: a comparison a pass produced (a DateTime's wall-clock key, its text range, an ordinal string ordering):
        // its operands may be computed from a view property, and both may read the view.
        var produced = binary.Method?.DeclaringType == typeof(CosmosFilterRewrite);

        var op = binary.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw new NotSupportedException($"Binary operator {binary.NodeType} is not supported.")
        };

        var left = produced ? TranslateOperand(binary.Left, parameters, mapMember) : TranslateFieldAccess(binary.Left, mapMember);
        var right = produced && ParameterFinder.Uses(binary.Right)
            ? TranslateOperand(binary.Right, parameters, mapMember)
            : BindValue(binary.Right, parameters);

        return $"{left} {op} {right}";
    }

    private static string TranslateMethodCall(
        MethodCallExpression method,
        IList<KeyValuePair<string, object?>> parameters,
        Func<string, string>? mapMember)
    {
        // TASK-896: the null-or-missing tests CosmosFilterRewrite emits, on a view property or a path under one.
        if (CosmosFilterRewrite.IsDefinedCall(method) || CosmosFilterRewrite.IsNullCall(method))
        {
            var function = CosmosFilterRewrite.IsDefinedCall(method) ? "IS_DEFINED" : "IS_NULL";
            return $"{function}({TranslateFieldAccess(method.Arguments[0], mapMember)})";
        }

        // TASK-837: only a string's Contains is a substring test. A collection's used to reach the string path and, on a
        // collection-valued field, render CONTAINS(c.Field, @p) over an array, which matches nothing. TASK-839: a
        // collection Contains is ARRAY_CONTAINS, in two shapes only; every other one is refused by name.
        if (method.Method.Name == "Contains" && (method.Object == null || method.Object.Type != typeof(string)))
        {
            return TranslateCollectionContains(method, parameters, mapMember);
        }

        if (method.Method.Name == "Contains" && method.Object != null)
        {
            var field = TranslateFieldAccess(method.Object, mapMember);
            var value = BindValue(method.Arguments[0], parameters);
            return $"CONTAINS({field}, {value})";
        }

        throw new NotSupportedException($"Method {method.Method.Name} is not supported for SQL translation.");
    }

    /// <summary>
    /// TASK-839: a collection <c>Contains</c>, in the two shapes Cosmos SQL's exact <c>ARRAY_CONTAINS</c> expresses:
    /// <list type="bullet">
    /// <item>a caller's list against a view property, <c>ids.Contains(v.Key)</c>: <c>ARRAY_CONTAINS(@p, c.Key)</c>, the
    /// whole list bound as ONE array parameter (the query text does not grow with the list; its serialized values
    /// still count toward the server's 524,288-character limit, so the store splits a long list);</item>
    /// <item>a view array property containing a caller's value, <c>v.Tags.Contains(x)</c>:
    /// <c>ARRAY_CONTAINS(c.Tags, @p)</c>.</item>
    /// </list>
    /// An empty list renders <c>false</c>: it matches nothing, and is never "no constraint" (rule 42). Measured on the
    /// emulator (vnext-preview, 2026-11-07): Guid, int and string values match exactly, with NOT and in a count. Every
    /// other shape is refused by name, before any request: a comparer, a computed operand, a nested
    /// collection, a list that depends on the document and is tested against it, or a test that names no view property.
    /// The message names the shape, never the values.
    /// </summary>
    private static string TranslateCollectionContains(
        MethodCallExpression method,
        IList<KeyValuePair<string, object?>> parameters,
        Func<string, string>? mapMember)
    {
        Expression source;
        Expression item;
        Type elementType;
        if (method.Object == null && method.Method.DeclaringType == typeof(Enumerable) && method.Arguments.Count == 2)
        {
            source = method.Arguments[0];
            item = method.Arguments[1];
            elementType = method.Method.GetGenericArguments()[0];
        }
        else if (method.Object != null && method.Arguments.Count == 1
                 && typeof(IEnumerable<>).MakeGenericType(method.Method.GetParameters()[0].ParameterType).IsAssignableFrom(method.Object.Type))
        {
            source = method.Object;
            item = method.Arguments[0];
            elementType = method.Method.GetParameters()[0].ParameterType;
        }
        else
        {
            // A comparer (Enumerable's or MemoryExtensions' three-argument form, which SpanContains leaves alone) cannot
            // be honoured by ARRAY_CONTAINS's equality.
            throw ContainsRefusal("it is not a plain Contains(value): a comparer or another overload has no Cosmos SQL form");
        }

        if (elementType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(elementType))
        {
            throw ContainsRefusal($"its elements are collections ({elementType.Name}); a nested collection is not supported");
        }

        var sourceReadsDocument = ParameterFinder.Uses(source);
        var itemReadsDocument = ParameterFinder.Uses(item);

        if (!sourceReadsDocument && itemReadsDocument)
        {
            // TASK-902: a DateTime list is a list of wall-clock keys tested against the property's key.
            var path = CosmosFilterRewrite.KeyedOperand(item) != null
                ? TranslateOperand(item, parameters, mapMember)
                : ViewPropertyPath(item, mapMember, "the tested value");
            var values = EvaluateValue(Birko.Data.Expressions.SpanContains.UnwrapConvert(source)!);
            if (values is not System.Collections.IEnumerable sequence || values is string)
            {
                throw ContainsRefusal("its list evaluates to null or to no sequence");
            }

            var bound = new List<object?>();
            foreach (var v in sequence)
            {
                bound.Add(v is Enum e ? Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture) : v);
            }

            if (bound.Count == 0) return "false";
            return $"ARRAY_CONTAINS({Bind(bound.ToArray(), parameters)}, {path})";
        }

        if (sourceReadsDocument && !itemReadsDocument)
        {
            var path = ViewPropertyPath(source, mapMember, "the collection");
            return $"ARRAY_CONTAINS({path}, {BindValue(item, parameters)})";
        }

        throw ContainsRefusal(sourceReadsDocument
            ? "both the collection and the value read the view, which ARRAY_CONTAINS cannot compare"
            : "neither side reads a view property");
    }

    /// <summary>
    /// The document path of a view property or a member path under one (<c>v.Inner.Tags</c>, TASK-840), its first
    /// segment mapped to its source field. A computed operand is refused here, a member that is not a document field in
    /// <see cref="DocumentPath"/>.
    /// </summary>
    private static string ViewPropertyPath(Expression expression, Func<string, string>? mapMember, string role)
    {
        var unwrapped = Birko.Data.Expressions.SpanContains.UnwrapConvert(expression);
        if (unwrapped is MemberExpression member)
        {
            return DocumentPath(member, mapMember);
        }

        throw ContainsRefusal($"{role} is a computed expression ({unwrapped?.NodeType}), not a view property read directly");
    }

    private static NotSupportedException ContainsRefusal(string reason)
        => new("A collection Contains in a Cosmos DB view filter must be a caller's list tested against a view property "
             + "(ids.Contains(v.Col)) or a view array property tested for a caller's value (v.Tags.Contains(x)); this one is "
             + $"not supported: {reason}. (TASK-839)");

    private static string Bind(object? value, IList<KeyValuePair<string, object?>> parameters)
    {
        var name = "@p" + parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        parameters.Add(new KeyValuePair<string, object?>(name, value));
        return name;
    }

    private sealed class ParameterFinder : ExpressionVisitor
    {
        private bool _found;

        public static bool Uses(Expression expression)
        {
            var finder = new ParameterFinder();
            finder.Visit(expression);
            return finder._found;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _found = true;
            return node;
        }
    }

    /// <summary>
    /// TASK-902: an operand of a comparison <c>CosmosFilterRewrite</c> produced: a view property (or a path under one), a
    /// value, and the string functions a DateTime's wall-clock key is built of (<c>SUBSTRING</c>, <c>REPLACE</c>,
    /// <c>INDEX_OF</c>, <c>CONCAT</c>, a conditional), over the property read as its stored text. Anything else is refused.
    /// </summary>
    private static string TranslateOperand(Expression expression, IList<KeyValuePair<string, object?>> parameters, Func<string, string>? mapMember)
    {
        // The key's own constants (positions, and the characters it maps) are written into the statement: they are this
        // translator's, never a caller's, and hold nothing a literal could break out of. A caller's value is bound.
        if (expression is ConstantExpression { Value: int position })
        {
            return position.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (expression is ConstantExpression { Value: string text } && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '+' or '-' or '.' or ':'))
        {
            return "\"" + text + "\"";
        }
        if (!ParameterFinder.Uses(expression)) return BindValue(expression, parameters);
        if (CosmosFilterRewrite.IsDateTimeText(expression))
        {
            return TranslateOperand(((UnaryExpression)((UnaryExpression)expression).Operand).Operand, parameters, mapMember);
        }

        switch (expression)
        {
            case ConditionalExpression conditional:
                return $"({TranslateExpression(conditional.Test, parameters, mapMember)} ? "
                       + $"{TranslateOperand(conditional.IfTrue, parameters, mapMember)} : {TranslateOperand(conditional.IfFalse, parameters, mapMember)})";
            case MethodCallExpression call when call.Method.DeclaringType == typeof(string):
                var function = call.Method.Name switch
                {
                    nameof(string.Substring) when call.Arguments.Count == 2 => "SUBSTRING",
                    nameof(string.Replace) when call.Arguments.Count == 2 => "REPLACE",
                    nameof(string.IndexOf) when call.Arguments.Count == 1 => "INDEX_OF",
                    nameof(string.Concat) when call.Object == null && call.Arguments.Count == 2 => "CONCAT",
                    _ => null,
                };
                if (function == null) break;
                var operands = (call.Object == null ? call.Arguments : call.Arguments.Prepend(call.Object))
                    .Select(a => TranslateOperand(a, parameters, mapMember));
                return $"{function}({string.Join(", ", operands)})";
        }

        if (Birko.Data.Expressions.SpanContains.UnwrapConvert(expression) is MemberExpression member)
        {
            return DocumentPath(member, mapMember);
        }
        throw new NotSupportedException($"A {expression.NodeType} operand of a Cosmos DB view filter is not supported. (TASK-902)");
    }

    private static string TranslateFieldAccess(Expression expression, Func<string, string>? mapMember)
    {
        // Only a conversion is looked through (an enum or a nullable lift); another unary (a negation) is an operation
        // on the field, not the field, and used to render as the bare field.
        if (Birko.Data.Expressions.SpanContains.UnwrapConvert(expression) is MemberExpression member)
        {
            return DocumentPath(member, mapMember);
        }

        throw new NotSupportedException("Cannot translate field access expression.");
    }

    /// <summary>
    /// TASK-840: the document path of a member chain read off the filter's parameter, <c>v.Inner.X</c> →
    /// <c>c["Inner"]["X"]</c> (bracket-quoted, TASK-844). Only the first segment, the view property, goes through the view-to-source map: the projection
    /// selects that source field whole (<c>c["Src"] AS Inner</c>, and <c>Select</c> requires the same type on both
    /// sides), so every deeper name is the same in the view and in the document. Before TASK-840 only the last name was
    /// kept (<c>c.X</c>), which compared a same-named top-level field, or a missing one, with no error.
    /// <c>Nullable&lt;T&gt;.Value</c> is the value itself. Any other member that is not a document field (a string's
    /// <c>Length</c>, a collection's <c>Count</c>, a struct's member) or a chain that does not start at
    /// the view (a captured variable) is refused by name, before any request.
    /// </summary>
    private static string DocumentPath(MemberExpression member, Func<string, string>? mapMember)
    {
        var names = new List<string>();
        Expression? current = member;
        while (Birko.Data.Expressions.SpanContains.UnwrapConvert(current) is MemberExpression m)
        {
            var owner = m.Expression?.Type;
            if (owner != null && Nullable.GetUnderlyingType(owner) != null && m.Member.Name == "Value")
            {
                current = m.Expression;
                continue;
            }

            if (owner != null && !IsDocumentObject(owner))
            {
                throw NotAPath($"it reads '{m.Member.Name}' of {TypeName(owner)}, which is not a field of the stored document");
            }

            names.Add(m.Member.Name);
            current = m.Expression;
        }

        if (Birko.Data.Expressions.SpanContains.UnwrapConvert(current) is not ParameterExpression || names.Count == 0)
        {
            throw NotAPath("the member chain does not read the view's parameter (a captured variable or a static member)");
        }

        names.Reverse();
        // The mapped first segment is the source's dotted path (TASK-843). Every segment is bracket-quoted (TASK-844):
        // dotted, a name like Value is a 400 syntax error (c.Value, measured on the emulator).
        var segments = Map(names[0], mapMember).Split('.').Concat(names.Skip(1)).ToList();
        return CosmosAggregationHelper.PathRef(segments);
    }

    /// <summary>A type whose members are fields of a JSON object: a class that is not a string or a collection.</summary>
    private static bool IsDocumentObject(Type type)
        => type.IsClass && type != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static string TypeName(Type type)
    {
        var tick = type.Name.IndexOf('`');
        return tick < 0 ? type.Name : type.Name.Substring(0, tick);
    }

    private static NotSupportedException NotAPath(string reason)
        => new($"A Cosmos DB view filter must compare a view property or a member path under one (v.Inner.X); this one "
             + $"is not supported: {reason}. (TASK-840)");

    private static string Map(string name, Func<string, string>? mapMember) => mapMember?.Invoke(name) ?? name;

    /// <summary>
    /// Evaluates a value operand to its CLR value. TASK-447: this used to *render* the value into the
    /// statement as a quoted literal; it now only extracts it, and <see cref="BindValue"/> binds it as
    /// a query parameter.
    /// </summary>
    /// <exception cref="NotSupportedException">The operand could not be evaluated to a constant.</exception>
    internal static object? EvaluateValue(Expression expression)
    {
        object? value;

        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
        }
        else
        {
            try
            {
                // Evaluate the expression to get the value
                var lambda = Expression.Lambda(expression);
                var compiled = lambda.Compile();
                value = compiled.DynamicInvoke();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // SH-H055: a parameter-dependent operand (column-vs-column) cannot be evaluated here
                // -- Compile() throws because the parameter is not in scope -- and DynamicInvoke can
                // surface anything the closure does. Both are "this filter cannot be translated", so
                // report them as the type the rest of this translator and the ElasticSearch sibling
                // already use, and let one catch select the whole family.
                //
                // The message carries the node's SHAPE and never the rendered expression: a compiled
                // tree interpolates the values a closure captured, and this message travels into logs
                // and error responses (CLAUDE.md § TASK-308).
                //
                // A cancellation is deliberately NOT rewrapped -- it is the caller's own decision and
                // rewrapping it is the defect § TASK-291 records.
                throw new NotSupportedException(
                    $"A {expression.NodeType} operand could not be evaluated to a constant and cannot "
                    + "be translated to a Cosmos SQL filter.", ex);
            }
        }

        return value;
    }

    /// <summary>
    /// Evaluates a value operand and binds it as a query parameter, returning the placeholder to put
    /// in the statement.
    /// </summary>
    /// <remarks>
    /// TASK-447. Values used to be rendered as quoted literals, escaping <c>'</c> as <c>\'</c> and
    /// nothing else. Cosmos NoSQL uses backslash as the escape character inside a literal, so a
    /// backslash in the *input* consumed the escape the code had just added: measured, an input of
    /// <c>a\' OR 1=1 --</c> rendered as <c>'a\\' OR 1=1 --'</c>, where the literal ends early and the
    /// remainder is parsed as SQL. On an aggregate view the predicate is the only thing scoping the
    /// query, so that widened it to every document with the caller controlling the predicate.
    ///
    /// Parameterised rather than escaped more carefully, because escaping is a blacklist against a
    /// grammar that can grow while a bound parameter has no grammar at all -- CLAUDE.md
    /// § TASK-308/SH-H028, "prefer removing the grammar to escaping it". It also retires the
    /// hand-written literal formatting CR-M086 twice had to correct: the SDK serializes a parameter
    /// with the same serializer that wrote the document, so an enum, a DateTime or a Guid now matches
    /// the stored form by construction instead of by a guess about System.Text.Json's defaults.
    /// </remarks>
    private static string BindValue(Expression expression, IList<KeyValuePair<string, object?>> parameters)
    {
        var value = EvaluateValue(expression);

        // Enums are bound as their underlying integral value. This is the one formatting decision
        // kept from CR-M086, because it is NOT a serialization concern the SDK settles for us: the
        // driver would serialize the enum through its own converter, and the stored documents are
        // written by Birko's own store, which uses the numeric form.
        if (value is Enum e)
        {
            value = Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture);
        }

        return Bind(value, parameters);
    }
}
