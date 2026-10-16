using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.Stores;
using Birko.Data.Views;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace Birko.Data.CosmosDB.Views;

/// <summary>
/// Azure Cosmos DB (NoSQL API) implementation of <see cref="IViewStore{TView}"/>.
/// For non-aggregate views, uses LINQ via <c>container.GetItemLinqQueryable</c>.
/// For aggregate views, builds Cosmos SQL with GROUP BY and executes via <c>GetItemQueryIterator</c>.
/// Joins are not supported (Cosmos DB does not support cross-container joins).
/// </summary>
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

    /// <inheritdoc />
    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        RequireComputable();

        // Take the SQL path for group-by-only (distinct/grouping) views too, not just aggregate views —
        // otherwise a HasGroupBy && !HasAggregates view would return raw ungrouped docs via LINQ (CR-L110).
        if (Grouped)
        {
            return await QueryAggregateAsync(filter, orderBy, limit, offset, ct).ConfigureAwait(false);
        }

        return await RunAsync<TView>(BuildProjectionSql(filter, orderBy, limit, offset), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        RequireComputable();

        // TASK-151 (SH-M379): grouped on the same condition as QueryAsync; a group-by-only view answered a raw document.
        if (Grouped)
        {
            var results = await QueryAggregateAsync(filter, null, 1, null, ct).ConfigureAwait(false);
            return results.FirstOrDefault();
        }

        return (await RunAsync<TView>(BuildProjectionSql(filter, null, 1, null), ct).ConfigureAwait(false)).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        RequireComputable();

        if (Grouped)
        {
            return await CountAggregateAsync(filter, ct).ConfigureAwait(false);
        }

        var parameters = new List<KeyValuePair<string, object?>>();
        var sql = "SELECT VALUE COUNT(1) FROM c" + Where(filter, parameters);
        return (await RunAsync<long>(Bind(sql, parameters), ct).ConfigureAwait(false)).Sum();
    }

    #region Projection (non-aggregate)

    /// <summary>
    /// SELECT each view field from its source field, under the view's name.
    /// </summary>
    /// <remarks>
    /// TASK-151 (SH-M410): the LINQ path deserialised the raw documents into the view, so a renamed field stayed
    /// default, and a filter or order on a view name tested a document field that does not exist.
    /// </remarks>
    private QueryDefinition BuildProjectionSql(Expression<Func<TView, bool>>? filter, OrderBy<TView>? orderBy, int? limit, int? offset)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        var sb = new StringBuilder("SELECT ");
        sb.Append(_definition.Fields.Count == 0
            ? "VALUE c"
            : string.Join(", ", _definition.Fields.Select(f => $"{CosmosAggregationHelper.FieldRef(f.SourceProperty)} AS {f.ViewProperty}")));
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
        // field and reference aggregate results by their SELECT alias (CR-H044).
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

        return results;
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
        var groupByFields = _definition.GroupBy
            .Select(g => (g.PropertyName, FindViewPropertyForGroupBy(g)));
        var aggregateDefs = _definition.Aggregates
            .Select(a => (a.Function, a.SourceProperty, a.ViewProperty));
        var (selectFromSql, groupBySql) = CosmosAggregationHelper.BuildAggregateSqlParts(groupByFields, aggregateDefs);

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

        var field = _definition.Fields.FirstOrDefault(f => f.ViewProperty == viewProperty);
        return field?.SourceProperty ?? viewProperty;
    }

    /// <summary>
    /// Resolves an ORDER BY key expressed as a TView property name (CR-H044): a group key maps back
    /// to its source field (<c>c.{source}</c>); an aggregate result orders by its SELECT alias.
    /// </summary>
    private string ResolveOrderKey(string viewProperty)
    {
        // Aggregate result column — order by the projected alias, not a raw document field.
        if (_definition.Aggregates.Any(a => a.ViewProperty == viewProperty))
        {
            return viewProperty;
        }

        // Group key / passthrough field — order by the source field the GROUP BY uses.
        return $"c.{MapViewPropertyToSource(viewProperty)}";
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

        var left = TranslateFieldAccess(binary.Left, mapMember);
        var right = BindValue(binary.Right, parameters);

        return $"{left} {op} {right}";
    }

    private static string TranslateMethodCall(
        MethodCallExpression method,
        IList<KeyValuePair<string, object?>> parameters,
        Func<string, string>? mapMember)
    {
        if (method.Method.Name == "Contains" && method.Object != null)
        {
            var field = TranslateFieldAccess(method.Object, mapMember);
            var value = BindValue(method.Arguments[0], parameters);
            return $"CONTAINS({field}, {value})";
        }

        throw new NotSupportedException($"Method {method.Method.Name} is not supported for SQL translation.");
    }

    private static string TranslateFieldAccess(Expression expression, Func<string, string>? mapMember)
    {
        if (expression is MemberExpression member)
        {
            return $"c.{Map(member.Member.Name, mapMember)}";
        }

        if (expression is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
        {
            return $"c.{Map(unaryMember.Member.Name, mapMember)}";
        }

        throw new NotSupportedException("Cannot translate field access expression.");
    }

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

        var name = "@p" + parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        parameters.Add(new KeyValuePair<string, object?>(name, value));
        return name;
    }
}
