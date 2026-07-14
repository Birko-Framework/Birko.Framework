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
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        // Take the SQL path for group-by-only (distinct/grouping) views too, not just aggregate views —
        // otherwise a HasGroupBy && !HasAggregates view would return raw ungrouped docs via LINQ (CR-L110).
        if (_definition.HasAggregates || _definition.HasGroupBy)
        {
            return await QueryAggregateAsync(filter, orderBy, limit, offset, ct).ConfigureAwait(false);
        }

        return await QueryLinqAsync(filter, orderBy, limit, offset, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        if (_definition.HasAggregates)
        {
            var results = await QueryAggregateAsync(filter, null, 1, null, ct).ConfigureAwait(false);
            return results.FirstOrDefault();
        }

        return await QueryFirstLinqAsync(filter, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        if (_definition.HasAggregates)
        {
            return await CountAggregateAsync(filter, ct).ConfigureAwait(false);
        }

        return await CountLinqAsync(filter, ct).ConfigureAwait(false);
    }

    #region LINQ-based queries (non-aggregate)

    private async Task<IEnumerable<TView>> QueryLinqAsync(
        Expression<Func<TView, bool>>? filter,
        OrderBy<TView>? orderBy,
        int? limit,
        int? offset,
        CancellationToken ct)
    {
        IQueryable<TView> query = _container.GetItemLinqQueryable<TView>();

        if (filter != null)
        {
            query = query.Where(filter);
        }

        if (orderBy?.Fields.Count > 0)
        {
            query = OrderByHelper.ApplyTo(query, orderBy);
        }

        if (offset.HasValue && offset.Value > 0)
        {
            query = query.Skip(offset.Value);
        }

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        var results = new List<TView>();
        using var iterator = query.ToFeedIterator();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct).ConfigureAwait(false);
            results.AddRange(response);
        }

        return results;
    }

    private async Task<TView?> QueryFirstLinqAsync(
        Expression<Func<TView, bool>>? filter,
        CancellationToken ct)
    {
        IQueryable<TView> query = _container.GetItemLinqQueryable<TView>();

        if (filter != null)
        {
            query = query.Where(filter);
        }

        query = query.Take(1);

        using var iterator = query.ToFeedIterator();
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct).ConfigureAwait(false);
            return response.FirstOrDefault();
        }

        return null;
    }

    private async Task<long> CountLinqAsync(
        Expression<Func<TView, bool>>? filter,
        CancellationToken ct)
    {
        var queryable = _container.GetItemLinqQueryable<TView>();

        if (filter != null)
        {
            return await queryable.Where(filter).CountAsync(ct).ConfigureAwait(false);
        }

        return await queryable.CountAsync(ct).ConfigureAwait(false);
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
        var sql = BuildAggregateSql(filter, orderBy, limit, offset);
        var queryDef = new QueryDefinition(sql);

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
        var sql = BuildCountAggregateSql(filter);
        var queryDef = new QueryDefinition(sql);

        using var iterator = _container.GetItemQueryIterator<CountResult>(queryDef);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct).ConfigureAwait(false);
            var first = response.FirstOrDefault();
            return first?.Count ?? 0;
        }

        return 0;
    }

    private string BuildAggregateSql(
        Expression<Func<TView, bool>>? filter,
        OrderBy<TView>? orderBy,
        int? limit,
        int? offset)
    {
        // Build SELECT ... FROM c and GROUP BY parts separately via shared helper
        var groupByFields = _definition.GroupBy
            .Select(g => (g.PropertyName, FindViewPropertyForGroupBy(g)));
        var aggregateDefs = _definition.Aggregates
            .Select(a => (a.Function, a.SourceProperty, a.ViewProperty));
        var (selectFromSql, groupBySql) = CosmosAggregationHelper.BuildAggregateSqlParts(groupByFields, aggregateDefs);

        var sb = new StringBuilder(selectFromSql);

        // WHERE clause (must come before GROUP BY). Translate view property names to source field
        // names — the query runs against the raw documents (FROM c) (CR-H045).
        if (filter != null)
        {
            var whereClause = CosmosFilterTranslator.Translate(filter, MapViewPropertyToSource);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" WHERE ").Append(whereClause);
            }
        }

        // GROUP BY clause
        if (groupBySql != null)
        {
            sb.Append(groupBySql);
        }

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
            sb.Append($" OFFSET {offset ?? 0} LIMIT {limit ?? int.MaxValue}");
        }

        return sb.ToString();
    }

    private string BuildCountAggregateSql(Expression<Func<TView, bool>>? filter)
    {
        var sb = new StringBuilder();

        // Wrap the aggregate query in a COUNT: SELECT VALUE COUNT(1) FROM (sub-query)
        // Cosmos DB doesn't support sub-queries in FROM, so we count the grouped results
        // by selecting COUNT(1) with the same GROUP BY
        sb.Append("SELECT VALUE COUNT(1) FROM (SELECT c.id FROM c");

        if (filter != null)
        {
            var whereClause = CosmosFilterTranslator.Translate(filter, MapViewPropertyToSource);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" WHERE ").Append(whereClause);
            }
        }

        if (_definition.HasGroupBy)
        {
            var groupByParts = _definition.GroupBy
                .Select(g => $"c.{g.PropertyName}");
            sb.Append(" GROUP BY ").Append(string.Join(", ", groupByParts));
        }

        sb.Append(')');

        return sb.ToString();
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

    #region Helpers

    /// <summary>
    /// Internal result type for COUNT queries.
    /// </summary>
    private sealed class CountResult
    {
        public long Count { get; set; }
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
    /// Returns an empty string if the expression cannot be translated.
    /// </summary>
    /// <param name="mapMember">
    /// Optional map from a TView property name to the raw source-document field name. The aggregate
    /// SQL path runs the WHERE against the source documents (FROM c), so a renamed view field must
    /// be translated back to its source name or the predicate targets a non-existent field and
    /// silently matches nothing (CR-H045). Null (LINQ path) leaves names unchanged.
    /// </param>
    public static string Translate<T>(Expression<Func<T, bool>> filter, Func<string, string>? mapMember = null)
    {
        try
        {
            return TranslateExpression(filter.Body, mapMember);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TranslateExpression(Expression expression, Func<string, string>? mapMember)
    {
        return expression switch
        {
            BinaryExpression binary => TranslateBinary(binary, mapMember),
            UnaryExpression { NodeType: ExpressionType.Not } unary => $"NOT ({TranslateExpression(unary.Operand, mapMember)})",
            MethodCallExpression method => TranslateMethodCall(method, mapMember),
            _ => throw new NotSupportedException($"Expression type {expression.NodeType} is not supported for SQL translation.")
        };
    }

    private static string TranslateBinary(BinaryExpression binary, Func<string, string>? mapMember)
    {
        if (binary.NodeType == ExpressionType.AndAlso)
        {
            return $"({TranslateExpression(binary.Left, mapMember)} AND {TranslateExpression(binary.Right, mapMember)})";
        }

        if (binary.NodeType == ExpressionType.OrElse)
        {
            return $"({TranslateExpression(binary.Left, mapMember)} OR {TranslateExpression(binary.Right, mapMember)})";
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
        var right = TranslateValue(binary.Right);

        return $"{left} {op} {right}";
    }

    private static string TranslateMethodCall(MethodCallExpression method, Func<string, string>? mapMember)
    {
        if (method.Method.Name == "Contains" && method.Object != null)
        {
            var field = TranslateFieldAccess(method.Object, mapMember);
            var value = TranslateValue(method.Arguments[0]);
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

    internal static string TranslateValue(Expression expression)
    {
        object? value;

        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
        }
        else
        {
            // Evaluate the expression to get the value
            var lambda = Expression.Lambda(expression);
            var compiled = lambda.Compile();
            value = compiled.DynamicInvoke();
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return value switch
        {
            null => "null",
            string s => $"'{s.Replace("'", "\\'")}'",
            bool b => b ? "true" : "false",
            // Enums default to numeric serialization (System.Text.Json); emit the numeric value, not
            // the unquoted member name which would be invalid SQL (CR-M086).
            Enum e => Convert.ToInt64(e, invariant).ToString(invariant),
            // Quote temporal values as ISO-8601 (matches System.Text.Json's default DateTime format);
            // the raw ToString() fallback emitted a culture-dependent, unquoted, invalid literal (CR-M086).
            DateTime dt => $"'{dt.ToString("o", invariant)}'",
            DateTimeOffset dto => $"'{dto.ToString("o", invariant)}'",
            decimal d => d.ToString(invariant),
            double d => d.ToString(invariant),
            float f => f.ToString(invariant),
            Guid g => $"'{g}'",
            _ => value.ToString()!
        };
    }

}
