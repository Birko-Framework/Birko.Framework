using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        if (_definition.HasAggregates)
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
            query = ApplyOrderBy(query, orderBy);
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

    private static IQueryable<TView> ApplyOrderBy(IQueryable<TView> query, OrderBy<TView> orderBy)
    {
        for (int i = 0; i < orderBy.Fields.Count; i++)
        {
            var field = orderBy.Fields[i];
            var param = Expression.Parameter(typeof(TView), "x");
            var property = Expression.Property(param, field.PropertyName);
            var lambda = Expression.Lambda(property, param);

            var methodName = i == 0
                ? (field.Descending ? "OrderByDescending" : "OrderBy")
                : (field.Descending ? "ThenByDescending" : "ThenBy");

            var method = typeof(Queryable).GetMethods()
                .First(m => m.Name == methodName && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(TView), property.Type);

            query = (IQueryable<TView>)method.Invoke(null, new object[] { query, lambda })!;
        }

        return query;
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
        var sb = new StringBuilder();

        // SELECT clause: group-by fields + aggregate functions
        sb.Append("SELECT ");
        var selectParts = new List<string>();

        foreach (var groupBy in _definition.GroupBy)
        {
            var cosmosField = ToCamelCase(groupBy.PropertyName);
            var viewField = FindViewPropertyForGroupBy(groupBy);
            var alias = ToCamelCase(viewField);
            selectParts.Add($"c.{cosmosField} AS {alias}");
        }

        foreach (var agg in _definition.Aggregates)
        {
            var alias = ToCamelCase(agg.ViewProperty);
            var aggSql = agg.Function switch
            {
                AggregateFunction.Count => "COUNT(1)",
                AggregateFunction.Sum => $"SUM(c.{ToCamelCase(agg.SourceProperty!)})",
                AggregateFunction.Avg => $"AVG(c.{ToCamelCase(agg.SourceProperty!)})",
                AggregateFunction.Min => $"MIN(c.{ToCamelCase(agg.SourceProperty!)})",
                AggregateFunction.Max => $"MAX(c.{ToCamelCase(agg.SourceProperty!)})",
                _ => throw new NotSupportedException($"Aggregate function {agg.Function} is not supported.")
            };
            selectParts.Add($"{aggSql} AS {alias}");
        }

        sb.Append(string.Join(", ", selectParts));

        // FROM clause
        sb.Append(" FROM c");

        // WHERE clause
        if (filter != null)
        {
            var whereClause = CosmosFilterTranslator.Translate(filter);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" WHERE ").Append(whereClause);
            }
        }

        // GROUP BY clause
        if (_definition.HasGroupBy)
        {
            var groupByParts = _definition.GroupBy
                .Select(g => $"c.{ToCamelCase(g.PropertyName)}");
            sb.Append(" GROUP BY ").Append(string.Join(", ", groupByParts));
        }

        // ORDER BY clause
        if (orderBy?.Fields.Count > 0)
        {
            var orderParts = orderBy.Fields
                .Select(f => $"c.{ToCamelCase(f.PropertyName)}{(f.Descending ? " DESC" : " ASC")}");
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
            var whereClause = CosmosFilterTranslator.Translate(filter);
            if (!string.IsNullOrEmpty(whereClause))
            {
                sb.Append(" WHERE ").Append(whereClause);
            }
        }

        if (_definition.HasGroupBy)
        {
            var groupByParts = _definition.GroupBy
                .Select(g => $"c.{ToCamelCase(g.PropertyName)}");
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

    #endregion

    #region Helpers

    /// <summary>
    /// Converts a PascalCase property name to camelCase for Cosmos DB field convention.
    /// </summary>
    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

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
    public static string Translate<T>(Expression<Func<T, bool>> filter)
    {
        try
        {
            return TranslateExpression(filter.Body);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TranslateExpression(Expression expression)
    {
        return expression switch
        {
            BinaryExpression binary => TranslateBinary(binary),
            UnaryExpression { NodeType: ExpressionType.Not } unary => $"NOT ({TranslateExpression(unary.Operand)})",
            MethodCallExpression method => TranslateMethodCall(method),
            _ => throw new NotSupportedException($"Expression type {expression.NodeType} is not supported for SQL translation.")
        };
    }

    private static string TranslateBinary(BinaryExpression binary)
    {
        if (binary.NodeType == ExpressionType.AndAlso)
        {
            return $"({TranslateExpression(binary.Left)} AND {TranslateExpression(binary.Right)})";
        }

        if (binary.NodeType == ExpressionType.OrElse)
        {
            return $"({TranslateExpression(binary.Left)} OR {TranslateExpression(binary.Right)})";
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

        var left = TranslateFieldAccess(binary.Left);
        var right = TranslateValue(binary.Right);

        return $"{left} {op} {right}";
    }

    private static string TranslateMethodCall(MethodCallExpression method)
    {
        if (method.Method.Name == "Contains" && method.Object != null)
        {
            var field = TranslateFieldAccess(method.Object);
            var value = TranslateValue(method.Arguments[0]);
            return $"CONTAINS({field}, {value})";
        }

        throw new NotSupportedException($"Method {method.Method.Name} is not supported for SQL translation.");
    }

    private static string TranslateFieldAccess(Expression expression)
    {
        if (expression is MemberExpression member)
        {
            return $"c.{ToCamelCase(member.Member.Name)}";
        }

        if (expression is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
        {
            return $"c.{ToCamelCase(unaryMember.Member.Name)}";
        }

        throw new NotSupportedException("Cannot translate field access expression.");
    }

    private static string TranslateValue(Expression expression)
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

        return value switch
        {
            null => "null",
            string s => $"'{s.Replace("'", "\\'")}'",
            bool b => b ? "true" : "false",
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Guid g => $"'{g}'",
            _ => value.ToString()!
        };
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
